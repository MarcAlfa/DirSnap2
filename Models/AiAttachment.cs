using Microsoft.UI.Xaml.Media;

namespace DirSnap2.Models;

/// <summary>An image sent to the AI provider: PNG or JPEG, Base64-encoded.</summary>
public sealed record AiImage(string MediaType, string Base64);

/// <summary>
/// Something pasted into the AI chat input (Ctrl+V), waiting to be sent with the next question:
/// an image (screenshot or image file), sent as an image, or another file, attached as context.
/// </summary>
public sealed class AiAttachment
{
    public required string Name { get; init; }

    /// <summary>File to attach as text context (null for an image).</summary>
    public string? FilePath { get; init; }

    /// <summary>The image to send (null for a file).</summary>
    public AiImage? Image { get; init; }

    /// <summary>Small preview of the image, shown in the chip and in the sent question.</summary>
    public ImageSource? Thumbnail { get; init; }

    /// <summary>Bytes sent (the re-encoded image) or the file size.</summary>
    public long Size { get; init; }

    public bool IsImage => Image != null;
    public bool IsFile => Image == null;
    public string SizeText => FSItem.FormatSize(Size);
    public string ToolTip => FilePath ?? Name;
}
