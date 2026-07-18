using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class SirilScriptBuilderTests
{
    [Fact]
    public void Build_RegistersBeforeStacking()
    {
        string script = SirilScriptBuilder.Build(
            new StackRequest("C:\\frames", "dng"),
            frameCount: 30);

        int register = script.IndexOf("register", StringComparison.Ordinal);
        int stack = script.IndexOf("stack ", StringComparison.Ordinal);

        Assert.True(register >= 0, "the script must register the sequence");
        Assert.True(stack >= 0, "the script must stack the sequence");

        // Stacking unregistered frames from a fixed tripod produces star trails,
        // so the ordering here is the whole point of the script.
        Assert.True(register < stack, "registration must happen before stacking");
    }

    [Fact]
    public void Build_StacksTheRegisteredSequenceNotTheRawOne()
    {
        string script = SirilScriptBuilder.Build(
            new StackRequest("C:\\frames", "dng"),
            frameCount: 30);

        // Siril writes registered frames with an r_ prefix. Stacking the
        // unprefixed sequence silently integrates unaligned frames.
        Assert.Contains($"stack r_{SirilScriptBuilder.SequenceName}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DebayersRawButNotJpeg()
    {
        string raw = SirilScriptBuilder.Build(new StackRequest("C:\\f", "dng"), 20);
        string jpeg = SirilScriptBuilder.Build(new StackRequest("C:\\f", "jpg"), 20);

        // A DNG is a Bayer mosaic and must be interpolated to colour; a JPEG is
        // already demosaiced and debayering it again would corrupt it.
        Assert.Contains("-debayer", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("-debayer", jpeg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dng", true)]
    [InlineData(".DNG", true)]
    [InlineData("cr2", true)]
    [InlineData("jpg", false)]
    [InlineData("png", false)]
    public void IsRawExtension_ClassifiesCorrectly(string extension, bool expected) =>
        Assert.Equal(expected, SirilScriptBuilder.IsRawExtension(extension));

    [Fact]
    public void ResolveMethod_FallsBackToAverageWhenTooFewFramesForRejection()
    {
        // Sigma clipping estimates a per-pixel distribution. With a handful of
        // frames it has no sample to speak of and rejects real signal.
        Assert.Equal(
            StackMethod.Average,
            SirilScriptBuilder.ResolveMethod(StackMethod.AverageSigmaClip, frameCount: 3));

        Assert.Equal(
            StackMethod.AverageSigmaClip,
            SirilScriptBuilder.ResolveMethod(StackMethod.AverageSigmaClip, frameCount: 40));
    }

    [Fact]
    public void Build_UsesRejectionOnlyWhenEnoughFrames()
    {
        string few = SirilScriptBuilder.Build(new StackRequest("C:\\f", "dng"), frameCount: 3);
        string many = SirilScriptBuilder.Build(new StackRequest("C:\\f", "dng"), frameCount: 40);

        Assert.Contains("mean", few, StringComparison.Ordinal);
        Assert.Contains("rej", many, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WritesSigmaThresholdsInvariantly()
    {
        // A comma decimal separator from a European locale would make Siril
        // parse the rejection thresholds as separate arguments.
        string script = SirilScriptBuilder.Build(
            new StackRequest("C:\\f", "dng", SigmaLow: 2.5, SigmaHigh: 3.5),
            frameCount: 40);

        Assert.Contains("2.5", script, StringComparison.Ordinal);
        Assert.Contains("3.5", script, StringComparison.Ordinal);
        Assert.DoesNotContain("2,5", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DeclaresRequiredVersion() =>
        Assert.StartsWith(
            $"requires {SirilScriptBuilder.RequiredVersion}",
            SirilScriptBuilder.Build(new StackRequest("C:\\f", "dng"), 20),
            StringComparison.Ordinal);
}
