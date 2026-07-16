using AstroDesk.Capture.Frames;
using OpenCvSharp;

namespace AstroDesk.Capture.Screenshots;

public sealed class PreviewScreenshotWriter
{
    public Task<string> SavePngAsync(
        CaptureFrame frame,
        string directoryPath,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        byte[] pixels = frame.CopyPixels();
        return SavePngAsync(
            pixels,
            frame.Width,
            frame.Height,
            frame.Stride,
            directoryPath,
            fileName,
            cancellationToken);
    }

    public async Task<string> SavePngAsync(
        byte[] bgraPixels,
        int width,
        int height,
        int stride,
        string directoryPath,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        Directory.CreateDirectory(directoryPath);

        string safeName = string.IsNullOrWhiteSpace(fileName)
            ? $"preview-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.png"
            : Path.ChangeExtension(Path.GetFileName(fileName), ".png");
        string path = Path.Combine(directoryPath, safeName);

        await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    byte[] contiguous = ToContiguous(bgraPixels, width, height, stride);
                    using Mat bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, contiguous);
                    if (!Cv2.ImWrite(path, bgra))
                    {
                        throw new IOException("OpenCV could not encode the preview screenshot.");
                    }
                },
                cancellationToken)
            .ConfigureAwait(false);

        return path;
    }

    private static byte[] ToContiguous(byte[] source, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions and stride are invalid.");
        }

        if (source.Length < checked(stride * height))
        {
            throw new ArgumentException("The pixel buffer is smaller than the declared frame.", nameof(source));
        }

        int targetStride = checked(width * 4);
        if (stride == targetStride && source.Length == targetStride * height)
        {
            return source;
        }

        byte[] target = new byte[checked(targetStride * height)];
        for (int row = 0; row < height; row++)
        {
            source.AsSpan(row * stride, targetStride)
                .CopyTo(target.AsSpan(row * targetStride, targetStride));
        }

        return target;
    }
}
