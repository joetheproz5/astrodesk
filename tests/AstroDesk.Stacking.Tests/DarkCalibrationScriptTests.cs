using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Covers the Siril script that subtracts a master dark from the lights.
/// </summary>
/// <remarks>
/// Verified against Siril 1.4.4 with synthetic frames carrying four deliberate
/// hot pixels. Before calibration those pixels read 255; afterwards they read 0,
/// while the background was untouched. The assertions here pin the script shape
/// that produced that, because it is the shape that only ever gets exercised on
/// a night that cannot be repeated.
/// </remarks>
public sealed class DarkCalibrationScriptTests
{
    private static StackRequest Request(string? darks, string extension = "dng") =>
        new("C:\\run", extension, DarksDirectory: darks);

    private static string Script(string? darks, string extension = "dng") =>
        SirilScriptBuilder.Build(Request(darks, extension), frameCount: 12);

    [Fact]
    public void WithoutDarksTheScriptIsUnchanged()
    {
        string script = Script(darks: null);

        Assert.DoesNotContain("calibrate", script, StringComparison.Ordinal);
        Assert.DoesNotContain("master_dark", script, StringComparison.Ordinal);
        Assert.Contains($"register {SirilScriptBuilder.SequenceName}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMasterDarkIsBuiltInsideTheDarksFolder()
    {
        // Converting the darks alongside the lights would be simpler and wrong:
        // Siril's convert takes every convertible file in the directory, so they
        // would be swept into the light sequence and averaged in as if they were
        // exposures of the sky.
        string script = Script("C:\\run\\darks");

        Assert.Contains("cd darks", script, StringComparison.Ordinal);
        Assert.Contains("convert dark -out=.", script, StringComparison.Ordinal);
        Assert.Contains("stack dark_ rej 3 3 -nonorm -out=master_dark", script, StringComparison.Ordinal);
        Assert.Contains("cd ..", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMasterDarkIsStackedWithoutNormalisation() =>
        // Normalising darks would scale away the very offsets the calibration
        // exists to measure.
        Assert.Contains("-nonorm", Script("C:\\run\\darks"), StringComparison.Ordinal);

    [Fact]
    public void CalibrationRunsBeforeRegistration()
    {
        string script = Script("C:\\run\\darks");

        int calibrate = script.IndexOf("calibrate", StringComparison.Ordinal);
        int register = script.IndexOf("register", StringComparison.Ordinal);

        Assert.True(calibrate > 0 && register > calibrate,
            "a dark must be subtracted before frames are resampled onto a common grid");
    }

    [Fact]
    public void RegistrationAndStackingFollowTheCalibratedPrefix()
    {
        // Siril writes calibrated frames as pp_, so everything downstream shifts.
        // Getting this wrong fails with "sequence not found" after the slow part
        // of the run has already happened.
        string script = Script("C:\\run\\darks");

        Assert.Contains($"register pp_{SirilScriptBuilder.SequenceName}", script, StringComparison.Ordinal);
        Assert.Contains($"stack r_pp_{SirilScriptBuilder.SequenceName}", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RawFramesAreNotDebayeredTwice()
    {
        // The dark has to come off the raw mosaic, before interpolation mixes
        // neighbouring photosites. So convert must not debayer when calibrating;
        // calibrate takes it over.
        string script = Script("C:\\run\\darks", "dng");

        string convertLine = script
            .Split('\n')
            .Single(line => line.StartsWith("convert astrodesk", StringComparison.Ordinal));

        Assert.DoesNotContain("-debayer", convertLine, StringComparison.Ordinal);
        Assert.Contains("-cfa -equalize_cfa -debayer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void RawFramesAreStillDebayeredWhenThereAreNoDarks()
    {
        string script = Script(darks: null, extension: "dng");

        Assert.Contains("convert astrodesk -out=. -debayer", script, StringComparison.Ordinal);
    }

    [Fact]
    public void AlreadyColourFramesAreNotTreatedAsAMosaic()
    {
        // A JPEG has been debayered by the phone already; telling Siril it is CFA
        // would corrupt it.
        string script = Script("C:\\run\\darks", "jpg");

        Assert.Contains("calibrate", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-cfa", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyDarksPathMeansNoCalibration(string? darks) =>
        Assert.False(SirilScriptBuilder.HasDarks(Request(darks)));
}
