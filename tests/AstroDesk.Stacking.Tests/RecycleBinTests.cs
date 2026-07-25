using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Covers deleting a capture run being recoverable, and confined.
/// </summary>
/// <remarks>
/// Deleting used to be permanent, which is the wrong default for a button whose
/// purpose is freeing space: one mis-click on the wrong row destroys frames that
/// cannot be retaken, because the sky has moved on.
/// </remarks>
public sealed class RecycleBinTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "astrodesk-recycle",
        Guid.NewGuid().ToString("N"));

    public RecycleBinTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ARunInsideTheCaptureFolderMayBeDeleted() =>
        Assert.True(RecycleBin.IsSafeToDelete(Path.Combine(_root, "stack-a"), _root));

    [Fact]
    public void TheCaptureFolderItselfIsRefused() =>
        // The catch that matters: this holds every original, and the shell would
        // happily remove it if asked.
        Assert.False(RecycleBin.IsSafeToDelete(_root, _root));

    [Fact]
    public void TheCaptureFolderIsRefusedRegardlessOfTrailingSeparator() =>
        Assert.False(RecycleBin.IsSafeToDelete(_root + Path.DirectorySeparatorChar, _root));

    [Fact]
    public void SomewhereElseEntirelyIsRefused() =>
        Assert.False(RecycleBin.IsSafeToDelete(@"C:\Windows\System32", _root));

    [Fact]
    public void AParentOfTheCaptureFolderIsRefused() =>
        Assert.False(RecycleBin.IsSafeToDelete(Path.GetDirectoryName(_root)!, _root));

    [Fact]
    public void EscapingWithDotDotIsRefused() =>
        // The path is resolved before comparing, so a traversal cannot slip past.
        Assert.False(RecycleBin.IsSafeToDelete(
            Path.Combine(_root, "stack-a", "..", "..", "elsewhere"),
            _root));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingIsDeletedWithoutAPath(string? path) =>
        Assert.False(RecycleBin.IsSafeToDelete(path, "C:\\captures"));

    [Fact]
    public void AMissingFolderReportsFailureRatherThanThrowing() =>
        Assert.False(RecycleBin.TrySendFolder(Path.Combine(_root, "never-existed")));

    [Fact]
    public void AFolderIsRemovedFromDiskAndRecoverable()
    {
        // The Recycle Bin itself is the shell's business; what this checks is
        // that the call succeeds and the folder leaves the capture directory.
        string folder = Path.Combine(_root, "stack-recycle-me");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "frame.dng"), new byte[1024]);

        bool sent = RecycleBin.TrySendFolder(folder);

        Assert.True(sent, "the shell declined to recycle the folder");
        Assert.False(Directory.Exists(folder));
    }
}
