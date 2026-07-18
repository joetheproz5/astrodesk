using System.Globalization;
using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Stacking;

/// <summary>
/// The phone's original display timeout, so it can be put back afterwards.
/// </summary>
public sealed record ScreenWakeState(string Serial, string? PreviousTimeout)
{
    public bool HasPrevious => !string.IsNullOrWhiteSpace(PreviousTimeout);
}

public interface IScreenWakeGuard
{
    /// <summary>Holds the display on for the duration of a run.</summary>
    Task<ScreenWakeState> AcquireAsync(string serial, CancellationToken cancellationToken = default);

    /// <summary>Restores the phone's own timeout.</summary>
    Task ReleaseAsync(ScreenWakeState state, CancellationToken cancellationToken = default);

    /// <summary>Wakes the display if it has gone off despite the guard.</summary>
    Task EnsureAwakeAsync(string serial, CancellationToken cancellationToken = default);
}

/// <summary>
/// Keeps the phone's display awake across an unattended capture run.
/// </summary>
/// <remarks>
/// <para>
/// A volume-key shutter only reaches the camera while the display is on, so an
/// unattended run dies silently the moment the phone sleeps. On an S23 Ultra the
/// display timeout was 30 seconds while an Expert RAW frame takes 30 to 95
/// seconds, meaning the screen switched off during almost every frame.
/// </para>
/// <para>
/// <c>svc power stayon usb</c> is not usable here: it only holds the display on
/// while the phone is charging, and a wireless ADB session has no cable. The
/// display timeout is therefore raised for the run and restored afterwards,
/// rather than left changed behind the user's back.
/// </para>
/// </remarks>
public sealed class ScreenWakeGuard(
    IAdbCommandExecutor adb,
    IAdbInputService input,
    ILogger<ScreenWakeGuard>? logger = null) : IScreenWakeGuard
{
    /// <summary>
    /// Display timeout held during a run. Comfortably longer than the slowest
    /// frame observed, and restored when the run ends.
    /// </summary>
    public const int RunTimeoutMilliseconds = 30 * 60 * 1000;

    private readonly IAdbCommandExecutor _adb = adb;
    private readonly IAdbInputService _input = input;
    private readonly ILogger<ScreenWakeGuard> _logger =
        logger ?? NullLogger<ScreenWakeGuard>.Instance;

    public async Task<ScreenWakeState> AcquireAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        string? previous = null;
        try
        {
            ProcessExecutionResult read = await _adb
                .ExecuteAsync(serial, ["shell", "settings", "get", "system", "screen_off_timeout"], cancellationToken)
                .ConfigureAwait(false);

            previous = NormaliseTimeout(read.StandardOutput);

            await _adb
                .ExecuteAsync(
                    serial,
                    [
                        "shell", "settings", "put", "system", "screen_off_timeout",
                        RunTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture),
                    ],
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureAwakeAsync(serial, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Display timeout raised for the run (was {Previous} ms).",
                previous ?? "unknown");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Losing the guard only risks the screen sleeping; the per-frame
            // wake still recovers it, so this must not abort the run.
            _logger.LogWarning(exception, "Could not raise the display timeout.");
        }

        return new ScreenWakeState(serial, previous);
    }

    public async Task ReleaseAsync(
        ScreenWakeState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.HasPrevious)
        {
            return;
        }

        try
        {
            await _adb
                .ExecuteAsync(
                    state.Serial,
                    ["shell", "settings", "put", "system", "screen_off_timeout", state.PreviousTimeout!],
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation("Display timeout restored to {Previous} ms.", state.PreviousTimeout);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not restore the display timeout.");
        }
    }

    public async Task EnsureAwakeAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        try
        {
            // KEYCODE_WAKEUP only wakes; unlike POWER it never toggles the
            // display off when it is already on.
            await _input
                .SendKeyAsync(serial, AndroidKeyCode.Wakeup, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not wake the phone display.");
        }
    }

    /// <summary>
    /// Reads the timeout back, rejecting the "null" that a device returns when
    /// the setting has never been written.
    /// </summary>
    public static string? NormaliseTimeout(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        string trimmed = output.Trim();
        if (trimmed.Length == 0 ||
            trimmed.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value <= 0)
        {
            return null;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}
