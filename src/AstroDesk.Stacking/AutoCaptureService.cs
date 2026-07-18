using AstroDesk.Device.Adb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Stacking;

/// <param name="Serial">Target device serial.</param>
/// <param name="Exposure">Shutter time set in the camera app.</param>
/// <param name="WriteDelay">
/// Expected time to save the file after the exposure ends. Sizes the watchdog and
/// the settle pause only; it never decides when the next shot is due.
/// </param>
/// <param name="FrameCount">Frames to trigger; zero means keep going until stopped.</param>
public sealed record AutoCaptureOptions(
    string Serial,
    TimeSpan Exposure,
    TimeSpan WriteDelay,
    int FrameCount);

/// <summary>
/// Counters for one auto-capture run.
/// </summary>
/// <remarks>
/// These exist because the failure modes are quiet. A shutter press that lands
/// while the camera is busy is ignored with no error; a capture that produces two
/// files looks like two frames. Comparing commands sent against captures counted
/// against frames stacked is the only way to see that from outside.
/// </remarks>
public sealed record AutoCaptureTelemetry(
    int ShotsRequested,
    int ShutterCommandsSent,
    int ShutterCommandsFailed,
    int FilesImported,
    int CapturesCounted,
    int DuplicatesIgnored,
    int IgnoredNotCapture,
    int IgnoredPredatingShutter,
    int Timeouts)
{
    public static AutoCaptureTelemetry Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Shutter commands that were sent but never produced a capture.
    /// </summary>
    public int UnaccountedShutters => Math.Max(0, ShutterCommandsSent - CapturesCounted);

    public string Summary =>
        $"requested {ShotsRequested} · sent {ShutterCommandsSent} · captures {CapturesCounted} · " +
        $"files {FilesImported} · duplicates {DuplicatesIgnored} · timeouts {Timeouts}";
}

public sealed class AutoCaptureTick(int frame, int total) : EventArgs
{
    public int Frame { get; } = frame;

    public int Total { get; } = total;
}

public sealed class AutoCaptureFinished(
    int framesTriggered,
    bool timedOut,
    string message,
    AutoCaptureTelemetry telemetry) : EventArgs
{
    public int FramesTriggered { get; } = framesTriggered;

    public bool TimedOut { get; } = timedOut;

    public string Message { get; } = message;

    public AutoCaptureTelemetry Telemetry { get; } = telemetry;
}

public interface IAutoCaptureService : IAsyncDisposable
{
    event EventHandler<AutoCaptureTick>? Triggered;

    event EventHandler<AutoCaptureFinished>? Finished;

    event EventHandler<AutoCaptureTelemetry>? TelemetryChanged;

    bool IsRunning { get; }

    AutoCaptureTelemetry Telemetry { get; }

    Task StartAsync(AutoCaptureOptions options, CancellationToken cancellationToken = default);

    Task StopAsync();

    /// <summary>
    /// Offers an imported file to the run. Only a file that completes the
    /// outstanding shutter press advances it.
    /// </summary>
    /// <param name="path">Local path of the imported file.</param>
    /// <param name="writtenAt">When the capture was produced.</param>
    CaptureMatch NotifyFileImported(string path, DateTimeOffset writtenAt);
}

/// <summary>
/// Fires the phone's shutter for an unattended stacking run, advancing only when
/// a file that genuinely belongs to the outstanding press has arrived.
/// </summary>
/// <remarks>
/// <para>
/// The shutter is triggered with <c>KEYCODE_VOLUME_UP</c>. Verified on an S23
/// Ultra (SCG20): with <c>com.sec.android.app.camera</c> focused, one press
/// produced exactly one new file. The same press with the camera backgrounded
/// produced none, which is why the watchdog exists.
/// </para>
/// <para>
/// Pacing follows capture arrival rather than a timer, because a timer must
/// assume how long the phone needs and is wrong exactly when it matters. Firing
/// early does not queue a shot, it is discarded, so a fast timer quietly captures
/// fewer frames than it reports.
/// </para>
/// </remarks>
public sealed class AutoCaptureService : IAutoCaptureService
{
    /// <summary>Never fire again sooner than this after a capture lands.</summary>
    public static readonly TimeSpan MinimumSettleDelay = TimeSpan.FromMilliseconds(750);

