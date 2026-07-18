using AstroDesk.Device.Adb;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class AutoCaptureTests
{
    [Fact]
    public void Timeout_AllowsForSlowWritesAndNoiseReduction()
    {
        // Samsung's long-exposure noise reduction can take about as long again
        // as the exposure, so a tight bound would abort healthy runs.
        TimeSpan timeout = AutoCaptureService.CalculateTimeout(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(4));

        Assert.True(timeout >= TimeSpan.FromSeconds(48), $"timeout was {timeout}");
    }

    [Fact]
    public void Timeout_HasAFloorForVeryShortExposures() =>
        Assert.True(
            AutoCaptureService.CalculateTimeout(TimeSpan.FromMilliseconds(10), TimeSpan.Zero)
                >= TimeSpan.FromSeconds(20));

    [Fact]
    public void SettleDelay_NeverDropsBelowTheFloor() =>
        Assert.Equal(
            AutoCaptureService.MinimumSettleDelay,
            AutoCaptureService.CalculateSettleDelay(TimeSpan.Zero));

    [Fact]
    public async Task Run_WaitsForTheFrameBeforeTakingTheNext()
    {
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        await service.StartAsync(
            new AutoCaptureOptions("SERIAL", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 3));

        await input.WaitForPressesAsync(1);
        Assert.Equal(1, input.PressCount);

        // No frame has landed, so nothing more may be fired no matter how long
        // we wait. This is the whole point of the redesign.
        await Task.Delay(400);
        Assert.Equal(1, input.PressCount);

        service.NotifyFileImported("cap001.jpg", DateTimeOffset.Now);
        await input.WaitForPressesAsync(2);
        Assert.Equal(2, input.PressCount);

        service.NotifyFileImported("cap002.jpg", DateTimeOffset.Now);
        await input.WaitForPressesAsync(3);
        Assert.Equal(3, input.PressCount);

        await service.StopAsync();
    }

    [Fact]
    public async Task Run_StopsAtTheRequestedFrameCount()
    {
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);
        var finished = new TaskCompletionSource<AutoCaptureFinished>();
        service.Finished += (_, args) => finished.TrySetResult(args);

        await service.StartAsync(
            new AutoCaptureOptions("SERIAL", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 2));

        await input.WaitForPressesAsync(1);
        service.NotifyFileImported("cap003.jpg", DateTimeOffset.Now);
        await input.WaitForPressesAsync(2);

        AutoCaptureFinished result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, result.FramesTriggered);
        Assert.False(result.TimedOut);
        Assert.Equal(2, input.PressCount);
    }

    [Fact]
    public async Task Run_ReportsATimeoutWhenNoFrameEverArrives()
    {
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);
        var finished = new TaskCompletionSource<AutoCaptureFinished>();
        service.Finished += (_, args) => finished.TrySetResult(args);

        // A shutter press that does nothing - volume key bound to zoom, or the
        // camera app not in the foreground - must not hang the run forever.
        await service.StartAsync(
            new AutoCaptureOptions("SERIAL", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 5));

        AutoCaptureFinished result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(40));

        Assert.True(result.TimedOut);
        Assert.Equal(1, result.FramesTriggered);
        Assert.Contains("volume key", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Run_UsesTheVolumeKeyNotACoordinateTap()
    {
        var input = new RecordingInputService();
        await using var service = new AutoCaptureService(input);

        await service.StartAsync(
            new AutoCaptureOptions("SERIAL", TimeSpan.Zero, TimeSpan.Zero, FrameCount: 1));
        await input.WaitForPressesAsync(1);
        await service.StopAsync();

        // A coordinate tap breaks on any layout or orientation change; the
        // volume key mapping does not.
        Assert.Equal(AndroidKeyCode.VolumeUp, input.LastKey);
        Assert.Equal(0, input.TapCount);
    }

    private sealed class RecordingInputService : IAdbInputService
    {
        private readonly SemaphoreSlim _pressed = new(0);
        private int _pressCount;

        public int PressCount => Volatile.Read(ref _pressCount);

        public int TapCount { get; private set; }

        public AndroidKeyCode? LastKey { get; private set; }

        public async Task WaitForPressesAsync(int count)
        {
            while (PressCount < count)
            {
                if (!await _pressed.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))
                {
                    throw new TimeoutException($"only saw {PressCount} of {count} presses");
                }
            }
        }

        public Task SendKeyAsync(
            string serial,
            AndroidKeyCode keyCode,
            CancellationToken cancellationToken = default)
        {
            LastKey = keyCode;
            Interlocked.Increment(ref _pressCount);
            _pressed.Release();
            return Task.CompletedTask;
        }

        public Task TapAsync(string serial, int x, int y, CancellationToken cancellationToken = default)
        {
            TapCount++;
            return Task.CompletedTask;
        }

        public Task SwipeAsync(
            string serial,
            int startX,
            int startY,
            int endX,
            int endY,
            TimeSpan duration,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(
            string serial,
            string text,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SetKeepAwakeAsync(
            string serial,
            bool enabled,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
