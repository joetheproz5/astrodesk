using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;

namespace AstroDesk.Device.Tests;

public sealed class AdbInputServiceTests
{
    [Fact]
    public async Task SwipeAsync_BuildsExpectedFallbackCommand()
    {
        var executor = new RecordingAdbExecutor();
        var service = new AdbInputService(executor);

        await service.SwipeAsync(
            "serial",
            10,
            20,
            300,
            400,
            TimeSpan.FromMilliseconds(750));

        var command = Assert.Single(executor.Commands);
        Assert.Equal(
            ["shell", "input", "swipe", "10", "20", "300", "400", "750"],
            command.Arguments);
    }

    [Fact]
    public async Task SendTextAsync_EncodesSpacesAndShellMetacharacters()
    {
        var executor = new RecordingAdbExecutor();
        var service = new AdbInputService(executor);

        await service.SendTextAsync("serial", "hello world&stars");

        var command = Assert.Single(executor.Commands);
        Assert.Equal("hello%sworld\\&stars", command.Arguments[^1]);
    }

    [Fact]
    public async Task SendKeyAsync_UsesAndroidKeyCode()
    {
        var executor = new RecordingAdbExecutor();
        var service = new AdbInputService(executor);

        await service.SendKeyAsync("serial", AndroidKeyCode.AppSwitch);

        Assert.Equal(
            ["shell", "input", "keyevent", "187"],
            Assert.Single(executor.Commands).Arguments);
    }

    private sealed class RecordingAdbExecutor : IAdbCommandExecutor
    {
        public List<(string? Serial, IReadOnlyList<string> Arguments)> Commands { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(
            string? serial,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Commands.Add((serial, arguments));
            return Task.FromResult(new ProcessExecutionResult(0, string.Empty, string.Empty, TimeSpan.Zero));
        }
    }
}
