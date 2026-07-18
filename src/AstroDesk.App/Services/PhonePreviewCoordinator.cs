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

    /// <summary>
    /// Raised when capture reattaches to a new scrcpy window, so the embedded
    /// host can rebind instead of staying pointed at the destroyed one.
    /// </summary>
    event EventHandler<nint>? WindowHandleChanged;

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
    /// <summary>
    /// Brightness multiplier applied to the preview in full red mode. Low enough
    /// to stop the preview glowing in your face on a dark site, high enough to
    /// still judge framing and focus.
    /// </summary>
    private const double FullRedPreviewDimFactor = 0.45;

    private readonly PreviewScreenshotWriter _screenshotWriter;
    private readonly IPhonePhotoSyncService _photoSync;
    private readonly IPhoneOrientationSessionService _orientation;
    private readonly IScrcpyWindowManager _windowManager;

    /// <summary>
    /// Recovery attempts allowed before the preview is declared dead, so a
    /// genuinely gone window cannot spin forever.
    /// </summary>
    private const int MaximumCaptureRecoveries = 3;

    private int _captureRecoveries;
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
        IPhonePhotoSyncService photoSync,
        IPhoneOrientationSessionService orientation,
        IScrcpyWindowManager windowManager,
        ILogger<PhonePreviewCoordinator> logger)
    {
        _capture = capture;
        _scrcpy = scrcpy;
        _histogram = histogram;
        _screenshotWriter = screenshotWriter;
        _photoSync = photoSync;
        _orientation = orientation;
        _windowManager = windowManager;
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

    public event EventHandler<nint>? WindowHandleChanged;

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
        if (!string.IsNullOrWhiteSpace(options.DeviceSerial))
        {
            await _orientation.EnterLandscapeAsync(options.DeviceSerial, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            ScrcpySession session = await _scrcpy.StartAsync(options, cancellationToken).ConfigureAwait(false);
            Interlocked.Exchange(ref _captureRecoveries, 0);
            await _capture.StartAsync(
                    session.WindowHandle,
                    new WindowCaptureOptions { FramesPerSecond = options.MaxFps is > 0 ? Math.Min(options.MaxFps.Value, 60) : 30 },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(session.Options.DeviceSerial))
            {
                await _photoSync.StartAsync(session.Options.DeviceSerial, cancellationToken).ConfigureAwait(false);
            }

            PublishStatus(true, "Embedded phone preview running.");
            return session;
        }
        catch
        {
            await _photoSync.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _capture.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _scrcpy.StopAsync(CancellationToken.None).ConfigureAwait(false);
            await _orientation.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _photoSync.StopAsync(cancellationToken).ConfigureAwait(false);
            await _capture.StopAsync(cancellationToken).ConfigureAwait(false);
            await _scrcpy.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _orientation.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
        }

        PublishStatus(false, "Phone preview stopped.");
    }

    public async Task<ScrcpySession> ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _photoSync.StopAsync(cancellationToken).ConfigureAwait(false);
        await _capture.StopAsync(cancellationToken).ConfigureAwait(false);
        string? serial = _scrcpy.CurrentSession?.Options.DeviceSerial;
        if (!string.IsNullOrWhiteSpace(serial))
        {
            await _orientation.EnterLandscapeAsync(serial, cancellationToken).ConfigureAwait(false);
        }

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
        if (!string.IsNullOrWhiteSpace(session.Options.DeviceSerial))
        {
            await _photoSync.StartAsync(session.Options.DeviceSerial, cancellationToken).ConfigureAwait(false);
        }

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
            ApplyPreviewDim(displayedPixels, _latestWidth, _latestHeight, _latestStride);
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
        _ = RecoverCaptureAsync(args);

    /// <summary>
    /// Re-resolves the scrcpy window and restarts capture after the handle dies.
    /// </summary>
    /// <remarks>
    /// The window handle is captured once when the session starts, but scrcpy
    /// recreates its SDL window on events such as the device rotating — which
    /// AstroDesk itself triggers via EnterLandscapeAsync immediately before the
    /// handle is taken. The old handle then fails GetClientRect with
    /// ERROR_INVALID_WINDOW_HANDLE and the preview goes black for the rest of the
    /// session. Because the process is still alive and its title is known, the
    /// new window can simply be found again.
    /// </remarks>
    private async Task RecoverCaptureAsync(CaptureErrorEventArgs args)
    {
        ScrcpySession? session = _scrcpy.CurrentSession;
        if (session is null)
        {
            PublishStatus(false, "Embedded preview capture stopped.", args.Message);
            return;
        }

        if (Interlocked.Increment(ref _captureRecoveries) > MaximumCaptureRecoveries)
        {
            _logger.LogWarning(
                "Preview capture failed {Count} times; giving up.",
                MaximumCaptureRecoveries);
            PublishStatus(false, "Embedded preview capture stopped.", args.Message);
            return;
        }

        PublishStatus(false, "Reattaching to the phone preview…");

        try
        {
            await _capture.StopAsync(CancellationToken.None).ConfigureAwait(false);

            nint handle = await _windowManager
                .FindWindowAsync(
                    session.WindowTitle,
                    session.ProcessId,
                    TimeSpan.FromSeconds(10))
                .ConfigureAwait(false);

            if (handle == nint.Zero)
            {
                PublishStatus(false, "Embedded preview capture stopped.", args.Message);
                return;
            }

            await _capture
                .StartAsync(handle, new WindowCaptureOptions(), CancellationToken.None)
                .ConfigureAwait(false);

            WindowHandleChanged?.Invoke(this, handle);
            _logger.LogInformation("Preview capture reattached to window {Handle}.", handle);
            PublishStatus(true, "Embedded phone preview running.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not reattach the preview capture.");
            PublishStatus(false, "Embedded preview capture stopped.", args.Message);
        }
    }

    private void HandleFpsChanged(object? sender, double fps)
    {
        _fps = fps;
        PublishStatus(_capture.IsRunning, _capture.IsRunning ? "Embedded phone preview running." : "Phone preview stopped.");
    }

    private void HandleHistogramReady(object? sender, HistogramResult result) =>
        HistogramReady?.Invoke(this, result);

    private async void HandleScrcpyCrashed(object? sender, ScrcpyCrashedEventArgs args)
    {
        await _photoSync.StopAsync().ConfigureAwait(false);
        await _orientation.RestoreAsync().ConfigureAwait(false);
        PublishStatus(false, "scrcpy stopped unexpectedly.", $"scrcpy exited with code {args.ExitCode}.");
    }

    private void HandleScrcpyStateChanged(object? sender, ScrcpyStateChangedEventArgs args)
    {
        if (args.State == ScrcpyState.Faulted)
        {
            PublishStatus(false, "scrcpy could not start.", "Check the configured scrcpy path and logs.");
        }
    }

    private void PublishStatus(bool running, string message, string? error = null) =>
        StatusChanged?.Invoke(this, new PreviewStatus(running, _fps, message, error));

    /// <summary>
    /// Scales every channel down while leaving hue and saturation intact.
    /// </summary>
    /// <remarks>
    /// Full red mode drops blue and green from AstroDesk's own chrome, but the
    /// preview is the one surface where colour has to stay truthful: it is the
    /// frame being judged, and a red-tinted version cannot show a colour cast,
    /// white balance error, or light pollution gradient. Dimming protects dark
    /// adaptation, which is the actual goal, without destroying that signal.
    /// </remarks>
    private static void ApplyPreviewDim(byte[] pixels, int width, int height, int stride)
    {
        for (int row = 0; row < height; row++)
        {
            int rowStart = row * stride;
            for (int column = 0; column < width; column++)
            {
                int index = rowStart + (column * 4);
                pixels[index] = Dim(pixels[index]);
                pixels[index + 1] = Dim(pixels[index + 1]);
                pixels[index + 2] = Dim(pixels[index + 2]);

                // Alpha at index + 3 is left alone.
            }
        }

        static byte Dim(byte channel) => (byte)Math.Clamp(
            (int)Math.Round(channel * FullRedPreviewDimFactor, MidpointRounding.AwayFromZero),
            0,
            255);
    }
}