    private readonly IAdbInputService _input;
    private readonly ILogger<AutoCaptureService> _logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly CaptureCorrelator _correlator = new();
    private readonly object _telemetrySync = new();

    /// <summary>
    /// Signals that a capture completing the outstanding press has arrived.
    /// Initial count zero, released at most once per counted capture.
    /// </summary>
    private SemaphoreSlim _captureArrived = new(0);

    private CancellationTokenSource? _cancellation;
    private Task? _runTask;
    private AutoCaptureTelemetry _telemetry = AutoCaptureTelemetry.Empty;

    public AutoCaptureService(
        IAdbInputService input,
        ILogger<AutoCaptureService>? logger = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _logger = logger ?? NullLogger<AutoCaptureService>.Instance;
    }

    public event EventHandler<AutoCaptureTick>? Triggered;

    public event EventHandler<AutoCaptureFinished>? Finished;

    public event EventHandler<AutoCaptureTelemetry>? TelemetryChanged;

    public bool IsRunning { get; private set; }

    public AutoCaptureTelemetry Telemetry
    {
        get
        {
            lock (_telemetrySync)
            {
                return _telemetry;
            }
        }
    }

    public static TimeSpan CalculateTimeout(TimeSpan exposure, TimeSpan writeDelay)
    {
        TimeSpan expected =
            (exposure > TimeSpan.Zero ? exposure : TimeSpan.Zero) +
            (writeDelay > TimeSpan.Zero ? writeDelay : TimeSpan.Zero);

        TimeSpan timeout = expected + expected + TimeSpan.FromSeconds(15);
        return timeout < TimeSpan.FromSeconds(20) ? TimeSpan.FromSeconds(20) : timeout;
    }

    public static TimeSpan CalculateSettleDelay(TimeSpan writeDelay)
    {
        TimeSpan quarter = writeDelay > TimeSpan.Zero
            ? TimeSpan.FromMilliseconds(writeDelay.TotalMilliseconds / 4)
            : TimeSpan.Zero;

        return quarter < MinimumSettleDelay ? MinimumSettleDelay : quarter;
    }

    public CaptureMatch NotifyFileImported(string path, DateTimeOffset writtenAt)
    {
        if (!IsRunning)
        {
            return CaptureMatch.NoPendingShutter;
        }

        CaptureMatch match = _correlator.Classify(path, writtenAt);

        Mutate(current => match switch
        {
            CaptureMatch.NewCapture => current with
            {
                FilesImported = current.FilesImported + 1,
                CapturesCounted = current.CapturesCounted + 1,
            },
            CaptureMatch.DuplicateOfCountedCapture => current with
            {
                FilesImported = current.FilesImported + 1,
                DuplicatesIgnored = current.DuplicatesIgnored + 1,
            },
            CaptureMatch.PredatesShutter => current with
            {
                FilesImported = current.FilesImported + 1,
                IgnoredPredatingShutter = current.IgnoredPredatingShutter + 1,
            },
            CaptureMatch.NotACapture => current with
            {
                IgnoredNotCapture = current.IgnoredNotCapture + 1,
            },
            _ => current with { FilesImported = current.FilesImported + 1 },
        });

        if (match == CaptureMatch.NewCapture)
        {
            _captureArrived.Release();
        }
        else
        {
            _logger.LogDebug("Ignored {File}: {Reason}.", Path.GetFileName(path), match);
        }

        return match;
    }

    public async Task StartAsync(
        AutoCaptureOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Serial);
        ArgumentOutOfRangeException.ThrowIfNegative(options.FrameCount);

        await StopAsync().ConfigureAwait(false);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _correlator.Reset();

            // A fresh signal so nothing counted before this run can advance it.
            _captureArrived.Dispose();
            _captureArrived = new SemaphoreSlim(0);

