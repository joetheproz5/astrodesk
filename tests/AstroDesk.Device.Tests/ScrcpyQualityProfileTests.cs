using AstroDesk.Device.Scrcpy;
using Xunit;

namespace AstroDesk.Device.Tests;

/// <summary>
/// Covers picking the stream quality from how the phone is attached.
/// </summary>
public sealed class ScrcpyQualityProfileTests
{
    [Theory]
    [InlineData("R5CT10ABCDE")]          // S23 Ultra hardware serial
    [InlineData("1234567890ABCDEF")]
    [InlineData("emulator5554")]
    public void APlainSerialIsACable(string serial) =>
        Assert.Equal(DeviceTransport.Cable, DeviceTransportDetector.Detect(serial));

    [Theory]
    [InlineData("192.168.1.42:5555")]
    [InlineData("192.168.0.7:37000")]
    [InlineData("adb-R5CT10ABCDE-Xy9Zab._adb-tls-connect._tcp")]
    [InlineData("adb-R5CT10ABCDE-Xy9Zab._adb._tcp")]
    public void ANetworkSerialIsWireless(string serial) =>
        Assert.Equal(DeviceTransport.Wireless, DeviceTransportDetector.Detect(serial));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoSerialIsUnknown(string? serial) =>
        Assert.Equal(DeviceTransport.Unknown, DeviceTransportDetector.Detect(serial));

    [Fact]
    public void ASerialContainingAColonButNoPortIsStillACable()
    {
        // Guards the host:port rule against eating a hardware serial that merely
        // happens to contain a colon; only a numeric port makes it a network one.
        Assert.Equal(DeviceTransport.Cable, DeviceTransportDetector.Detect("R5CT:ABCDE"));
        Assert.Equal(DeviceTransport.Cable, DeviceTransportDetector.Detect("device:"));
    }

    [Fact]
    public void APortOutsideTheValidRangeIsNotTreatedAsWireless() =>
        Assert.Equal(DeviceTransport.Cable, DeviceTransportDetector.Detect("serial:99999"));

    [Fact]
    public void TheCableProfileIsRicherThanTheWirelessOne()
    {
        // The whole point: one profile everywhere throttles the cable down to
        // what Wi-Fi can survive. Resolution is where the visible gain is.
        Assert.True(ScrcpyQualityProfile.Cable.MaxSize > ScrcpyQualityProfile.Wireless.MaxSize);
        Assert.True(
            ScrcpyQualityProfile.Cable.VideoBitRateMbps >
            ScrcpyQualityProfile.Wireless.VideoBitRateMbps);
    }

    [Fact]
    public void TheCableProfileStaysWithinWhatAUsbLinkSurvived()
    {
        // Regression guard. At 90 fps and 40 Mbps a real S23 Ultra reset the USB
        // link within seconds of any on-screen movement - adbd logged "UsbFfs:
        // connection terminated: Connection reset by peer" and the scrcpy server
        // died with it - so the preview dropped whenever it was touched. The
        // frame rate buys nothing here anyway: the phone's camera preview runs
        // well below 60 in the dark, so the extra frames are duplicates.
        Assert.True(
            ScrcpyQualityProfile.Cable.MaxFps <= 60,
            $"frame rate {ScrcpyQualityProfile.Cable.MaxFps} destabilised the link");
        Assert.True(
            ScrcpyQualityProfile.Cable.VideoBitRateMbps <= 24,
            $"bitrate {ScrcpyQualityProfile.Cable.VideoBitRateMbps} Mbps destabilised the link");
    }

    [Fact]
    public void AnUnknownTransportGetsTheConservativeProfile()
    {
        // Guessing high on a link that turns out to be Wi-Fi produces a stuttering
        // preview, which is worse than guessing low.
        Assert.Equal(ScrcpyQualityProfile.Wireless, ScrcpyQualityProfile.For(DeviceTransport.Unknown));
        Assert.Equal(ScrcpyQualityProfile.Wireless, ScrcpyQualityProfile.For(DeviceTransport.Wireless));
        Assert.Equal(ScrcpyQualityProfile.Cable, ScrcpyQualityProfile.For(DeviceTransport.Cable));
    }
}
