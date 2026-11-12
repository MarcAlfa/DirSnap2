using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DirSnap2.Services;

/// <summary>One provider the AI Assistant can talk to.</summary>
public sealed record AiProviderInfo(
    string Id,
    string DisplayName,
    string DefaultModel,
    string[] SuggestedModels,
    bool RequiresApiKey,
    bool UsesBaseUrl);

/// <summary>Supported providers. Model names are free text: the suggestions are only a starting point.</summary>
public static class AiProviders
{
    public const string Gemini = "gemini";
    public const string OpenAi = "openai";
    public const string Anthropic = "anthropic";
    public const string Moonshot = "moonshot";
    public const string OpenAiCompatible = "openai_compatible";

    /// <summary>Moonshot AI (Kimi) international platform, OpenAI-compatible API.</summary>
    public const string MoonshotBaseUrl = "https://api.moonshot.ai/v1";

    public static readonly AiProviderInfo[] All =
    {
        new(Gemini, "Google Gemini", "gemini-3.8-flash",
            new[] { "gemini-3.8-flash", "gemini-4-argon", "gemini-4-carbon" }, true, false),
        new(OpenAi, "OpenAI", "chat-latest",
            new[] { "chat-latest", "gpt-6.1-terra", "gpt-6.1-sol" }, true, false),
        new(Anthropic, "Anthropic Claude", "claude-opus-5-5",
            new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" }, true, false),
        new(Moonshot, "Moonshot AI (Kimi)", "kimi-k3.1-pro",
            new[] { "kimi-k3.1-pro", "kimi-k3.1-pro-highspeed" }, true, false),
        new(OpenAiCompatible, "OpenAI-compatible (Ollama, Groq, OpenRouter, DeepSeek...)", "llama3.1",
            new[] { "llama3.1", "qwen2.5-coder", "deepseek-chat" }, false, true),
    };

    /// <summary>Short label for the header badge.</summary>
    public static string ShortName(string id) => id switch
    {
        Gemini => "Gemini",
        OpenAi => "OpenAI",
        Anthropic => "Claude",
        Moonshot => "Kimi",
        OpenAiCompatible => "Compatible",
        _ => id
    };

    public static AiProviderInfo Get(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? All[0];
}

/// <summary>A message of the conversation as sent to the provider.</summary>
public sealed record AiChatTurn(bool IsUser, string Text);

/// <summary>Settings of one request (resolved from the config at send time).</summary>
public sealed record AiRequestSettings(string Provider, string Model, string ApiKey, string BaseUrl);

public interface IAiService
{
    /// <summary>Streams the reply text chunk by chunk. Throws <see cref="AiServiceException"/> on API errors.</summary>
    IAsyncEnumerable<string> StreamChatAsync(
        AiRequestSettings settings,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        CancellationToken cancellationToken);

    /// <summary>Sends a tiny request and returns a short human-readable result.</summary>
    Task<string> TestConnectionAsync(AiRequestSettings settings, CancellationToken cancellationToken);
}

public sealed class AiServiceException : Exception
{
    public AiServiceException(string message) : base(message) { }
}

/// <summary>
/// Streaming chat over plain HttpClient (Server-Sent Events) for three API formats:
/// OpenAI chat completions (also Ollama, Groq, OpenRouter, DeepSeek), Google Gemini and Anthropic Messages.
/// Never logs API keys or message contents.
/// </summary>
public sealed class AiService : IAiService
{
    private const string AnthropicApiVersion = "2026-10-21";
    private const int AnthropicMaxTokens = 64000;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(20),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    })
    {
        // Replies are streamed and can take minutes: cancellation comes from the Stop button
        Timeout = Timeout.InfiniteTimeSpan
    };

