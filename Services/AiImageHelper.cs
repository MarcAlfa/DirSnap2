using System.IO;
using DirSnap2.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace DirSnap2.Services;

/// <summary>
/// Prepares pasted images for the AI providers: scaled down to <see cref="MaxSide"/> px on the
/// longest side (larger images cost tokens without adding detail, and providers limit the size),
/// re-encoded as PNG (sharp text in screenshots), or JPEG when the PNG is still too heavy.
/// </summary>
public static class AiImageHelper
{
    public const int MaxSide = 2000;
    private const long JpegAboveBytes = 3_500_000;

    /// <summary>Image file extensions pasted from Explorer that are sent as images.</summary>
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"
    };

    public static async Task<(AiImage Image, byte[] Bytes)> PrepareAsync(IRandomAccessStream input)
    {
        var decoder = await BitmapDecoder.CreateAsync(input);
        uint width = decoder.OrientedPixelWidth;
        uint height = decoder.OrientedPixelHeight;
        double scale = Math.Min(1.0, (double)MaxSide / Math.Max(1, Math.Max(width, height)));
        uint w = (uint)Math.Max(1, Math.Round(width * scale));
        uint h = (uint)Math.Max(1, Math.Round(height * scale));

        var transform = new BitmapTransform
        {
            ScaledWidth = w,
            ScaledHeight = h,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var pixels = pixelData.DetachPixelData();

        var bytes = await EncodeAsync(BitmapEncoder.PngEncoderId, BitmapAlphaMode.Premultiplied, pixels, w, h);
        string mediaType = "image/png";
        if (bytes.Length > JpegAboveBytes)
        {
            bytes = await EncodeAsync(BitmapEncoder.JpegEncoderId, BitmapAlphaMode.Ignore, pixels, w, h);
            mediaType = "image/jpeg";
        }
        return (new AiImage(mediaType, Convert.ToBase64String(bytes)), bytes);
    }

    private static async Task<byte[]> EncodeAsync(Guid encoderId, BitmapAlphaMode alpha, byte[] pixels, uint w, uint h)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, output);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, alpha, w, h, 96, 96, pixels);
        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    /// <summary>Small preview for the chips and the chat bubbles. Must run on the UI thread.</summary>
    public static async Task<BitmapImage> CreateThumbnailAsync(byte[] bytes, int height)
    {
        var bitmap = new BitmapImage { DecodePixelHeight = height, DecodePixelType = DecodePixelType.Logical };
        using var stream = new MemoryStream(bytes).AsRandomAccessStream();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
