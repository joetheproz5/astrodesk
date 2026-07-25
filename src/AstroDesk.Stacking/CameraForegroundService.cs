using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Stacking;

public sealed record CameraReadyResult(bool Ready, string Message);

public interface ICameraForegroundService
{
    /// <summary>
    /// Brings the camera app to the front and confirms it is running.
    /// </summary>
    Task<CameraReadyResult> EnsureForegroundAsync(
        string serial,
        CameraApp app,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Makes sure the camera app is actually in front before shutter commands are sent.
/// </summary>
/// <remarks>
/// A volume-key shutter is delivered to whichever app holds the foreground. If the
/// camera is merely running in the background the key press is consumed as a
/// volume change and no photo is taken — verified on an S23 Ultra, where the same
/// press produced zero files with the camera backgrounded and one file with it in
/// front. The failure is silent, so an unattended run would otherwise fire its
/// whole sequence into nothing and only reveal the problem at the watchdog.
/// </remarks>
public sealed class CameraForegroundService(
    IAdbCommandExecutor adb,
    ILogger<CameraForegroundService>? logger = null) : ICameraForegroundService
{
    /// <summary>
    /// Samsung's camera. Checked first because this is the workflow AstroDesk is
    /// built around; the generic intent is the fallback for other phones.
    /// </summary>
    public const string SamsungCameraPackage = "com.sec.android.app.camera";

    /// <summary>
    /// Time given to the camera to come up before the first shutter. Samsung's
    /// camera can take a few seconds to initialise, and a press during startup is
    /// discarded.
    /// </summary>
    public static readonly TimeSpan LaunchSettleDelay = TimeSpan.FromSeconds(4);

    private readonly IAdbCommandExecutor _adb = adb;
    private readonly ILogger<CameraForegroundService> _logger =
        logger ?? NullLogger<CameraForegroundService>.Instance;

    public async Task<CameraReadyResult> EnsureForegroundAsync(
        string serial,
        CameraApp app,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentNullException.ThrowIfNull(app);

        try
        {
            // Wake first: a sleeping phone accepts the launch but shows nothing,
            // and the shutter would go to the lock screen.
            await _adb
                .ExecuteAsync(serial, ["shell", "input", "keyevent", "KEYCODE_WAKEUP"], cancellationToken)
                .ConfigureAwait(false);

            // monkey resumes an already-running instance rather than starting a
            // second task, which "am start" does not reliably do for the camera.
            ProcessExecutionResult launch = await _adb
                .ExecuteAsync(
                    serial,
                    [
                        "shell", "monkey", "-p", app.Package,
                        "-c", "android.intent.category.LAUNCHER", "1",
                    ],
                    cancellationToken)
                .ConfigureAwait(false);

            if (!launch.Succeeded ||
                launch.StandardOutput.Contains("No activities found", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Could not launch {Package}.", app.Package);
                return new CameraReadyResult(
                    false,
                    $"Could not open {app.Name} on the phone. Open it manually, then start auto capture.");
            }

            await Task.Delay(LaunchSettleDelay, cancellationToken).ConfigureAwait(false);

            bool running = await IsRunningAsync(serial, app, cancellationToken).ConfigureAwait(false);
            return running
                ? new CameraReadyResult(true, $"{app.Name} is in the foreground.")
                : new CameraReadyResult(
                    false,
                    $"{app.Name} did not come to the foreground. Open it on the phone and try again.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Camera foreground check failed.");
            return new CameraReadyResult(false, $"Could not prepare the camera: {exception.Message}");
        }
    }

    private async Task<bool> IsRunningAsync(
        string serial,
        CameraApp app,
        CancellationToken cancellationToken)
    {
        // mCurrentFocus is unreliable while scrcpy mirrors the device: it reads
        // null even with the camera genuinely in front. The process being alive
        // after an explicit launch is the dependable signal.
        ProcessExecutionResult result = await _adb
            .ExecuteAsync(serial, ["shell", "pidof", app.Package], cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded && result.StandardOutput.Trim().Length > 0;
    }
}
