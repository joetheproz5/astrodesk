using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;

namespace AstroDesk.Device.Tests;

public sealed class AdbServiceTests
{
    [Fact]
    public async Task GetConnectionAsync_RequiresSelectionWhenMultipleDevicesAreReady()
    {
        var runner = new RecordingProcessRunner
        {
            RunResult = new ProcessExecutionResult(
                0,
                """
                List of devices attached
                SERIAL-A device model:Phone_A transport_id:1
                SERIAL-B device model:Phone_B transport_id:2
                """,
                string.Empty,
                TimeSpan.Zero),
        };
        var service = CreateService(runner);

        var unselected = await service.GetConnectionAsync();
        var selected = await service.GetConnectionAsync("SERIAL-B");

        Assert.Equal(AdbConnectionState.MultipleDevices, unselected.State);
        Assert.Equal(AdbConnectionState.Connected, selected.State);
        Assert.Equal("SERIAL-B", selected.SelectedDevice?.Serial);
        Assert.DoesNotContain("SERIAL-B", selected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_MarksSerialAsSensitiveAndUsesArgumentList()
    {
        var runner = new RecordingProcessRunner();
        var service = CreateService(runner);

        _ = await service.ExecuteAsync("SECRET-SERIAL", ["shell", "wm", "size"]);

        var invocation = Assert.Single(runner.Invocations);
        Assert.Equal(["-s", "SECRET-SERIAL", "shell", "wm", "size"], invocation.Arguments);
        Assert.Contains("SECRET-SERIAL", invocation.SensitiveValues!);
    }

    private static AdbService CreateService(RecordingProcessRunner runner) =>
        new(
            runner,
            new FixedExecutableLocator("C:\\tools\\adb.exe"),
            new DeviceToolOptions());

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public List<ProcessInvocation> Invocations { get; } = [];

        public ProcessExecutionResult RunResult { get; set; } =
            new(0, string.Empty, string.Empty, TimeSpan.Zero);

        public Task<ProcessExecutionResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocations.Add(invocation);
            return Task.FromResult(RunResult);
        }

        public Task<IChildProcess> StartAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedExecutableLocator(string path) : IExecutableLocator
    {
        public string? Find(ExecutableRequest request) => path;

        public string Resolve(ExecutableRequest request) => path;
    }
}
