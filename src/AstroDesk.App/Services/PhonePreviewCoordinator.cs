using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroDesk.Capture.Frames;
using AstroDesk.Capture.Histogram;
using AstroDesk.Capture.Screenshots;
using AstroDesk.Core.Enums;
using AstroDesk.Device.Scrcpy;
using Microsoft.Extensions.Logging;

namespace AstroDesk.App.Services;

public sealed record PreviewFrameSnapshot(
    BitmapSource Image,
    int Width,
    int Height,
    int Stride,
    DateTimeOffset CapturedAt);

public sealed record PreviewStatus(
    bool IsRunning,
    double FramesPerSecond,
    string Message,
    string? Error = null);

public interface IPhonePreviewCoordinator : IAsyncDisposable
{
    event EventHandler<PreviewFrameSnapshot>? FrameReady;

    event EventHandler<HistogramResult>? HistogramReady;

    event EventHandler<PreviewStatus>? StatusChanged;

    bool IsFrozen { get; set; }

    NightDisplayMode DisplayMode { get; set; }

    TimeSpan HistogramUpdateInterval { get; set; }

    ScrcpySession? CurrentSession { get; }

    Task<ScrcpySession> StartAsync(
        ScrcpyLaunchOptions options,
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<ScrcpySession> ReconnectAsync(CancellationToken cancellationToken = default);

    Task<string> SaveCurrentFrameAsync(
        string directory,
        string? fileName = null,
        CancellationToken cancellationToken = default);
}

public sealed class PhonePreviewCoordinator : IPhonePreviewCoordinator
{
    private readonly IWindowCaptureService _capture;
    private readonly IScrcpyService _scrcpy;
    private readonly ThrottledHistogramProcessor _histogram;
    private readonly PreviewScreenshotWriter _screenshotWriter;
    private readonly ILogger<PhonePreviewCoordinator> _logger;
    private readonly object _latestSync = new();
    private byte[]? _latestRawPixels;
    private byte[]? _latestPixels;
    private int _latestWidth;
    private int _latestHeight;
    private int _latestStride;
    private DateTimeOffset _latestCapturedAt;
    private NightDisplayMode _displayMode = NightDisplayMode.NormalDark;
    private double _fps;

    public PhonePreviewCoordinator(
        IWindowCaptureService capture,
        IScrcpyService scrcpy,
        ThrottledHistogramProcessor histogram,
        PreviewScreenshotWriter screenshotWriter,
        ILogger<PhonePreviewCoordinator> logger)
    {
        _capture = capture;
        _scrcpy = scrcpy;
        _histogram = histogram;
        _screenshotWriter = screenshotWriter;
        _logger = logger;

        _capture.FrameArrived += HandleFrameArrived;
        _capture.CaptureFailed += HandleCaptureFailed;
        _capture.FramesPerSecondChanged += HandleFpsChanged;
        _histogram.HistogramReady += HandleHistogramReady;
        _scrcpy.Crashed += HandleScrcpyCrashed;
        _scrcpy.StateChanged += HandleScrcpyStateChanged;
    }

    public event EventHandler<PreviewFrameSnapshot>? FrameReady;

    public event EventHandler<HistogramResult>? HistogramReady;

    public event EventHandler<PreviewStatus>? StatusChanged;

    public bool IsFrozen { get; set; }

    public NightDisplayMode DisplayMode
    {
        get
        {
            lock (_latestSync)
            {
                return _displayMode;
            }
        }
        set
        {
            lock (_latestSync)
            {
                if (_displayMode == value)
                {
                    return;
                }

                _displayMode = value;
                RepublishLatestFrameLocked();
            }
        }
    }

    public TimeSpan HistogramUpdateInterval
    {
        get => _histogram.MinimumInterval;
        set => _histogram.MinimumInterval = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public ScrcpySession? CurrentSession => _scrcpy.CurrentSession;

    public async Task<ScrcpySession> StartAsync(
        ScrcpyLaunchOptions options,
        CancellationToken cancellationToken = default)
    {
        ScrcpySession session = await _scrcpy.StartAsync(options, cancellationToken).ConfigureAwait(false);
        await _capture.StartAsync(
                session.WindowHandle,
                new WindowCaptureOptions { FramesPerSecond = options.MaxFps is > 0 ? Math.Min(options.MaxFps.Value, 60) : 30 },
                cancellationToken)
            .ConfigureAwait(false);
        PublishStatus(true, "Embedded phone preview running.");
        return session;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _capture.StopAsync(cancellationToken).ConfigureAwait(false);
        await _scrcpy.StopAsync(cancellationToken).ConfigureAwait(false);
        PublishStatus(false, "Phone preview stopped.");
    }

    public async Task<ScrcpySession> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _capture.StopAsync(cancellationToken).ConfigureAwait(false);
        ScrcpySession session = await _scrcpy.ReconnectAsync(cancellationToken).ConfigureAwait(false);
        await _capture.StartAsync(
                session.WindowHandle,
                new WindowCaptureOptions
                {
                    FramesPerSecond = session.Options.MaxFps is > 0
                        ? Math.Min(session.Options.MaxFps.Value, 60)
                        : 30,
                },
                cancellationToken)
            .ConfigureAwait(false);
        PublishStatus(true, "Embedded phone preview reconnected.");
        return session;
    }

