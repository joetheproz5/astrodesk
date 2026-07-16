using AstroDesk.Device.Adb;

namespace AstroDesk.Device.Tests;

public sealed class DeviceMonitorTests
{
    [Fact]
    public async Task PollOnceAsync_SerializesConcurrentPolls()
    {
        var client = new ConcurrencyTrackingAdbClient();
        await using var monitor = new DeviceMonitor(
            client,
            new DeviceMonitorOptions
            {
                AutoReconnect = false,
                RefreshInterval = TimeSpan.FromSeconds(10),
            });

        await Task.WhenAll(
            monitor.PollOnceAsync(),
            monitor.PollOnceAsync(),
            monitor.PollOnceAsync());

        Assert.Equal(1, client.MaximumConcurrentCalls);
        Assert.Equal(3, client.ConnectionCalls);
    }

    [Fact]
    public async Task PollOnceAsync_ReconnectsDisconnectedDeviceThenReadsStatus()
    {
        var client = new ReconnectingAdbClient();
        await using var monitor = new DeviceMonitor(
            client,
            new DeviceMonitorOptions
            {
                AutoReconnect = true,
                ReconnectCooldown = TimeSpan.Zero,
            });

        var result = await monitor.PollOnceAsync();

        Assert.Equal(1, client.ReconnectCalls);
        Assert.Equal(AdbConnectionState.Connected, result.Connection.State);
        Assert.NotNull(result.PhoneStatus);
    }

    private sealed class ConcurrencyTrackingAdbClient : IAdbDeviceClient
    {
        private int _currentCalls;
        private int _maximumConcurrentCalls;
        private int _connectionCalls;

        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);

        public int ConnectionCalls => Volatile.Read(ref _connectionCalls);

        public Task<AdbDeviceList> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<AdbConnectionSnapshot> GetConnectionAsync(
            string? preferredSerial = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _connectionCalls);
            var current = Interlocked.Increment(ref _currentCalls);
            UpdateMaximum(current);
            try
            {
                await Task.Delay(30, cancellationToken);
                return Disconnected();
            }
            finally
            {
                Interlocked.Decrement(ref _currentCalls);
            }
        }

        public Task<PhoneStatus> GetPhoneStatusAsync(
            string serial,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ReconnectAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        private void UpdateMaximum(int current)
        {
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentCalls);
                if (current <= maximum
                    || Interlocked.CompareExchange(ref _maximumConcurrentCalls, current, maximum) == maximum)
                {
                    return;
                }
            }
        }
    }

    private sealed class ReconnectingAdbClient : IAdbDeviceClient
    {
        private bool _connected;

        public int ReconnectCalls { get; private set; }

        public Task<AdbDeviceList> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdbConnectionSnapshot> GetConnectionAsync(
            string? preferredSerial = null,
            CancellationToken cancellationToken = default)
        {
            if (!_connected)
            {
                return Task.FromResult(Disconnected());
            }

            var device = new AdbDevice("serial", AdbDeviceState.Device, "SM-S918B");
            return Task.FromResult(
                new AdbConnectionSnapshot(
                    AdbConnectionState.Connected,
                    [device],
                    device,
                    "Connected."));
        }

        public Task<PhoneStatus> GetPhoneStatusAsync(
            string serial,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new PhoneStatus(
                    "SM-S918B",
                    "14",
                    80,
                    ChargingState.Charging,
                    32m,
                    4.1m,
                    null,
                    null,
                    null,
                    null,
                    UsbConnectionState.Connected,
                    DateTimeOffset.UtcNow));

        public Task ReconnectAsync(CancellationToken cancellationToken = default)
        {
            ReconnectCalls++;
            _connected = true;
            return Task.CompletedTask;
        }
    }

    private static AdbConnectionSnapshot Disconnected() =>
        new(
            AdbConnectionState.Disconnected,
            Array.Empty<AdbDevice>(),
            null,
            "Disconnected.");
}
