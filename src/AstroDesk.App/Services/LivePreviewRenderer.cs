using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroDesk.Stacking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.App.Services;

/// <summary>
/// Bridges captured files and the running preview stack.
/// </summary>
/// <remarks>
/// <see cref="LivePreviewStacker"/> is deliberately free of WPF so it can be
/// tested without a dispatcher, which leaves decoding and rendering to live
/// here. Frames are decoded small and in greyscale: the preview only has to
/// answer "is tonight's run working", and doing that on a 50 megapixel colour
/// frame would cost far more than the answer is worth.
/// </remarks>
public interface ILivePreviewRenderer
{
    /// <summary>Working width of the preview stack, in pixels.</summary>
    int TargetWidth { get; }

    /// <summary>
    /// Decodes a captured file into a greyscale preview frame, or null when the
    /// file cannot be read as an image.
    /// </summary>
    PreviewImage? Decode(string path);

    /// <summary>Renders a stacked result into a frozen, displayable bitmap.</summary>
    ImageSource Render(PreviewImage image);

    /// <summary>
    /// Small colour thumbnail of a capture for the frame list, or null when the
    /// file cannot be read.
    /// </summary>
    ImageSource? LoadThumbnail(string path, int width = 160);
}

public sealed class LivePreviewRenderer(ILogger<LivePreviewRenderer>? logger = null)
    : ILivePreviewRenderer
{
    private readonly ILogger<LivePreviewRenderer> _logger =
        logger ?? NullLogger<LivePreviewRenderer>.Instance;

    /// <summary>
    /// Wide enough to see whether stars are round and the frame is in focus,
    /// small enough that the alignment search stays cheap on every frame.
    /// </summary>
    public int TargetWidth => 640;

    public PreviewImage? Decode(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            BitmapSource source = LoadDownscaled(path, TargetWidth);
            return ToGreyscale(source);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            // A file can still be mid-write when the sync poll spots it, and a
            // DNG without a usable embedded preview cannot be shown at all.
            // Neither is worth failing the run over: the final Siril stack reads
            // the original files regardless.
            _logger.LogDebug(exception, "Live preview skipped {File}.", Path.GetFileName(path));
            return null;
        }
    }

    public ImageSource? LoadThumbnail(string path, int width = 160)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return LoadDownscaled(path, width);
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            _logger.LogDebug(exception, "No thumbnail for {File}.", Path.GetFileName(path));
            return null;
        }
    }

    public ImageSource Render(PreviewImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Normalise to the actual range so a faint sky is still visible. The
        // stack is linear, and linear astronomical data is almost black until
        // it is stretched.
        (float black, float white) = FindRange(image);
        float span = white - black;
        if (span <= float.Epsilon)
        {
            span = 1f;
        }

        byte[] pixels = new byte[image.Pixels.Length];
        for (int index = 0; index < pixels.Length; index++)
        {
            float normalised = (image.Pixels[index] - black) / span;
            pixels[index] = (byte)Math.Clamp(normalised * 255f, 0f, 255f);
        }

        BitmapSource bitmap = BitmapSource.Create(
            image.Width,
            image.Height,
            96,
            96,
            PixelFormats.Gray8,
            null,
            pixels,
            image.Width);

        // Frozen so it can cross from the decode thread to the UI thread.
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Decodes a capture down to <paramref name="width"/> pixels wide.
    /// </summary>
    /// <remarks>
    /// A DNG cannot be handed straight to WPF: Windows ships no codec for it, and
    /// on an S23 Ultra the sensor data is JPEG XL compressed, which nothing in the
    /// box reads. The embedded JPEG rendition is extracted instead, which is what
    /// a preview wants anyway. Without this every RAW frame decoded to null and
    /// the live stack stayed empty no matter how many frames were captured.
    /// </remarks>
    public static BitmapSource LoadDownscaled(string path, int width)
    {
        byte[]? embedded = IsRaw(path) ? DngPreviewExtractor.TryExtract(path) : null;
        if (IsRaw(path) && embedded is null)
        {
            throw new NotSupportedException(
                $"'{Path.GetFileName(path)}' has no embedded JPEG preview to display.");
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();

        // DecodePixelWidth downscales during decode rather than after, so a
        // 50 megapixel capture never becomes a full-size bitmap in memory.
        bitmap.DecodePixelWidth = width;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;

        if (embedded is not null)
        {
            bitmap.StreamSource = new MemoryStream(embedded);
        }
        else
        {
            bitmap.UriSource = new Uri(path);
        }

        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static bool IsRaw(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".dng" or ".raw";

    private static bool IsUnreadable(Exception exception) =>
        exception is NotSupportedException or FileFormatException or IOException
            or ArgumentException or OverflowException or InvalidOperationException;

    private static PreviewImage ToGreyscale(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
        converted.Freeze();

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width;
        byte[] bytes = new byte[stride * height];
        converted.CopyPixels(bytes, stride, 0);

        float[] pixels = new float[width * height];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = bytes[index];
        }

        return new PreviewImage(width, height, pixels);
    }

    /// <summary>
    /// Picks display black and white points from percentiles rather than the
    /// extremes, so one hot pixel or a single satellite streak cannot flatten
    /// the whole image.
    /// </summary>
    private static (float Black, float White) FindRange(PreviewImage image)
    {
        float[] sorted = [.. image.Pixels];
        Array.Sort(sorted);

        int low = (int)(sorted.Length * 0.02);
        int high = (int)(sorted.Length * 0.995);
        low = Math.Clamp(low, 0, sorted.Length - 1);
        high = Math.Clamp(high, 0, sorted.Length - 1);

        return (sorted[low], sorted[high]);
    }
}
