using AstroDesk.Device.Scrcpy;

namespace AstroDesk.Device.Tests;

public sealed class ScrcpyArgumentBuilderTests
{
    [Fact]
    public void Build_IncludesRequiredAndConfiguredArguments()
    {
        var options = new ScrcpyLaunchOptions
        {
            DeviceSerial = "device-serial",
            VideoBitRateMbps = 16,
            MaxSize = 1920,
            MaxFps = 45,
            KeepAwake = true,
            TurnScreenOff = true,
        };

        var arguments = ScrcpyArgumentBuilder.Build(options, "AstroDesk-Test-Window");

        Assert.Equal("--window-title", arguments[0]);
        Assert.Equal("AstroDesk-Test-Window", arguments[1]);
        Assert.Contains("--no-audio", arguments);
        Assert.Contains("--video-bit-rate=16M", arguments);
        Assert.Contains("--max-size=1920", arguments);
        Assert.Contains("--max-fps=45", arguments);
        Assert.Contains("--stay-awake", arguments);
        Assert.Contains("--turn-screen-off", arguments);
        Assert.Equal("--serial", arguments[^2]);
        Assert.Equal("device-serial", arguments[^1]);
    }

    [Fact]
    public void Build_OmitsOptionalArgumentsWhenDisabled()
    {
        var options = new ScrcpyLaunchOptions
        {
            MaxSize = null,
            MaxFps = null,
            KeepAwake = false,
        };

        var arguments = ScrcpyArgumentBuilder.Build(options, "title");

        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--max-size", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.StartsWith("--max-fps", StringComparison.Ordinal));
        Assert.DoesNotContain("--stay-awake", arguments);
        Assert.Contains("--no-audio", arguments);
    }

    [Fact]
    public void CreateUniqueWindowTitle_AlwaysAppendsUniqueSuffix()
    {
        var first = ScrcpyArgumentBuilder.CreateUniqueWindowTitle("AstroDesk-Phone-Preview");
        var second = ScrcpyArgumentBuilder.CreateUniqueWindowTitle("AstroDesk-Phone-Preview");

        Assert.StartsWith("AstroDesk-Phone-Preview-", first, StringComparison.Ordinal);
        Assert.StartsWith("AstroDesk-Phone-Preview-", second, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_RejectsUnsafeBitrates(int bitrate)
    {
        var options = new ScrcpyLaunchOptions { VideoBitRateMbps = bitrate };

        Assert.Throws<ArgumentOutOfRangeException>(() => ScrcpyArgumentBuilder.Validate(options));
    }
}
