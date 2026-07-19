using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Covers a stack run being repeatable on the same folder.
/// </summary>
/// <remarks>
/// Siril's <c>convert</c> takes every convertible file in the current directory,
/// and FITS is convertible. Converting in place therefore meant a second run
/// converted the first run's output as well: ten frames became twenty, every
/// exposure counted twice, and the result was quietly wrong rather than failing.
/// Verified against Siril 1.4.4 - three consecutive runs over the same folder
/// now report ten converted and ten stacked every time.
/// </remarks>
public sealed class RepeatedStackScriptTests
{
    private static string Script(string? darks = null) =>
        SirilScriptBuilder.Build(
            new StackRequest("C:\\run", "dng", DarksDirectory: darks),
            frameCount: 10);

    [Fact]
    public void FramesAreConvertedIntoTheirOwnFolder() =>
        Assert.Contains(
            $"convert {SirilScriptBuilder.ConvertName} -out={SirilScriptBuilder.ProcessFolderName}",
            Script(),
            StringComparison.Ordinal);

    [Fact]
    public void NothingIsConvertedIntoTheCaptureFolder() =>
        // "-out=." is what put the products beside the captures and made a rerun
        // eat its own output.
        Assert.DoesNotContain("-out=.\n", Script().Replace("\r", string.Empty), StringComparison.Ordinal);

    [Fact]
    public void TheResultStaysInsideTheProcessFolder()
    {
        // Writing it beside the captures looks tidier and is wrong: the next run
        // converts the stack and its preview along with the frames. Measured at
        // twelve converted images where there were ten.
        string script = Script();

        Assert.DoesNotContain("-out=../", script, StringComparison.Ordinal);
        Assert.Contains("-out=stacked", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDarksAlsoConvertOutOfTheirOwnFolder() =>
        Assert.Contains(
            $"convert {SirilScriptBuilder.DarkConvertName} -out=../{SirilScriptBuilder.DarkProcessFolderName}",
            Script("C:\\run\\darks"),
            StringComparison.Ordinal);

    [Fact]
    public void WorkHappensInsideTheProcessFolder()
    {
        string script = Script();

        int cd = script.IndexOf($"cd {SirilScriptBuilder.ProcessFolderName}", StringComparison.Ordinal);
        int register = script.IndexOf("register", StringComparison.Ordinal);

        Assert.True(cd > 0 && register > cd, "registration must run where the converted frames are");
    }
}
