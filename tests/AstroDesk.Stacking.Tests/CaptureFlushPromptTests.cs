using AstroDesk.Device.Adb;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class CaptureFlushPromptTests
{
    [Theory]
    [InlineData("Physical size: 1440x3088", 1440, 3088)]
    [InlineData("Physical size: 1080x2340\n", 1080, 2340)]
    public void ParseScreenSize_ReadsThePhysicalSize(string output, int width, int height) =>
        Assert.Equal((width, height), CaptureFlushPrompt.ParseScreenSize(output));

    [Fact]
    public void ParseScreenSize_PrefersAnOverrideOverThePhysicalSize()
    {
        // When a device reports both, the override is what is actually displayed,
        // so a tap computed from the physical size would land in the wrong place.
        const string output = "Physical size: 1440x3088\nOverride size: 1080x2316";

        Assert.Equal((1080, 2316), CaptureFlushPrompt.ParseScreenSize(output));
    }

    [Fact]
    public void ParseScreenSize_FallsBackRatherThanThrowing() =>
        Assert.Equal((1440, 3088), CaptureFlushPrompt.ParseScreenSize("something unexpected"));

    [Fact]
    public async Task Prompt_TapsTheThumbnailScaledToTheScreen()
    {
        var adb = new FakeAdb("Physical size: 1440x3088");
        var input = new RecordingInput();
        var prompt = new CaptureFlushPrompt(adb, input) { PromptDelay = TimeSpan.Zero };

        await prompt.PromptAsync("SERIAL");

        // Measured from a screenshot of Expert RAW on this panel.
        Assert.Equal((240, 2754), input.LastTap);
    }

    [Fact]
    public async Task Prompt_ScalesToADifferentPanel()
    {
        // The position is stored as a fraction precisely so another resolution
        // still lands on the control rather than beside it.
        var adb = new FakeAdb("Physical size: 1080x2340");
        var input = new RecordingInput();
        var prompt = new CaptureFlushPrompt(adb, input) { PromptDelay = TimeSpan.Zero };

        await prompt.PromptAsync("SERIAL");

        Assert.Equal((180, 2087), input.LastTap);
    }

    [Fact]
    public async Task Dismiss_SendsBackSoTheNextShutterReachesTheCamera()
    {
        var input = new RecordingInput();
        var prompt = new CaptureFlushPrompt(new FakeAdb("Physical size: 1440x3088"), input);

        await prompt.DismissAsync("SERIAL");

        // Leaving the review screen open means the next volume-key press goes to
        // the viewer and no photo is taken.
        Assert.Equal(AndroidKeyCode.Back, input.LastKey);
    }

    [Fact]
    public async Task Disabled_DoesNothingAtAll()
    {
        var input = new RecordingInput();
        var prompt = new CaptureFlushPrompt(new FakeAdb("Physical size: 1440x3088"), input)
        {
            IsEnabled = false,
            PromptDelay = TimeSpan.Zero,
        };

        await prompt.PromptAsync("SERIAL");
        await prompt.DismissAsync("SERIAL");

        Assert.Null(input.LastTap);
        Assert.Null(input.LastKey);
    }

    [Fact]
    public async Task Prompt_SurvivesAFailingDevice()
    {
        // A failed prompt only costs latency - the capture still completes on its
        // own - so it must never abort the run.
        var prompt = new CaptureFlushPrompt(new ThrowingAdb(), new RecordingInput())
        {
            PromptDelay = TimeSpan.Zero,
        };

        await prompt.PromptAsync("SERIAL");
    }

    private sealed class FakeAdb(string output) : IAdbCommandExecutor
    {
        public Task<AstroDesk.Device.Processes.ProcessExecutionResult> ExecuteAsync(
            string? serial,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AstroDesk.Device.Processes.ProcessExecutionResult(
                0, output, string.Empty, TimeSpan.Zero));
    }

    private sealed class ThrowingAdb : IAdbCommandExecutor
    {
        public Task<AstroDesk.Device.Processes.ProcessExecutionResult> ExecuteAsync(
            string? serial,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("device offline");
    }

    private sealed class RecordingInput : IAdbInputService
    {
        public (int X, int Y)? LastTap { get; private set; }

        public AndroidKeyCode? LastKey { get; private set; }

        public Task TapAsync(string serial, int x, int y, CancellationToken cancellationToken = default)
        {
            LastTap = (x, y);
            return Task.CompletedTask;
        }

        public Task SendKeyAsync(string serial, AndroidKeyCode keyCode, CancellationToken cancellationToken = default)
        {
            LastKey = keyCode;
            return Task.CompletedTask;
        }

        public Task SwipeAsync(
            string serial, int startX, int startY, int endX, int endY,
            TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(string serial, string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetKeepAwakeAsync(string serial, bool enabled, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