    public async IAsyncEnumerable<string> StreamChatAsync(
        AiRequestSettings settings,
        string systemPrompt,
        IReadOnlyList<AiChatTurn> turns,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var provider = AiProviders.Get(settings.Provider);
        if (provider.RequiresApiKey && string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new AiServiceException($"No API key set for {provider.DisplayName}. Add it in Settings → AI Assistant.");
        if (string.IsNullOrWhiteSpace(settings.Model))
            throw new AiServiceException("No model set. Choose one in Settings → AI Assistant.");

        AppLogger.Info($"[AiService] Request: provider={provider.Id}, model={settings.Model}, turns={turns.Count}");

        using var response = await SendWithRetryAsync(
            () => BuildRequest(provider.Id, settings, systemPrompt, turns), cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        // SSE: "data:" lines carry the JSON events, blank lines separate events
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                break;
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var data = line.Substring(5).Trim();
            if (data.Length == 0)
                continue;
            if (data == "[DONE]")
                break;

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }
            if (node == null)
                continue;

            var (text, done) = provider.Id switch
            {
                AiProviders.Gemini => ParseGeminiEvent(node),
                AiProviders.Anthropic => ParseAnthropicEvent(node),
                _ => ParseOpenAiEvent(node)
            };

            if (!string.IsNullOrEmpty(text))
                yield return text;
            if (done)
                break;
        }
    }

    public async Task<string> TestConnectionAsync(AiRequestSettings settings, CancellationToken cancellationToken)
    {
        var turns = new[] { new AiChatTurn(true, "Reply with the single word OK.") };
        var sb = new StringBuilder();
        await foreach (var chunk in StreamChatAsync(settings, "You are a connection test.", turns, cancellationToken))
        {
            sb.Append(chunk);
            if (sb.Length > 40)
                break;
        }
        var reply = sb.ToString().Trim();
        return reply.Length == 0
            ? $"Connected to {settings.Model}."
            : $"Connected to {settings.Model}. Reply: \"{(reply.Length > 40 ? reply[..40] + "…" : reply)}\"";
    }

    // ==================== HTTP ====================

    /// <summary>Sends the request; one retry after a short pause on rate limit / overload / server errors.</summary>
    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> buildRequest, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            HttpResponseMessage response;
            try
            {
                using var request = buildRequest();
                response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new AiServiceException($"Cannot reach the AI provider: {ex.Message}");
            }

            if (response.IsSuccessStatusCode)
                return response;

            int status = (int)response.StatusCode;
            string body = string.Empty;
            try
            {
                body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // body not readable: keep the status only
            }
            response.Dispose();

            bool retryable = status is 429 or 500 or 502 or 503 or 504 or 529;
            if (retryable && attempt == 0)
            {
                AppLogger.Warn($"[AiService] HTTP {status}, retrying once");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                continue;
            }

