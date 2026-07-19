using System.Diagnostics;
using AstroDesk.Device.Processes;
using Xunit;

namespace AstroDesk.Device.Tests;

/// <summary>
/// Covers a wedged tool being given up on rather than waited for forever.
/// </summary>
/// <remarks>
/// Every adb call used to wait on the app's lifetime token alone, so a phone
/// that stopped answering blocked indefinitely. The damage was to the UI rather
/// than the tool: a command is disabled while its task runs, and scrcpy's
/// lifecycle semaphore meant the stuck call also blocked stop and reconnect, so
/// all three buttons greyed out permanently with no way back but restarting.
/// </remarks>
public sealed class ProcessTimeoutTests
{
    /// <summary>
    /// A process that outlives its timeout, without depending on any tool being
    /// installed: ping sleeps roughly a second between echoes.
    /// </summary>
    private static ProcessInvocation SlowCommand(TimeSpan timeout) =>
        new(
            "ping",
            ["-n", "20", "127.0.0.1"],
            Timeout: timeout);

    [Fact]
    public async Task AWedgedToolIsStoppedRatherThanWaitedFor()
    {
        ProcessRunner runner = new();
        Stopwatch stopwatch = Stopwatch.StartNew();

        ToolProcessTimeoutException exception =
            await Assert.ThrowsAsync<ToolProcessTimeoutException>(
                () => runner.RunAsync(SlowCommand(TimeSpan.FromSeconds(1))));

        stopwatch.Stop();

        // The point is that it returns at all, and promptly. Twenty pings would
        // run for about twenty seconds.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"gave up after {stopwatch.Elapsed.TotalSeconds:0.#}s, which is not prompt");
        Assert.Equal(TimeSpan.FromSeconds(1), exception.Timeout);
    }

    [Fact]
    public async Task TheTimeoutIsDistinctFromCancellation()
    {
        // The caller has to be able to tell "the user gave up" from "the tool
        // wedged", because only the second one is worth explaining on screen.
        ProcessRunner runner = new();
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(SlowCommand(TimeSpan.FromSeconds(30)), cancelled.Token));
    }

    [Fact]
    public async Task CancellingDuringTheRunIsNotReportedAsATimeout()
    {
        ProcessRunner runner = new();
        using CancellationTokenSource cancellation = new();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(400));

        Exception exception = await Record.ExceptionAsync(
            () => runner.RunAsync(SlowCommand(TimeSpan.FromSeconds(30)), cancellation.Token));

        Assert.IsNotType<ToolProcessTimeoutException>(exception);
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task AQuickToolIsUnaffected()
    {
        ProcessRunner runner = new();

        ProcessExecutionResult result = await runner.RunAsync(
            new ProcessInvocation("cmd", ["/c", "echo", "ok"], Timeout: TimeSpan.FromSeconds(20)));

        Assert.True(result.Succeeded);
        Assert.Contains("ok", result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThereIsADefaultTimeoutSoNoCallerCanHangByOmission()
    {
        // The regression that matters: an invocation that does not ask for a
        // timeout must still get one, because every existing caller omits it.
        Assert.True(ProcessRunner.DefaultTimeout > TimeSpan.Zero);
        Assert.True(
            ProcessRunner.DefaultTimeout <= TimeSpan.FromMinutes(1),
            "a default this long would still read as a frozen app");
    }
}
