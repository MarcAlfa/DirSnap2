using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace DirSnap2.Services;

public sealed class TextFileReadResult
{
    public string Content { get; set; } = string.Empty;
    public Encoding Encoding { get; set; } = Encoding.UTF8;
    public string Newline { get; set; } = "\r\n";
    public bool HasBom { get; set; }
}

public interface ITextFileService
{
    TextFileReadResult ReadText(string path);
    void WriteText(string path, string text, Encoding encoding, string newline, bool writeBom);
    string? ValidateJson(string text);
    string FormatJson(string text);
}

public sealed class TextFileService : ITextFileService
{
    public const long MaxEditableSize = 5 * 1024 * 1024; // 5 MB

    static TextFileService()
    {
        // .NET (Core) ships only Unicode/Latin1/ASCII by default: legacy code pages
        // such as Windows-1252 need this provider to be registered first.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Converts every newline style (CRLF, lone CR, LF) to LF.
    /// The WinUI 3 TextBox hands text back with lone CR line endings.
    /// </summary>
    public static string NormalizeNewlines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.IndexOf('\r') < 0
            ? text
            : text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>
    /// Ordinal comparison that treats CRLF, CR and LF as the same line break (no allocations).
    /// </summary>
    public static bool EqualsIgnoringNewlineStyle(string? a, string? b)
    {
        a ??= string.Empty;
        b ??= string.Empty;

        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            char ca = a[i], cb = b[j];
            bool na = ca is '\r' or '\n';
            bool nb = cb is '\r' or '\n';
            if (na != nb)
                return false;

            if (na)
            {
                i += ca == '\r' && i + 1 < a.Length && a[i + 1] == '\n' ? 2 : 1;
                j += cb == '\r' && j + 1 < b.Length && b[j + 1] == '\n' ? 2 : 1;
            }
            else
            {
                if (ca != cb)
                    return false;
                i++;
                j++;
            }
        }

        return i == a.Length && j == b.Length;
    }

    public static string GetNewlineDisplayName(string newline) => newline switch
    {
        "\r\n" => "CRLF",
        "\r" => "CR",
        _ => "LF"
    };

    public TextFileReadResult ReadText(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"File not found: {path}");
        }

        if (fileInfo.Length > MaxEditableSize)
        {
            throw new InvalidOperationException(
                $"File too large to edit ({fileInfo.Length / (1024.0 * 1024.0):F1} MB, maximum allowed is 5 MB).");
        }

        byte[] raw = File.ReadAllBytes(path);

        // Binary check: search for null byte in the first 8KB
        int checkLen = Math.Min(raw.Length, 8192);
        for (int i = 0; i < checkLen; i++)
        {
            if (raw[i] == 0)
            {
                throw new InvalidOperationException("Binary file detected, cannot be edited.");
            }
        }

        // Encoding detection
        Encoding encoding = Encoding.UTF8;
        bool hasBom = false;

        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
        {
            hasBom = true;
            encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        }
        else
        {
            // Try strict UTF-8 without BOM
            try
            {
                var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
                utf8Strict.GetString(raw);
                encoding = new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
                // Fallback to ANSI / Windows-1252 / Latin-1
                try
                {
                    encoding = Encoding.GetEncoding(1252);
                }
                catch
                {
                    encoding = Encoding.Latin1;
                }
            }
        }

        string text = encoding.GetString(raw);
        if (hasBom && text.StartsWith("\uFEFF"))
        {
            text = text.Substring(1);
        }

        // Newline detection
        string originalNewline =
            text.Contains("\r\n") ? "\r\n"
            : text.Contains('\n') ? "\n"
            : text.Contains('\r') ? "\r"
            : "\n";
        string normalized = NormalizeNewlines(text);

        return new TextFileReadResult
        {
            Content = normalized,
            Encoding = encoding,
            Newline = originalNewline,
            HasBom = hasBom
        };
    }

    public void WriteText(string path, string text, Encoding encoding, string newline, bool writeBom)
    {
        // The editor TextBox returns lone CR line endings: normalize all styles first.
        string normalized = NormalizeNewlines(text);
        string converted = newline != "\n" ? normalized.Replace("\n", newline) : normalized;

        Encoding targetEncoding = encoding;
        if (encoding is UTF8Encoding)
        {
            targetEncoding = new UTF8Encoding(writeBom);
        }

        else
        {
            // Legacy code pages (ANSI/Latin1): refuse characters they cannot store instead of
            // silently writing '?' in their place.
            targetEncoding = Encoding.GetEncoding(
                encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        }

        byte[] body;
        try
        {
            body = targetEncoding.GetBytes(converted);
        }
        catch (EncoderFallbackException ex)
        {
            throw new InvalidOperationException(
                $"the character '{ex.CharUnknown}' cannot be stored in this file's encoding ({encoding.EncodingName}). Remove it and save again.");
        }

        byte[] preamble = targetEncoding.GetPreamble();

        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var tempFile = Path.Combine(dir, $".dirsnap_{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(preamble, 0, preamble.Length);
                stream.Write(body, 0, body.Length);
            }

            if (File.Exists(path))
            {
                try
                {
                    // Keeps attributes (hidden…), creation time and ACLs of the original file.
                    File.Replace(tempFile, path, destinationBackupFileName: null);
                }
                catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
                {
                    // Some volumes/shares do not support ReplaceFile: fall back to a plain move.
                    if (!File.Exists(tempFile))
                        throw;
                    File.Move(tempFile, path, overwrite: true);
                }
            }
            else
            {
                File.Move(tempFile, path);
            }
        }
        catch
        {
            try
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
            catch
            {
                // Ignore cleanup error
            }
            throw;
        }
    }

    public string? ValidateJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return null; // Valid
        }
        catch (JsonException ex)
        {
            return $"Line {ex.LineNumber}, Byte {ex.BytePositionInLine}: {ex.Message}";
        }
    }

    public string FormatJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        return JsonSerializer.Serialize(doc.RootElement, options) + "\n";
    }
}
