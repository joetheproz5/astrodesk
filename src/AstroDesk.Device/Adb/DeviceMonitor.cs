using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Device.Adb;

public sealed class DeviceMonitorOptions
{
    private TimeSpan _refreshInterval = TimeSpan.FromSeconds(10);
    private TimeSpan _reconnectCooldown = TimeSpan.FromSeconds(30);

    public TimeSpan RefreshInterval
    {
        get => _refreshInterval;
        set => _refreshInterval = value > TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public bool AutoReconnect { get; set; } = true;

    public TimeSpan ReconnectCooldown
    {
        get => _reconnectCooldown;
        set => _reconnectCooldown = value >= TimeSpan.Zero
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public string? PreferredSerial { get; set; }
}

public sealed record DeviceMonitorSnapshot(
    AdbConnectionSnapshot Connection,
    PhoneStatus? PhoneStatus,
    DateTimeOffset CheckedAt,
    string? ErrorMessage = null);

public sealed class DeviceMonitorSnapshotEventArgs : EventArgs
{
    public DeviceMonitorSnapshotEventArgs(DeviceMonitorSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public DeviceMonitorSnapshot Snapshot { get; }
}

public interface IDeviceMonitor : IAsyncDisposable
{
    event EventHandler<DeviceMonitorSnapshotEventArgs>? SnapshotChanged;

    DeviceMonitorSnapshot? LastSnapshot { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task<DeviceMonitorSnapshot> PollOnceAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Polls one cycle at a time and serializes explicit polls, preventing overlapping ADB work.
/// </summary>
public sealed class DeviceMonitor : IDeviceMonitor
{
    private readonly IAdbDeviceClient _client;
    private readonly DeviceMonitorOptions _options;
    private readonly ILogger<DeviceMonitor> _logger;
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly object _lifecycleSync = new();
    private CancellationTokenSource? _runCancellation;
    private Task? _runTask;
    private DateTimeOffset _lastReconnectAttempt = DateTimeOffset.MinValue;
    private DeviceMonitorSnapshot? _lastSnapshot;

    public DeviceMonitor(
        IAdbDeviceClient client,
        DeviceMonitorOptions options,
        ILogger<DeviceMonitor>? logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<DeviceMonitor>.Instance;
    }

    public event EventHandler<DeviceMonitorSnapshotEventArgs>? SnapshotChanged;

    public DeviceMonitorSnapshot? LastSnapshot => Volatile.Read(ref _lastSnapshot);

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_lifecycleSync)
        {
            if (_runTask is { IsCompleted: false })
            {
                return Task.CompletedTask;
            }

            _runCancellation?.Dispose();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _runTask = RunAsync(_runCancellation.Token);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task? runTask;
        lock (_lifecycleSync)
        {
            _runCancellation?.Cancel();
            runTask = _runTask;
        }

        if (runTask is null)
        {
            return;
        }

        try
        {
            await runTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_runCancellation?.IsCancellationRequested == true)
        {
        }
    }

    public async Task<DeviceMonitorSnapshot> PollOnceAsync(CancellationToken cancellationToken = default)
    {
        await _pollGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await _client.GetConnectionAsync(
                _options.PreferredSerial,
                cancellationToken).ConfigureAwait(false);

            if (ShouldReconnect(connection))
            {
                _lastReconnectAttempt = DateTimeOffset.UtcNow;
                try
                {
                    await _client.ReconnectAsync(cancellationToken).ConfigureAwait(false);
                    connection = await _client.GetConnectionAsync(
                        _options.PreferredSerial,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(exception, "ADB reconnect attempt failed.");
                }
            }

            PhoneStatus? status = null;
            if (connection.IsConnected)
            {
                status = await _client.GetPhoneStatusAsync(
                    connection.SelectedDevice!.Serial,
                    cancellationToken).ConfigureAwait(false);
            }

            var snapshot = new DeviceMonitorSnapshot(connection, status, DateTimeOffset.UtcNow);
            Publish(snapshot);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Device monitoring poll failed.");
            var connection = LastSnapshot?.Connection
                             ?? new AdbConnectionSnapshot(
                                 AdbConnectionState.Unknown,
                                 Array.Empty<AdbDevice>(),
                                 null,
                                 "ADB status is unavailable.");
            var snapshot = new DeviceMonitorSnapshot(
                connection,
                null,
                DateTimeOffset.UtcNow,
                exception.Message);
            Publish(snapshot);
            return snapshot;
        }
        finally
        {
            _pollGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_lifecycleSync)
        {
            _runCancellation?.Dispose();
            _runCancellation = null;
            _runTask = null;
        }

        _pollGate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await PollOnceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(_options.RefreshInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool ShouldReconnect(AdbConnectionSnapshot connection)
    {
        if (!_options.AutoReconnect
            || connection.State is not (AdbConnectionState.Disconnected or AdbConnectionState.Offline))
        {
            return false;
        }

        return DateTimeOffset.UtcNow - _lastReconnectAttempt >= _options.ReconnectCooldown;
    }

    private void Publish(DeviceMonitorSnapshot snapshot)
    {
        Volatile.Write(ref _lastSnapshot, snapshot);
        var handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        var eventArgs = new DeviceMonitorSnapshotEventArgs(snapshot);
        foreach (EventHandler<DeviceMonitorSnapshotEventArgs> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this, eventArgs);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "A device monitor subscriber threw an exception.");
            }
        }
    }
}
