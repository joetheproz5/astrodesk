using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class CameraAppTests
{
    [Fact]
    public void ExpertRaw_IsTheDefault()
    {
        // Expert RAW is the app that offers long exposures and writes DNG, which
        // is what a stacking run actually wants.
        Assert.Equal(CameraApp.ExpertRaw, CameraApp.Default);
        Assert.Equal("com.samsung.android.app.galaxyraw", CameraApp.Default.Package);
    }

    [Fact]
    public void EachAppWritesToItsOwnFolder()
    {
        // Verified on an S23 Ultra: DCIM held Camera, Expert RAW and
        // DeepSkyCamera as separate directories. Pairing the wrong folder with
        // an app yields a run that captures successfully and imports nothing.
        Assert.Equal("/sdcard/DCIM/Expert RAW", CameraApp.ExpertRaw.RemoteFolder);
        Assert.Equal("/sdcard/DCIM/Camera", CameraApp.StockCamera.RemoteFolder);
        Assert.Equal("/sdcard/DCIM/DeepSkyCamera", CameraApp.DeepSkyCamera.RemoteFolder);

        string[] folders = [.. CameraApp.All.Select(app => app.RemoteFolder)];
        Assert.Equal(folders.Length, folders.Distinct().Count());
    }

    [Fact]
    public void FolderWithASpaceIsQuotedForTheDeviceShell()
    {
        // adb joins shell arguments into one command line that the device shell
        // re-splits, so "Expert RAW" arrives as two arguments. Verified on
        // device: unquoted listing failed, single-quoted listing succeeded.
        Assert.Equal("'/sdcard/DCIM/Expert RAW'", CameraApp.ExpertRaw.ShellQuotedFolder);
        Assert.Contains(' ', CameraApp.ExpertRaw.RemoteFolder);
    }

    [Fact]
    public void EveryFolderIsQuotedWhetherOrNotItNeedsIt() =>
        Assert.All(
            CameraApp.All,
            app => Assert.Equal($"'{app.RemoteFolder}'", app.ShellQuotedFolder));

    [Theory]
    [InlineData("Expert RAW")]
    [InlineData("expert raw")]
    public void FromName_MatchesCaseInsensitively(string name) =>
        Assert.Equal(CameraApp.ExpertRaw, CameraApp.FromName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Some Other Camera")]
    public void FromName_FallsBackToTheDefault(string? name) =>
        Assert.Equal(CameraApp.Default, CameraApp.FromName(name));

    [Fact]
    public void PackagesAreDistinct()
    {
        string[] packages = [.. CameraApp.All.Select(app => app.Package)];
        Assert.Equal(packages.Length, packages.Distinct().Count());
    }
}
