using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AstroDesk.Stacking;

/// <summary>
/// Turns captured files into preview frames, and stacked frames back into
/// something displayable.
/// </summary>
/// <remarks>
/// <para>
/// This lived in the app, which meant the pipeline tests could not reach it and
/// kept a parallel copy of the decode instead. A regression in the real decoder
/// therefore passed green - a poor position to be in given the whole thing moved
/// from greyscale to colour.
/// </para>
/// <para>
/// Frames are decoded small: the preview only has to answer "is tonight's run
/// working", and doing that at 50 megapixels would cost far more than the answer
/// is worth. They are decoded in colour, because a cast is itself diagnostic -
/// it is how skyglow and a bad white balance announce themselves.
/// </para>
/// </remarks>
public static class PreviewDecoder
{
    /// <summary>
    /// Wide enough to see whether stars are round and the frame is in focus,
    /// small enough that the alignment search stays cheap on every frame.
    /// </summary>
    public const int DefaultTargetWidth = 640;

    public static bool IsRaw(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".dng" or ".raw";

    /// <summary>
    /// True for the failures that mean "skip this file", not "the run is broken".
    /// </summary>
    /// <remarks>
    /// A file can still be mid-write when the sync poll spots it, and a DNG
    /// without a usable embedded preview cannot be shown at all. Neither is
    /// worth failing a run over: the final stack reads the originals regardless.
    /// </remarks>
    public static bool IsUnreadable(Exception exception) =>
        exception is NotSupportedException or FileFormatException or IOException
            or ArgumentException or OverflowException or InvalidOperationException;

    /// <summary>
    /// Decodes a capture into an RGB preview frame, or null when unreadable.
    /// </summary>
    public static PreviewImage? Decode(string path, int targetWidth = DefaultTargetWidth)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return ToRgb(LoadDownscaled(path, targetWidth));
        }
        catch (Exception exception) when (IsUnreadable(exception))
        {
            return null;
        }
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

    /// <summary>
    /// Renders a stacked result into a frozen, displayable bitmap.
    /// </summary>
    public static BitmapSource Render(PreviewImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Normalise to the actual range so a faint sky is still visible. The
        // stack is linear, and linear astronomical data is almost black until
        // it is stretched.
        //
        // The same black and white points are applied to every channel, taken
        // from luminance. Stretching each channel to its own range would silently
        // white-balance the image: it would neutralise the orange cast of a
        // light-polluted sky, but equally it would neutralise a genuinely red
        // target, and it would hide one channel clipping. For a preview whose job
        // is to answer whether the frames are usable, a faithful cast is more
        // informative than a flattering one.
        (float black, float white) = FindRange(image);
        float span = white - black;
        if (span <= float.Epsilon)
        {
            span = 1f;
        }

        if (!image.IsColour)
        {
            byte[] grey = new byte[image.Pixels.Length];
            for (int index = 0; index < grey.Length; index++)
            {
                grey[index] = Stretch(image.Pixels[index], black, span);
            }

            return Freeze(BitmapSource.Create(
                image.Width, image.Height, 96, 96, PixelFormats.Gray8, null, grey, image.Width));
        }

        // Back to BGR for WPF.
        byte[] pixels = new byte[image.Pixels.Length];
        for (int index = 0; index < image.Width * image.Height; index++)
        {
            int offset = index * 3;
            pixels[offset] = Stretch(image.Pixels[offset + 2], black, span);
            pixels[offset + 1] = Stretch(image.Pixels[offset + 1], black, span);
            pixels[offset + 2] = Stretch(image.Pixels[offset], black, span);
        }

        return Freeze(BitmapSource.Create(
            image.Width, image.Height, 96, 96, PixelFormats.Bgr24, null, pixels, image.Width * 3));
    }

    /// <summary>
    /// Decodes into an RGB preview frame.
    /// </summary>
    /// <remarks>
    /// WPF gives BGR byte order, which is reversed here so the stack holds plain
    /// RGB and nothing downstream has to remember the channel order.
    /// </remarks>
    public static PreviewImage ToRgb(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        converted.Freeze();

        int width = converted.PixelWidth;
        int height = converted.PixelHeight;
        int stride = width * 3;
        byte[] bytes = new byte[stride * height];
        converted.CopyPixels(bytes, stride, 0);

        float[] pixels = new float[width * height * PreviewImage.Rgb];
        for (int index = 0; index < width * height; index++)
        {
            int offset = index * 3;
            pixels[offset] = bytes[offset + 2];
            pixels[offset + 1] = bytes[offset + 1];
            pixels[offset + 2] = bytes[offset];
        }

        return new PreviewImage(width, height, pixels, PreviewImage.Rgb);
    }

    private static byte Stretch(float value, float black, float span) =>
        (byte)Math.Clamp((value - black) / span * 255f, 0f, 255f);

    private static BitmapSource Freeze(BitmapSource bitmap)
    {
        // Frozen so it can cross from the decode thread to the UI thread.
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Picks display black and white points from percentiles rather than the
    /// extremes, so one hot pixel or a single satellite streak cannot flatten
    /// the whole image.
    /// </summary>
    /// <remarks>
    /// Measured on luminance so the range is one shared stretch across the
    /// channels, and so a colour frame does not cost three times the sort.
    /// </remarks>
    private static (float Black, float White) FindRange(PreviewImage image)
    {
        float[] sorted = [.. image.Luminance];
        Array.Sort(sorted);

        int low = (int)(sorted.Length * 0.02);
        int high = (int)(sorted.Length * 0.995);
        low = Math.Clamp(low, 0, sorted.Length - 1);
        high = Math.Clamp(high, 0, sorted.Length - 1);

        return (sorted[low], sorted[high]);
    }
}
