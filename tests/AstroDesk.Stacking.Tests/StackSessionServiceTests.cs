using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class StackSessionServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "astrodesk-stack-tests",
        Guid.NewGuid().ToString("N"));

    public StackSessionServiceTests() => Directory.CreateDirectory(_root);

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

    [Fact]
    public void Offer_IgnoresCapturesWhenNotArmed()
    {
        var service = new StackSessionService();
        string capture = CreateCapture("shot.dng");

        Assert.Null(service.Offer(capture, DateTimeOffset.Now));
        Assert.Empty(service.Frames);
    }

    [Fact]
    public void Offer_CopiesRatherThanMovesTheOriginal()
    {
        var service = new StackSessionService();
        service.Arm(_root, targetFrameCount: 0);
        string capture = CreateCapture("shot.dng");

        StackFrame? frame = service.Offer(capture, DateTimeOffset.Now);

        Assert.NotNull(frame);
        // Arming stack mode must never relocate the user's only copy of a shot.
        Assert.True(File.Exists(capture), "the original capture must survive");
        Assert.True(File.Exists(frame!.Path), "the stack copy must exist");
        Assert.NotEqual(capture, frame.Path);
    }

    [Fact]
    public void Offer_DisarmsOnceTheTargetIsReached()
    {
        var service = new StackSessionService();
        service.Arm(_root, targetFrameCount: 2);

        Assert.NotNull(service.Offer(CreateCapture("a.dng"), DateTimeOffset.Now));
        Assert.NotNull(service.Offer(CreateCapture("b.dng"), DateTimeOffset.Now));

        Assert.False(service.IsArmed);

        // A long unattended run must not keep collecting past the requested count.
        Assert.Null(service.Offer(CreateCapture("c.dng"), DateTimeOffset.Now));
        Assert.Equal(2, service.Frames.Count);
    }

    [Fact]
    public void Offer_CollectsIndefinitelyWhenTargetIsZero()
    {
        var service = new StackSessionService();
        service.Arm(_root, targetFrameCount: 0);

        for (int index = 0; index < 5; index++)
        {
            Assert.NotNull(service.Offer(CreateCapture($"f{index}.dng"), DateTimeOffset.Now));
        }

        Assert.True(service.IsArmed);
        Assert.Equal(5, service.Frames.Count);
    }

    [Fact]
    public void Offer_RejectsNonImageFiles()
    {
        var service = new StackSessionService();
        service.Arm(_root, targetFrameCount: 0);

        Assert.Null(service.Offer(CreateCapture("notes.txt"), DateTimeOffset.Now));
        Assert.Empty(service.Frames);
    }

    [Fact]
    public void ResolveFrameExtension_PrefersRawOverJpeg()
    {
        var service = new StackSessionService();
        service.Arm(_root, targetFrameCount: 0);
        service.Offer(CreateCapture("shot.jpg"), DateTimeOffset.Now);
        service.Offer(CreateCapture("shot.dng"), DateTimeOffset.Now);

        // RAW is linear and unsharpened, so averaging it actually recovers
        // signal; a JPEG has already been denoised and 8-bit compressed.
        Assert.Equal("dng", service.ResolveFrameExtension());
    }

    [Fact]
    public void ResolveFrameExtension_FallsBackToJpegWhenNoRaw()
    {
        var service = new StackSessionService();
        service.Arm(_root, targetFrameCount: 0);
        service.Offer(CreateCapture("shot.jpg"), DateTimeOffset.Now);

        Assert.Equal("jpg", service.ResolveFrameExtension());
    }

    [Fact]
    public void ResolveFrameExtension_IsNullWithNoFrames() =>
        Assert.Null(new StackSessionService().ResolveFrameExtension());

    [Fact]
    public void Arm_StartsAFreshRunEachTime()
    {
        var service = new StackSessionService();
        service.Arm(_root, 0);
        service.Offer(CreateCapture("a.dng"), DateTimeOffset.Now);
        Assert.Single(service.Frames);

        string second = service.Arm(_root, 0);

        Assert.Empty(service.Frames);
        Assert.Equal(second, service.SessionFolder);
    }

    [Fact]
    public void FrameAdded_ReportsProgressAgainstTarget()
    {
        var service = new StackSessionService();
        service.Arm(_root, targetFrameCount: 3);
        StackFrameAddedEventArgs? last = null;
        service.FrameAdded += (_, args) => last = args;

        service.Offer(CreateCapture("a.dng"), DateTimeOffset.Now);

        Assert.NotNull(last);
        Assert.Equal(1, last!.Collected);
        Assert.Equal(3, last.Target);
    }

    [Fact]
    public void StackFrame_IdentifiesRaw()
    {
        Assert.True(new StackFrame("a.dng", DateTimeOffset.Now).IsRaw);
        Assert.False(new StackFrame("a.jpg", DateTimeOffset.Now).IsRaw);
    }

    private string CreateCapture(string name)
    {
        string source = Path.Combine(_root, "camera");
        Directory.CreateDirectory(source);
        string path = Path.Combine(source, name);
        File.WriteAllText(path, "test");
        return path;
    }
}
