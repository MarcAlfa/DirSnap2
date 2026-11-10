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
        string originalNewline = text.Contains("\r\n") ? "\r\n" : "\n";
        string normalized = text.Replace("\r\n", "\n");

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
        string normalized = text.Replace("\r\n", "\n");
        string converted = newline != "\n" ? normalized.Replace("\n", newline) : normalized;

        Encoding targetEncoding = encoding;
        if (encoding is UTF8Encoding)
        {
            targetEncoding = new UTF8Encoding(writeBom);
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var tempFile = Path.Combine(dir, $".dirsnap_{Guid.NewGuid():N}.tmp");

        try
        {
            using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, targetEncoding))
            {
                writer.Write(converted);
            }

            File.Move(tempFile, path, overwrite: true);
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