    public async Task<string> SaveCurrentFrameAsync(
        string directory,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        byte[] pixels;
        int width;
        int height;
        int stride;
        lock (_latestSync)
        {
            if (_latestPixels is null)
            {
                throw new InvalidOperationException("No embedded preview frame is available to save.");
            }

            pixels = (byte[])_latestPixels.Clone();
            width = _latestWidth;
            height = _latestHeight;
            stride = _latestStride;
        }

        return await _screenshotWriter
            .SavePngAsync(pixels, width, height, stride, directory, fileName, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _capture.FrameArrived -= HandleFrameArrived;
        _capture.CaptureFailed -= HandleCaptureFailed;
        _capture.FramesPerSecondChanged -= HandleFpsChanged;
        _histogram.HistogramReady -= HandleHistogramReady;
        _scrcpy.Crashed -= HandleScrcpyCrashed;
        _scrcpy.StateChanged -= HandleScrcpyStateChanged;
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private void HandleFrameArrived(object? sender, CaptureFrameEventArgs args)
    {
        _histogram.OfferFrame(args.Frame);
        if (IsFrozen)
        {
            return;
        }

        try
        {
            byte[] rawPixels = args.Frame.CopyPixels();
            lock (_latestSync)
            {
                _latestRawPixels = rawPixels;
                _latestWidth = args.Frame.Width;
                _latestHeight = args.Frame.Height;
                _latestStride = args.Frame.Stride;
                _latestCapturedAt = args.Frame.CapturedAt;
                PublishFrameLocked(rawPixels);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not render an embedded preview frame.");
        }
    }

    private void RepublishLatestFrameLocked()
    {
        if (_latestRawPixels is not null)
        {
            PublishFrameLocked(_latestRawPixels);
        }
    }

    private void PublishFrameLocked(byte[] rawPixels)
    {
        byte[] displayedPixels = _displayMode == NightDisplayMode.FullRed
            ? (byte[])rawPixels.Clone()
            : rawPixels;
        if (_displayMode == NightDisplayMode.FullRed)
        {
            ApplyRedTint(displayedPixels, _latestWidth, _latestHeight, _latestStride);
        }

        BitmapSource bitmap = BitmapSource.Create(
            _latestWidth,
            _latestHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            displayedPixels,
            _latestStride);
        bitmap.Freeze();
        _latestPixels = displayedPixels;

        FrameReady?.Invoke(
            this,
            new PreviewFrameSnapshot(
                bitmap,
                _latestWidth,
                _latestHeight,
                _latestStride,
                _latestCapturedAt));
    }

    private void HandleCaptureFailed(object? sender, CaptureErrorEventArgs args) =>
        PublishStatus(false, "Embedded preview capture stopped.", args.Message);

    private void HandleFpsChanged(object? sender, double fps)
    {
        _fps = fps;
        PublishStatus(_capture.IsRunning, _capture.IsRunning ? "Embedded phone preview running." : "Phone preview stopped.");
    }

    private void HandleHistogramReady(object? sender, HistogramResult result) =>
        HistogramReady?.Invoke(this, result);

    private void HandleScrcpyCrashed(object? sender, ScrcpyCrashedEventArgs args) =>
        PublishStatus(false, "scrcpy stopped unexpectedly.", $"scrcpy exited with code {args.ExitCode}.");

    private void HandleScrcpyStateChanged(object? sender, ScrcpyStateChangedEventArgs args)
    {
        if (args.State == ScrcpyState.Faulted)
        {
            PublishStatus(false, "scrcpy could not start.", "Check the configured scrcpy path and logs.");
        }
    }

    private void PublishStatus(bool running, string message, string? error = null) =>
        StatusChanged?.Invoke(this, new PreviewStatus(running, _fps, message, error));

    private static void ApplyRedTint(byte[] pixels, int width, int height, int stride)
    {
        for (int row = 0; row < height; row++)
        {
            int rowStart = row * stride;
            for (int column = 0; column < width; column++)
            {
                int index = rowStart + (column * 4);
                byte blue = pixels[index];
                byte green = pixels[index + 1];
                byte red = pixels[index + 2];
                byte luminance = (byte)Math.Clamp(
                    (int)Math.Round((red * 0.299) + (green * 0.587) + (blue * 0.114)),
                    0,
                    255);
                pixels[index] = 0;
                pixels[index + 1] = 0;
                pixels[index + 2] = luminance;
            }
        }
    }
}