            AppLogger.Warn($"[AiService] HTTP {status}");
            throw new AiServiceException(DescribeHttpError(status, body));
        }
    }

    private static string DescribeHttpError(int status, string body)
    {
        string? apiMessage = null;
        try
        {
            var node = JsonNode.Parse(body);
            // OpenAI / Anthropic: { "error": { "message": ... } }, Gemini: { "error": { "message": ... } } (also as array)
            var err = node is JsonArray arr && arr.Count > 0 ? arr[0]?["error"] : node?["error"];
            apiMessage = err is JsonValue ? err.GetValue<string>() : err?["message"]?.GetValue<string>();
        }
        catch
        {
            // not JSON
        }

        string prefix = status switch
        {
            400 => "Bad request",
            401 => "Invalid API key",
            403 => "Access denied (check the API key and its permissions)",
            404 => "Not found (check the model name and the Base URL)",
            429 => "Rate limit or quota exceeded",
            529 => "The provider is overloaded, try again shortly",
            >= 500 => "Provider error",
            _ => "Request failed"
        };
        return string.IsNullOrWhiteSpace(apiMessage)
            ? $"{prefix} (HTTP {status})."
            : $"{prefix} (HTTP {status}): {apiMessage}";
    }

    private static HttpRequestMessage BuildRequest(
        string providerId, AiRequestSettings s, string systemPrompt, IReadOnlyList<AiChatTurn> turns)
    {
        return providerId switch
        {
            AiProviders.Gemini => BuildGeminiRequest(s, systemPrompt, turns),
            AiProviders.Anthropic => BuildAnthropicRequest(s, systemPrompt, turns),
            AiProviders.OpenAi => BuildOpenAiRequest("https://api.openai.com/v1", s, systemPrompt, turns),
            AiProviders.Moonshot => BuildOpenAiRequest(AiProviders.MoonshotBaseUrl, s, systemPrompt, turns),
            _ => BuildOpenAiRequest(s.BaseUrl, s, systemPrompt, turns)
        };
    }

    private static StringContent JsonBody(JsonNode body) =>
        new(body.ToJsonString(), Encoding.UTF8, "application/json");

    // ==================== OPENAI (and compatible) ====================

    private static HttpRequestMessage BuildOpenAiRequest(
        string baseUrl, AiRequestSettings s, string systemPrompt, IReadOnlyList<AiChatTurn> turns)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl.Trim().TrimEnd('/') + "/chat/completions", UriKind.Absolute, out var uri))
            throw new AiServiceException("Invalid Base URL. Set it in Settings → AI Assistant (e.g. http://localhost:11434/v1).");

        var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = systemPrompt } };
        foreach (var t in turns)
            messages.Add(new JsonObject { ["role"] = t.IsUser ? "user" : "assistant", ["content"] = t.Text });

        var body = new JsonObject
        {
            ["model"] = s.Model,
            ["stream"] = true,
            ["messages"] = messages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonBody(body) };
        if (!string.IsNullOrWhiteSpace(s.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return request;
    }

    private static (string? Text, bool Done) ParseOpenAiEvent(JsonNode node)
    {
        if (node["error"] is { } err)
            throw new AiServiceException($"Provider error: {err["message"]?.GetValue<string>() ?? err.ToJsonString()}");

        var choice = node["choices"] is JsonArray choices && choices.Count > 0 ? choices[0] : null;
        if (choice == null)
            return (null, false);

        string? text = choice["delta"]?["content"] is JsonValue v && v.TryGetValue<string>(out var c) ? c : null;
        string? finish = choice["finish_reason"] is JsonValue f && f.TryGetValue<string>(out var fr) ? fr : null;
        if (finish == "length")
            text += "\n\n[Reply cut: maximum length reached]";
        else if (finish == "content_filter")
            text += "\n\n[Reply stopped by the provider's content filter]";
        return (text, false); // the stream ends with "data: [DONE]"
    }

    // ==================== GEMINI ====================

    private static HttpRequestMessage BuildGeminiRequest(
        AiRequestSettings s, string systemPrompt, IReadOnlyList<AiChatTurn> turns)
    {
        var contents = new JsonArray();
        foreach (var t in turns)
        {
            contents.Add(new JsonObject
            {
                ["role"] = t.IsUser ? "user" : "model",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = t.Text } }
            });
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemPrompt } }
            },
            ["contents"] = contents
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(s.Model)}:streamGenerateContent?alt=sse";
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonBody(body) };
        // Key in a header, not in the URL, so it never ends up in logs or proxies
        request.Headers.Add("x-goog-api-key", s.ApiKey);
        return request;
    }

    private static (string? Text, bool Done) ParseGeminiEvent(JsonNode node)
    {
        if (node["error"] is { } err)
            throw new AiServiceException($"Provider error: {err["message"]?.GetValue<string>() ?? err.ToJsonString()}");

        if (node["promptFeedback"]?["blockReason"] is JsonValue block)
            throw new AiServiceException($"The request was blocked by the provider ({block}).");

        var candidate = node["candidates"] is JsonArray cands && cands.Count > 0 ? cands[0] : null;
        if (candidate == null)
            return (null, false);

        var sb = new StringBuilder();
        if (candidate["content"]?["parts"] is JsonArray parts)
        {
            foreach (var part in parts)
            {
                // "thought" parts are the model's reasoning summary: not part of the reply
                if (part?["thought"] is JsonValue th && th.TryGetValue<bool>(out var isThought) && isThought)
                    continue;
                if (part?["text"] is JsonValue tv && tv.TryGetValue<string>(out var txt))
                    sb.Append(txt);
            }
        }

        string? finish = candidate["finishReason"] is JsonValue f && f.TryGetValue<string>(out var fr) ? fr : null;
        if (finish == "MAX_TOKENS")
            sb.Append("\n\n[Reply cut: maximum length reached]");
        else if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "RECITATION")
            sb.Append($"\n\n[Reply stopped by the provider ({finish})]");

        return (sb.ToString(), false);
    }

    // ==================== ANTHROPIC ====================

    private static HttpRequestMessage BuildAnthropicRequest(
        AiRequestSettings s, string systemPrompt, IReadOnlyList<AiChatTurn> turns)
    {
        var messages = new JsonArray();
        foreach (var t in turns)
            messages.Add(new JsonObject { ["role"] = t.IsUser ? "user" : "assistant", ["content"] = t.Text });

        // API 2026-10-21: no temperature/top_p/top_k. Current models think adaptively by default;
        // thinking blocks are ignored, only the text deltas are shown.
        var body = new JsonObject
        {
            ["model"] = s.Model,
            ["max_tokens"] = AnthropicMaxTokens,
            ["stream"] = true,
            ["system"] = systemPrompt,
            ["messages"] = messages
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages") { Content = JsonBody(body) };
        request.Headers.Add("x-api-key", s.ApiKey);
        request.Headers.Add("anthropic-version", AnthropicApiVersion);
        return request;
    }

    private static (string? Text, bool Done) ParseAnthropicEvent(JsonNode node)
    {
        string? type = node["type"]?.GetValue<string>();
        switch (type)
        {
            case "content_block_delta":
                if (node["delta"]?["type"]?.GetValue<string>() == "text_delta")
                    return (node["delta"]?["text"]?.GetValue<string>(), false);
                return (null, false);

            case "message_delta":
                string? stop = node["delta"]?["stop_reason"] is JsonValue sv && sv.TryGetValue<string>(out var sr) ? sr : null;
                return stop switch
                {
                    "max_tokens" => ("\n\n[Reply cut: maximum length reached]", false),
                    "refusal" => ("\n\n[The model declined to answer this request]", false),
                    _ => (null, false)
                };

            case "message_stop":
                return (null, true);

            case "error":
                var err = node["error"];
                string errType = err?["type"]?.GetValue<string>() ?? "error";
                string msg = err?["message"]?.GetValue<string>() ?? "unknown error";
                throw new AiServiceException(errType == "overloaded_error"
                    ? "The provider is overloaded, try again shortly."
                    : $"Provider error: {msg}");

            default:
                return (null, false);
        }
    }
}

