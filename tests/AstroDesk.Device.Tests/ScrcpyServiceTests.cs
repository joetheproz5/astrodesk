using AstroDesk.Device.Processes;
using AstroDesk.Device.Scrcpy;

namespace AstroDesk.Device.Tests;

public sealed class ScrcpyServiceTests
{
    [Fact]
    public async Task StartAsync_StartsRedirectedChildFindsAndHidesUniqueWindow()
    {
        var runner = new FakeProcessRunner();
        var windows = new FakeWindowManager();
        await using var service = CreateService(runner, windows);

        var session = await service.StartAsync(
            new ScrcpyLaunchOptions
            {
                DeviceSerial = "SECRET-SERIAL",
                WindowDiscoveryTimeout = TimeSpan.FromSeconds(1),
            });

        Assert.Equal(ScrcpyState.Running, service.State);
        Assert.Equal(new nint(456), session.WindowHandle);
        Assert.StartsWith("AstroDesk-Phone-Preview-", session.WindowTitle, StringComparison.Ordinal);
        Assert.Equal(session.WindowTitle, windows.RequestedTitle);
        Assert.True(windows.Hidden);

        var invocation = Assert.IsType<ProcessInvocation>(runner.Invocation);
        Assert.False(invocation.CreateNoWindow);
        Assert.Contains("--no-audio", invocation.Arguments);
        Assert.Contains("--stay-awake", invocation.Arguments);
        Assert.Contains("SECRET-SERIAL", invocation.SensitiveValues!);
    }

    [Fact]
    public async Task UnexpectedExit_RaisesCrashEventAndClearsSession()
    {
        var runner = new FakeProcessRunner();
        var windows = new FakeWindowManager();
        await using var service = CreateService(runner, windows);
        var crashed = new TaskCompletionSource<ScrcpyCrashedEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Crashed += (_, args) => crashed.TrySetResult(args);

        var session = await service.StartAsync(new ScrcpyLaunchOptions());
        runner.Child.Exit(17);
        var args = await crashed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(17, args.ExitCode);
        Assert.Equal(session, args.Session);
        Assert.Equal(ScrcpyState.Crashed, service.State);
        Assert.Null(service.CurrentSession);
        Assert.True(windows.Restored);
    }

    private static ScrcpyService CreateService(
        FakeProcessRunner runner,
        FakeWindowManager windows) =>
        new(
            runner,
            new FixedExecutableLocator(),
            windows,
            new DeviceToolOptions());

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public FakeChildProcess Child { get; } = new();

        public ProcessInvocation? Invocation { get; private set; }

        public Task<ProcessExecutionResult> RunAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IChildProcess> StartAsync(
            ProcessInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            Invocation = invocation;
            return Task.FromResult<IChildProcess>(Child);
        }
    }

    private sealed class FakeChildProcess : IChildProcess
    {
        private readonly TaskCompletionSource<int> _exit =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int? _exitCode;

        public int Id => 123;

        public bool HasExited => _exit.Task.IsCompleted;

        public int? ExitCode => _exitCode;

        public Task<int> WaitForExitAsync(CancellationToken cancellationToken) =>
            _exit.Task.WaitAsync(cancellationToken);

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Exit(0);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Exit(int exitCode)
        {
            _exitCode = exitCode;
            _exit.TrySetResult(exitCode);
        }
    }

    private sealed class FakeWindowManager : IScrcpyWindowManager
    {
        public string? RequestedTitle { get; private set; }

        public bool Hidden { get; private set; }

        public bool Restored { get; private set; }

        public Task<nint> FindWindowAsync(
            string exactTitle,
            int processId,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            RequestedTitle = exactTitle;
            return Task.FromResult(new nint(456));
        }

        public WindowPresentationState HideOffscreenWithoutMinimizing(nint windowHandle)
        {
            Hidden = true;
            return new WindowPresentationState(
                windowHandle,
                new NativeWindowRect(10, 10, 1010, 2010),
                nint.Zero);
        }

        public void Restore(WindowPresentationState state)
        {
            Restored = true;
        }
    }

    private sealed class FixedExecutableLocator : IExecutableLocator
    {
        public string? Find(ExecutableRequest request) => "C:\\tools\\scrcpy.exe";

        public string Resolve(ExecutableRequest request) => "C:\\tools\\scrcpy.exe";
    }
}