            SetTelemetry(AutoCaptureTelemetry.Empty with { ShotsRequested = options.FrameCount });

            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;
            _runTask = RunAsync(options, _cancellation.Token);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        CancellationTokenSource? cancellation;
        Task? runTask;
        try
        {
            cancellation = _cancellation;
            runTask = _runTask;
            _cancellation = null;
            _runTask = null;

            // Cleared before cancelling so any file arriving during teardown is
            // rejected rather than releasing the signal behind us.
            IsRunning = false;
        }
        finally
        {
            _lifecycle.Release();
        }

        if (cancellation is null)
        {
            return;
        }

        await cancellation.CancelAsync().ConfigureAwait(false);
        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected while stopping.
            }
        }

        cancellation.Dispose();
    }

    private async Task RunAsync(AutoCaptureOptions options, CancellationToken cancellationToken)
    {
        TimeSpan timeout = CalculateTimeout(options.Exposure, options.WriteDelay);
        TimeSpan settle = CalculateSettleDelay(options.WriteDelay);

        _logger.LogInformation(
            "Auto capture started: {Frames} frames, advancing on capture arrival (timeout {Timeout}).",
            options.FrameCount == 0 ? "unlimited" : options.FrameCount.ToString(),
            timeout);

        int frame = 0;
        bool timedOut = false;
        string message;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (options.FrameCount > 0 && frame >= options.FrameCount)
                {
                    break;
                }

                // Re-check immediately before issuing the command so a stop during
                // the settle delay cannot be followed by another press.
                cancellationToken.ThrowIfCancellationRequested();

                DateTimeOffset pressedAt = DateTimeOffset.Now;
                try
                {
                    await _input
                        .SendKeyAsync(options.Serial, AndroidKeyCode.VolumeUp, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // The command never reached the phone, so no capture is
                    // pending and the frame must not be reported as started.
                    _logger.LogWarning(exception, "Shutter command failed.");
                    Mutate(current => current with
                    {
                        ShutterCommandsFailed = current.ShutterCommandsFailed + 1,
                    });
                    message = $"Shutter command failed: {exception.Message}";
                    Finish(frame, timedOut: false, message);
                    return;
                }

                // Only now is the shot genuinely started: the command succeeded.
                _correlator.ShutterPressed(pressedAt);
                frame++;
                Mutate(current => current with
                {
                    ShutterCommandsSent = current.ShutterCommandsSent + 1,
                });
                Triggered?.Invoke(this, new AutoCaptureTick(frame, options.FrameCount));

                if (options.FrameCount > 0 && frame >= options.FrameCount)
                {
                    break;
                }

                bool arrived = await _captureArrived
                    .WaitAsync(timeout, cancellationToken)
                    .ConfigureAwait(false);

                if (!arrived)
                {
                    timedOut = true;
                    Mutate(current => current with { Timeouts = current.Timeouts + 1 });
                    break;
                }

                await Task.Delay(settle, cancellationToken).ConfigureAwait(false);
            }

            message = timedOut
                ? $"Stopped after {frame}: no photo arrived within {timeout.TotalSeconds:F0}s. " +
                  "Check that the camera app is open and its volume key is set to take pictures."
                : $"Auto capture finished after {frame} shots.";
        }
        catch (OperationCanceledException)
        {
            message = $"Auto capture stopped after {frame} shots.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Auto capture stopped after an error.");
            message = $"Auto capture failed: {exception.Message}";
        }

        Finish(frame, timedOut, message);
    }

    private void Finish(int frame, bool timedOut, string message)
    {
        IsRunning = false;
        AutoCaptureTelemetry telemetry = Telemetry;
        _logger.LogInformation("Auto capture telemetry: {Summary}", telemetry.Summary);
        Finished?.Invoke(this, new AutoCaptureFinished(frame, timedOut, message, telemetry));
    }

    private void Mutate(Func<AutoCaptureTelemetry, AutoCaptureTelemetry> update)
    {
        AutoCaptureTelemetry updated;
        lock (_telemetrySync)
        {
            updated = update(_telemetry);
            _telemetry = updated;
        }

        TelemetryChanged?.Invoke(this, updated);
    }

    private void SetTelemetry(AutoCaptureTelemetry telemetry)
    {
        lock (_telemetrySync)
        {
            _telemetry = telemetry;
        }

        TelemetryChanged?.Invoke(this, telemetry);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
        _captureArrived.Dispose();
    }
}
