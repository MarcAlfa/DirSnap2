using System.Collections.ObjectModel;
using System.IO;
using DirSnap2.Models;
using DirSnap2.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Streams;

namespace DirSnap2.ViewModels;

/// <summary>
/// Ctrl+V in the chat input: a screenshot (bitmap on the clipboard) or files copied in Explorer are
/// attached to the next question. Images are sent as images (and stay in the chat history, so
/// follow-up questions still see them); other files are attached as text context, like a tab's file.
/// </summary>
public sealed partial class AiChatViewModel
{
    private const int MaxPendingImages = 5;
    private const int MaxPendingAttachments = 10;

    public ObservableCollection<AiAttachment> Attachments { get; } = new();

    public bool HasAttachments => Attachments.Count > 0;

    private void InitAttachments()
    {
        Attachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAttachments));
            ScheduleContextEstimate();
        };
    }

    /// <summary>
    /// True when Ctrl+V should attach instead of pasting text: files copied in Explorer, or an
    /// image without text (copying cells from Excel also puts a picture on the clipboard: that stays text).
    /// </summary>
    public static bool HasPastableAttachment(DataPackageView content) =>
        content.Contains(StandardDataFormats.StorageItems) ||
        (content.Contains(StandardDataFormats.Bitmap) && !content.Contains(StandardDataFormats.Text));

    public async Task PasteAttachmentsAsync(DataPackageView content)
    {
        try
        {
            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                int folders = 0;
                foreach (var item in items)
                {
                    if (item is StorageFile file)
                        await AddFileAsync(file);
                    else
                        folders++;
                }
                if (folders > 0)
                    StatusText = "Folders cannot be attached: copy the files inside them.";
            }
            else if (content.Contains(StandardDataFormats.Bitmap))
            {
                var reference = await content.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                await AddImageAsync($"Screenshot {DateTime.Now:HH.mm.ss}.png", stream);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[AiChat] Paste: {ex.Message}");
            StatusText = $"Cannot paste: {ex.Message}";
        }
    }

    private async Task AddFileAsync(StorageFile file)
    {
        if (!CanAddAttachment()) return;

        if (AiImageHelper.ImageExtensions.Contains(Path.GetExtension(file.Name)))
        {
            using var stream = await file.OpenReadAsync();
            await AddImageAsync(file.Name, stream, file.Path);
            return;
        }

        if (Attachments.Any(a => string.Equals(a.FilePath, file.Path, StringComparison.OrdinalIgnoreCase)))
            return;

        long size = 0;
        try { size = new FileInfo(file.Path).Length; } catch { }
        Attachments.Add(new AiAttachment { Name = file.Name, FilePath = file.Path, Size = size });
    }

    private async Task AddImageAsync(string name, IRandomAccessStream stream, string? sourcePath = null)
    {
        if (!CanAddAttachment()) return;
        if (Attachments.Count(a => a.IsImage) >= MaxPendingImages)
        {
            StatusText = $"At most {MaxPendingImages} images per question.";
            return;
        }

        var (image, bytes) = await AiImageHelper.PrepareAsync(stream);
        var thumbnail = await AiImageHelper.CreateThumbnailAsync(bytes, 64);
        Attachments.Add(new AiAttachment { Name = name, Image = image, Thumbnail = thumbnail, Size = bytes.Length });
        if (sourcePath != null)
            AppLogger.Info($"[AiChat] Image attached: {sourcePath} ({FSItem.FormatSize(bytes.Length)} {image.MediaType})");
    }

    private bool CanAddAttachment()
    {
        if (Attachments.Count < MaxPendingAttachments) return true;
        StatusText = $"At most {MaxPendingAttachments} attachments per question.";
        return false;
    }

    public void RemoveAttachment(AiAttachment attachment) => Attachments.Remove(attachment);

    /// <summary>"🖼 2 images" for the pending (or sent) images, empty when none.</summary>
    private static string ImagesSummary(int count) =>
        count == 0 ? string.Empty : count == 1 ? "🖼 1 image" : $"🖼 {count} images";
}
