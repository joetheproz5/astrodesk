using AstroDesk.Device.Adb;
using AstroDesk.Device.Processes;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class ScreenWakeGuardTests
{
    [Theory]
    [InlineData("30000\n", "30000")]
    [InlineData("  60000  ", "60000")]
    public void NormaliseTimeout_ReadsAValidSetting(string output, string expected) =>
        Assert.Equal(expected, ScreenWakeGuard.NormaliseTimeout(output));

    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("not a number")]
    [InlineData("0")]
    [InlineData("-1")]
    public void NormaliseTimeout_RejectsAnythingUnusable(string output) =>
        // A device returns "null" when the setting was never written; restoring
        // that literally would be worse than leaving the value alone.
        Assert.Null(ScreenWakeGuard.NormaliseTimeout(output));

    [Fact]
    public async Task Acquire_RaisesTheTimeoutAndRemembersTheOldOne()
    {
        // Measured on an S23 Ultra: the display timeout was 30s while a single
        // Expert RAW frame takes 30-95s, so the screen switched off mid-frame and
        // the next volume-key press never reached the camera.
        var adb = new RecordingAdb("30000");
        var guard = new ScreenWakeGuard(adb, new RecordingInput());

        ScreenWakeState state = await guard.AcquireAsync("SERIAL");

        Assert.Equal("30000", state.PreviousTimeout);
        Assert.Contains(
            adb.Commands,
            command => command.Contains("screen_off_timeout") &&
                       command.Contains(ScreenWakeGuard.RunTimeoutMilliseconds.ToString()));
    }

    [Fact]
    public async Task Release_PutsThePhonesOwnTimeoutBack()
    {
        var adb = new RecordingAdb("30000");
        var guard = new ScreenWakeGuard(adb, new RecordingInput());

        ScreenWakeState state = await guard.AcquireAsync("SERIAL");
        adb.Commands.Clear();
        await guard.ReleaseAsync(state);

        // Leaving a phone with a 30 minute display timeout after a session would
        // be a rude thing to do to someone's device.
        Assert.Contains(
            adb.Commands,
            command => command.Contains("screen_off_timeout") && command.Contains("30000"));
    }

    [Fact]
    public async Task Release_DoesNothingWhenThereWasNothingToRestore()
    {
        var adb = new RecordingAdb("null");
        var guard = new ScreenWakeGuard(adb, new RecordingInput());

        ScreenWakeState state = await guard.AcquireAsync("SERIAL");
        adb.Commands.Clear();
        await guard.ReleaseAsync(state);

        Assert.Empty(adb.Commands);
    }

    [Fact]
    public async Task EnsureAwake_UsesWakeupRatherThanPower()
    {
        var input = new RecordingInput();
        var guard = new ScreenWakeGuard(new RecordingAdb("30000"), input);

        await guard.EnsureAwakeAsync("SERIAL");

        // POWER toggles: sending it to an awake phone switches the display off,
        // which is the very failure this guard exists to prevent.
        Assert.Equal(AndroidKeyCode.Wakeup, input.LastKey);
        Assert.NotEqual(AndroidKeyCode.Power, input.LastKey);
    }

    [Fact]
    public async Task Acquire_SurvivesAFailingDevice()
    {
        // Losing the guard only risks the screen sleeping, which the per-frame
        // wake still recovers, so it must not abort the run.
        var guard = new ScreenWakeGuard(new ThrowingAdb(), new RecordingInput());

        ScreenWakeState state = await guard.AcquireAsync("SERIAL");

        Assert.False(state.HasPrevious);
    }

    private sealed class RecordingAdb(string readValue) : IAdbCommandExecutor
    {
        public List<string> Commands { get; } = [];

        public Task<ProcessExecutionResult> ExecuteAsync(
            string? serial,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(string.Join(' ', arguments));
            return Task.FromResult(new ProcessExecutionResult(0, readValue, string.Empty, TimeSpan.Zero));
        }
    }

    private sealed class ThrowingAdb : IAdbCommandExecutor
    {
        public Task<ProcessExecutionResult> ExecuteAsync(
            string? serial,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("device offline");
    }

    private sealed class RecordingInput : IAdbInputService
    {
        public AndroidKeyCode? LastKey { get; private set; }

        public Task SendKeyAsync(string serial, AndroidKeyCode keyCode, CancellationToken cancellationToken = default)
        {
            LastKey = keyCode;
            return Task.CompletedTask;
        }

        public Task TapAsync(string serial, int x, int y, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SwipeAsync(
            string serial, int startX, int startY, int endX, int endY,
            TimeSpan duration, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SendTextAsync(string serial, string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetKeepAwakeAsync(string serial, bool enabled, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