/// <summary>
/// API keys encrypted with Windows DPAPI (CurrentUser scope): config.json holds only Base64 ciphertext,
/// readable only by the same Windows user on the same PC.
/// </summary>
public static class AiKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DirSnap2.AiKey.v1");

    public static string Protect(string plainKey)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plainKey), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string? Unprotect(string? encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            // Config copied from another user/PC: the key cannot be decrypted and must be entered again
            AppLogger.Warn($"[AiKeyStore] Cannot decrypt the saved API key: {ex.GetType().Name}");
            return null;
        }
    }

    public static string? GetKey(Models.AppConfig config, string providerId) =>
        config.AiApiKeysEncrypted.TryGetValue(providerId, out var enc) ? Unprotect(enc) : null;

    public static bool HasKey(Models.AppConfig config, string providerId) =>
        config.AiApiKeysEncrypted.TryGetValue(providerId, out var enc) && !string.IsNullOrWhiteSpace(enc);

    public static void SetKey(Models.AppConfig config, string providerId, string? plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey))
            config.AiApiKeysEncrypted.Remove(providerId);
        else
            config.AiApiKeysEncrypted[providerId] = Protect(plainKey.Trim());
    }

    /// <summary>Resolves provider, model, key and base URL from the current config.</summary>
    public static AiRequestSettings ResolveSettings(Models.AppConfig config)
    {
        var provider = AiProviders.Get(config.AiProvider);
        string model = config.AiModels.TryGetValue(provider.Id, out var m) && !string.IsNullOrWhiteSpace(m)
            ? m.Trim()
            : provider.DefaultModel;
        return new AiRequestSettings(provider.Id, model, GetKey(config, provider.Id) ?? string.Empty, config.AiBaseUrl ?? string.Empty);
    }
}
