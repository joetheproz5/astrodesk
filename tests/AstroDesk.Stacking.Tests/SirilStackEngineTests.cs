using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class SirilStackEngineTests
{
    [Fact]
    public void ReportsFailure_DetectsAbortedRunDespiteSuccessExitCode()
    {
        // Observed against Siril 1.4.4: when registration cannot solve the
        // frames the script aborts, but siril-cli still exits 0. Trusting the
        // exit code alone reported a stack that never happened.
        const string log = """
            log: Cannot perform star matching: try #3. Image 12 skipped
            log: No image was registered to the reference
            log: Registration aborted.
            log: Script execution failed.
            """;

        Assert.True(SirilStackEngine.ReportsFailure(log));
    }

    [Fact]
    public void ReportsFailure_IsFalseForASuccessfulRun()
    {
        const string log = """
            log: Stacked sequence successfully.
            log: Saving FITS: file stacked.fit, 1 layer(s), 640x480 pixels, 32 bits
            log: Script execution finished successfully.
            """;

        Assert.False(SirilStackEngine.ReportsFailure(log));
    }

    [Theory]
    [InlineData("log: Registration aborted.")]
    [InlineData("log: No image was registered to the reference")]
    [InlineData("log: Script execution failed.")]
    public void ReportsFailure_CatchesEachAbortSignal(string log) =>
        Assert.True(SirilStackEngine.ReportsFailure(log));

    [Fact]
    public void ParseStackedFrameCount_ReadsTheIntegratedCount() =>
        Assert.Equal(
            24,
            SirilStackEngine.ParseStackedFrameCount("log: Stacking 24 images with average", 30));

    [Fact]
    public void ParseStackedFrameCount_FallsBackWhenTheLogSaysNothing() =>
        Assert.Equal(30, SirilStackEngine.ParseStackedFrameCount("log: nothing useful", 30));

    [Fact]
    public async Task StackAsync_ReportsMissingEngineRatherThanThrowing()
    {
        var engine = new SirilStackEngine(new ThrowingProcessRunner())
        {
            ExecutablePath = @"C:\definitely\not\here\siril-cli.exe",
        };

        StackResult result = await engine.StackAsync(new StackRequest(Path.GetTempPath(), "dng"));

        Assert.False(result.Succeeded);
        Assert.Contains("Siril", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ThrowingProcessRunner : AstroDesk.Device.Processes.IProcessRunner
    {
        public Task<AstroDesk.Device.Processes.ProcessExecutionResult> RunAsync(
            AstroDesk.Device.Processes.ProcessInvocation invocation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("must not be started when unavailable");

        public Task<AstroDesk.Device.Processes.IChildProcess> StartAsync(
            AstroDesk.Device.Processes.ProcessInvocation invocation,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("must not be started when unavailable");
    }
}
