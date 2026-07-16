using AstroDesk.Capture.Frames;
using OpenCvSharp;

namespace AstroDesk.Capture.Histogram;

public sealed class HistogramAnalyzer
{
    public HistogramResult Analyze(CaptureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Analyze(
            frame.BgraPixels.Span,
            frame.Width,
            frame.Height,
            frame.Stride,
            frame.CapturedAt);
    }

    public HistogramResult Analyze(
        ReadOnlySpan<byte> bgraPixels,
        int width,
        int height,
        int stride,
        DateTimeOffset? capturedAt = null)
    {
        if (width <= 0 || height <= 0 || stride < width * 4)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Frame dimensions and stride are invalid.");
        }

        int expectedLength = checked(stride * height);
        if (bgraPixels.Length < expectedLength)
        {
            throw new ArgumentException("The pixel buffer is smaller than the declared frame.", nameof(bgraPixels));
        }

        byte[] contiguous = new byte[checked(width * height * 4)];
        if (stride == width * 4)
        {
            bgraPixels[..contiguous.Length].CopyTo(contiguous);
        }
        else
        {
            for (int row = 0; row < height; row++)
            {
                bgraPixels.Slice(row * stride, width * 4)
                    .CopyTo(contiguous.AsSpan(row * width * 4, width * 4));
            }
        }

        using Mat bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, contiguous);
        using Mat bgr = new();
        using Mat gray = new();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);

        Mat[] channels = Cv2.Split(bgr);
        try
        {
            float[] luminance = CalculateHistogram(gray);
            float[] blue = CalculateHistogram(channels[0]);
            float[] green = CalculateHistogram(channels[1]);
            float[] red = CalculateHistogram(channels[2]);

            long totalPixels = (long)width * height;
            double shadow = (luminance[0] / totalPixels) * 100;
            double highlights = (luminance[255] / totalPixels) * 100;

            return new HistogramResult(
                luminance,
                red,
                green,
                blue,
                highlights,
                shadow,
                capturedAt ?? DateTimeOffset.UtcNow);
        }
        finally
        {
            foreach (Mat channel in channels)
            {
                channel.Dispose();
            }
        }
    }

    private static float[] CalculateHistogram(Mat channel)
    {
        using Mat histogram = new();
        Rangef[] ranges = [new Rangef(0, 256)];
        Cv2.CalcHist([channel], [0], null, histogram, 1, [256], ranges);

        float[] values = new float[256];
        for (int index = 0; index < values.Length; index++)
        {
            values[index] = histogram.At<float>(index);
        }

        return values;
    }
}
