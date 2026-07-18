using System.IO;
using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;

namespace AstroDesk.App.Services;

public sealed class PhonePhotoImportedEventArgs(string localPath) : EventArgs
{
    public string LocalPath { get; } = localPath;
}

public interface IPhonePhotoSyncService : IAsyncDisposable
{
    event EventHandler<PhonePhotoImportedEventArgs>? PhotoImported;

    bool Enabled { get; set; }

    string DestinationFolder { get; set; }

    Task StartAsync(string serial, CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Watches Samsung's camera folder while a scrcpy session is active and pulls
/// newly-created full-resolution photos to the laptop.
/// </summary>
public sealed class PhonePhotoSyncService(
    IAdbCommandExecutor adb,
    ILogger<PhonePhotoSyncService> logger) : IPhonePhotoSyncService
{
    private const string RemoteCameraFolder = "/sdcard/DCIM/Camera";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private readonly IAdbCommandExecutor _adb = adb;
    private readonly ILogger<PhonePhotoSyncService> _logger = logger;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;

    public event EventHandler<PhonePhotoImportedEventArgs>? PhotoImported;

    public bool Enabled { get; set; } = true;

    public string DestinationFolder { get; set; } = string.Empty;

    public async Task StartAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        cancellationToken.ThrowIfCancellationRequested();
        await StopAsync(cancellationToken).ConfigureAwait(false);

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _runCancellation = new CancellationTokenSource();
            _runTask = RunAsync(serial, _runCancellation.Token);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        CancellationTokenSource? runCancellation;
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            runCancellation = _runCancellation;
            runCancellation?.Cancel();
            runTask = _runTask;
            _runTask = null;
            _runCancellation = null;
        }
        finally
        {
            _lifecycle.Release();
        }

        if (runTask is not null)
        {
            try
            {
                await runTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        runCancellation?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(string serial, CancellationToken cancellationToken)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        Dictionary<string, int> pending = new(StringComparer.Ordinal);
        bool baselineEstablished = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<string>? remotePhotos = await ListRemotePhotosAsync(serial, cancellationToken)
                    .ConfigureAwait(false);
                if (remotePhotos is not null)
                {
                    if (!baselineEstablished)
                    {
                        seen.UnionWith(remotePhotos);
                        baselineEstablished = true;
                        _logger.LogInformation(
                            "Phone photo sync is watching {RemoteFolder} on {Serial}.",
                            RemoteCameraFolder,
                            serial);
                    }
                    else if (Enabled)
                    {
                        foreach (string remotePath in remotePhotos.Where(path => !seen.Contains(path)))
                        {
                            int observations = pending.GetValueOrDefault(remotePath) + 1;
                            pending[remotePath] = observations;
                            if (observations < 2)
                            {
                                continue;
                            }

                            string? localPath = await PullPhotoAsync(serial, remotePath, cancellationToken)
                                .ConfigureAwait(false);
                            if (localPath is null)
                            {
                                continue;
                            }

                            seen.Add(remotePath);
                            pending.Remove(remotePath);
                            PhotoImported?.Invoke(this, new PhonePhotoImportedEventArgs(localPath));
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Phone photo sync poll failed; it will retry.");
            }

            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<IReadOnlyList<string>?> ListRemotePhotosAsync(
        string serial,
        CancellationToken cancellationToken)
    {
        ProcessExecutionResult result = await _adb.ExecuteAsync(
                serial,
                ["shell", "ls", "-1", RemoteCameraFolder],
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            _logger.LogDebug("Could not list phone camera folder: {Error}", result.StandardError.Trim());
            return null;
        }

        return result.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName) && IsPhoto(fileName))
            .Select(fileName => $"{RemoteCameraFolder}/{fileName}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<string?> PullPhotoAsync(
        string serial,
        string remotePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(DestinationFolder))
        {
            _logger.LogWarning("Phone photo sync has no destination folder configured.");
            return null;
        }

        string destination = Path.GetFullPath(Environment.ExpandEnvironmentVariables(DestinationFolder));
        Directory.CreateDirectory(destination);
        string fileName = Path.GetFileName(remotePath);
        string localPath = Path.Combine(destination, fileName);
        if (File.Exists(localPath))
        {
            return localPath;
        }

        string temporaryPath = localPath + $".partial-{Guid.NewGuid():N}";
        try
        {
            ProcessExecutionResult result = await _adb.ExecuteAsync(
                    serial,
                    ["pull", remotePath, temporaryPath],
                    cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded || !File.Exists(temporaryPath))
            {
                _logger.LogWarning("Could not import {RemotePath}: {Error}", remotePath, result.StandardError.Trim());
                return null;
            }

            File.Move(temporaryPath, localPath, false);
            _logger.LogInformation("Imported phone photo {RemotePath} to {LocalPath}.", remotePath, localPath);
            return localPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsPhoto(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() is ".jpg" or ".jpeg" or ".heic" or ".dng" or ".png";
}
