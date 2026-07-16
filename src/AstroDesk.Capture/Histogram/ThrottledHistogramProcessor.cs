using AstroDesk.Capture.Frames;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Capture.Histogram;

public sealed class ThrottledHistogramProcessor(
    HistogramAnalyzer analyzer,
    ILogger<ThrottledHistogramProcessor> logger) : IDisposable
{
    private readonly HistogramAnalyzer _analyzer = analyzer;
    private readonly ILogger<ThrottledHistogramProcessor> _logger = logger;
    private readonly SemaphoreSlim _processingGate = new(1, 1);
    private DateTimeOffset _lastStartedAt = DateTimeOffset.MinValue;
    private bool _disposed;

    public event EventHandler<HistogramResult>? HistogramReady;

    public TimeSpan MinimumInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    public bool IsFrozen { get; set; }

    public void OfferFrame(CaptureFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (IsFrozen ||
            now - _lastStartedAt < MinimumInterval ||
            !_processingGate.Wait(0))
        {
            return;
        }

        _lastStartedAt = now;
        byte[] pixels = frame.CopyPixels();
        int width = frame.Width;
        int height = frame.Height;
        int stride = frame.Stride;
        DateTimeOffset capturedAt = frame.CapturedAt;

        _ = Task.Run(
            () =>
            {
                try
                {
                    HistogramResult result = _analyzer.Analyze(
                        pixels,
                        width,
                        height,
                        stride,
                        capturedAt);
                    HistogramReady?.Invoke(this, result);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "Histogram processing failed for a preview frame.");
                }
                finally
                {
                    _processingGate.Release();
                }
            });
    }

    public void Dispose()
    {
        _disposed = true;
        _processingGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
