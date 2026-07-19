using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Covers finding what previous sessions left on disk.
/// </summary>
public sealed class CaptureLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "astrodesk-library",
        Guid.NewGuid().ToString("N"));

    public CaptureLibraryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not fail the suite.
        }
    }

    private string Run(string name, int frames, bool withResult = false)
    {
        string folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        for (int index = 0; index < frames; index++)
        {
            File.WriteAllBytes(Path.Combine(folder, $"frame{index:00}.dng"), new byte[2048]);
        }

        if (withResult)
        {
            File.WriteAllBytes(Path.Combine(folder, "stacked.fit"), new byte[4096]);
        }

        return folder;
    }

    [Fact]
    public void EachStackRunIsListedWithItsFrameCountAndSize()
    {
        Run("stack-20260718-211933", frames: 9);

        CaptureRunSummary run = Assert.Single(CaptureLibrary.Scan(_root));

        Assert.Equal("stack-20260718-211933", run.Name);
        Assert.Equal(9, run.FrameCount);
        Assert.Equal(9 * 2048, run.TotalBytes);
        Assert.NotNull(run.PreviewFramePath);
    }

    [Fact]
    public void AFinishedIntegrationIsFlagged()
    {
        Run("stack-a", frames: 3, withResult: true);
        Run("stack-b", frames: 3);

        IReadOnlyList<CaptureRunSummary> runs = CaptureLibrary.Scan(_root);

        Assert.True(runs.Single(run => run.Name == "stack-a").HasResult);
        Assert.False(runs.Single(run => run.Name == "stack-b").HasResult);
    }

    [Fact]
    public void TheResultsOwnBytesCountTowardsTheRunsSize()
    {
        // A stacked result can be over a hundred megabytes, which is exactly the
        // thing someone clearing space needs to see.
        Run("stack-a", frames: 2, withResult: true);

        CaptureRunSummary run = Assert.Single(CaptureLibrary.Scan(_root));

        Assert.Equal((2 * 2048) + 4096, run.TotalBytes);
    }

    [Fact]
    public void CapturesOutsideAnyRunAreShownRatherThanIgnored()
    {
        // They take the same space whether or not they were ever stacked.
        File.WriteAllBytes(Path.Combine(_root, "20260718_211943.dng"), new byte[1024]);
        Run("stack-a", frames: 2);

        IReadOnlyList<CaptureRunSummary> runs = CaptureLibrary.Scan(_root);

        CaptureRunSummary loose = runs.Single(run => run.IsLooseCaptures);
        Assert.Equal(1, loose.FrameCount);
        Assert.Equal(2, runs.Count);
    }

    [Fact]
    public void FoldersWithoutFramesAreSkipped()
    {
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
        File.WriteAllText(Path.Combine(_root, "notes", "readme.txt"), "not a capture");

        Assert.Empty(CaptureLibrary.Scan(_root));
    }

    [Fact]
    public void RunsAreListedNewestFirst()
    {
        string older = Run("stack-old", frames: 1);
        Thread.Sleep(1100);
        Run("stack-new", frames: 1);

        // Force a clear age gap rather than relying on filesystem timestamp
        // resolution.
        File.SetLastWriteTimeUtc(
            Directory.GetFiles(older)[0],
            DateTime.UtcNow.AddDays(-3));

        IReadOnlyList<CaptureRunSummary> runs = CaptureLibrary.Scan(_root);

        Assert.Equal("stack-new", runs[0].Name);
        Assert.Equal("stack-old", runs[1].Name);
    }

    [Fact]
    public void AMissingRootIsEmptyRatherThanAnError() =>
        Assert.Empty(CaptureLibrary.Scan(Path.Combine(_root, "does-not-exist")));

    [Fact]
    public void TotalAddsUpEveryRun()
    {
        Run("stack-a", frames: 2);
        Run("stack-b", frames: 3);

        Assert.Equal(5 * 2048, CaptureLibrary.TotalBytes(CaptureLibrary.Scan(_root)));
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(28L * 1024 * 1024, "28 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3.0 GB")]
    public void SizesReadTheWaySomeoneClearingSpaceWantsThem(long bytes, string expected) =>
        Assert.Equal(expected, CaptureLibrary.FormatSize(bytes));
}
