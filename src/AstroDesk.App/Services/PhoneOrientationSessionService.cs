using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;

namespace AstroDesk.App.Services;

public interface IPhoneOrientationSessionService : IAsyncDisposable
{
    Task EnterLandscapeAsync(string serial, CancellationToken cancellationToken = default);

    Task RestoreAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Locks Android into landscape for the embedded scrcpy session, then restores
/// the user's previous auto-rotate and rotation settings on disconnect.
/// </summary>
public sealed class PhoneOrientationSessionService(
    IAdbCommandExecutor adb,
    ILogger<PhoneOrientationSessionService> logger) : IPhoneOrientationSessionService
{
    private readonly IAdbCommandExecutor _adb = adb;
    private readonly ILogger<PhoneOrientationSessionService> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _serial;
    private string? _savedAccelerometerRotation;
    private string? _savedUserRotation;

    public async Task EnterLandscapeAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestoreCoreAsync(cancellationToken).ConfigureAwait(false);
            string? accelerometer = await GetSettingAsync(serial, "accelerometer_rotation", cancellationToken)
                .ConfigureAwait(false);
            string? rotation = await GetSettingAsync(serial, "user_rotation", cancellationToken)
                .ConfigureAwait(false);

            bool autoRotationSet = await PutSettingAsync(
                    serial,
                    "accelerometer_rotation",
                    "0",
                    cancellationToken)
                .ConfigureAwait(false);
            bool landscapeSet = await PutSettingAsync(serial, "user_rotation", "1", cancellationToken)
                .ConfigureAwait(false);
            if (!autoRotationSet || !landscapeSet)
            {
                _logger.LogWarning("Android did not accept the requested landscape orientation.");
                return;
            }

            _serial = serial;
            _savedAccelerometerRotation = accelerometer;
            _savedUserRotation = rotation;
            await Task.Delay(350, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RestoreCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RestoreAsync().ConfigureAwait(false);
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RestoreCoreAsync(CancellationToken cancellationToken)
    {
        string? serial = _serial;
        string? accelerometer = _savedAccelerometerRotation;
        string? rotation = _savedUserRotation;
        _serial = null;
        _savedAccelerometerRotation = null;
        _savedUserRotation = null;
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        if (accelerometer is "0" or "1")
        {
            _ = await PutSettingAsync(serial, "accelerometer_rotation", accelerometer, cancellationToken)
                .ConfigureAwait(false);
        }

        if (rotation is "0" or "1" or "2" or "3")
        {
            _ = await PutSettingAsync(serial, "user_rotation", rotation, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<string?> GetSettingAsync(
        string serial,
        string key,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _adb.ExecuteAsync(
                serial,
                ["shell", "settings", "get", "system", key],
                cancellationToken)
            .ConfigureAwait(false);
        return result.Succeeded ? result.StandardOutput.Trim() : null;
    }

    private async Task<bool> PutSettingAsync(
        string serial,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _adb.ExecuteAsync(
                serial,
                ["shell", "settings", "put", "system", key, value],
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogWarning("Could not set Android {Setting}: {Error}", key, result.StandardError.Trim());
        }

        return result.Succeeded;
    }
}
