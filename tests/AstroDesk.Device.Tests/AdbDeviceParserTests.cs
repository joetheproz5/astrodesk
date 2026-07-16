using AstroDesk.Device.Adb;

namespace AstroDesk.Device.Tests;

public sealed class AdbDeviceParserTests
{
    [Fact]
    public void Parse_RecognizesReadyUnauthorizedOfflineAndPermissionsStates()
    {
        const string output = """
            * daemon not running; starting now at tcp:5037
            * daemon started successfully
            List of devices attached
            R5CT123456A    device product:dm3qxxx model:SM-S918B device:dm3q transport_id:1
            emulator-5554  device product:sdk model:Android_SDK_built_for_x86 device:emu transport_id:2
            R5CTUNAUTH     unauthorized usb:1-2 transport_id:3
            R5CTOFFLINE    offline transport_id:4
            R5CTNOPERM     no permissions (missing udev rules)
            """;

        var result = AdbDeviceParser.Parse(output);

        Assert.Equal(5, result.Devices.Count);
        Assert.True(result.HasMultipleConnectedDevices);
        Assert.True(result.RequiresAuthorization);

        var phone = Assert.Single(result.Devices, device => device.Serial == "R5CT123456A");
        Assert.Equal(AdbDeviceState.Device, phone.State);
        Assert.Equal("SM-S918B", phone.Model);
        Assert.Equal("dm3qxxx", phone.Product);
        Assert.Equal("1", phone.TransportId);

        Assert.Equal(
            AdbDeviceState.Unauthorized,
            Assert.Single(result.Devices, device => device.Serial == "R5CTUNAUTH").State);
        Assert.Equal(
            AdbDeviceState.Offline,
            Assert.Single(result.Devices, device => device.Serial == "R5CTOFFLINE").State);
        Assert.Equal(
            AdbDeviceState.NoPermissions,
            Assert.Single(result.Devices, device => device.Serial == "R5CTNOPERM").State);
    }

    [Fact]
    public void Parse_EmptyOutputReturnsEmptyList()
    {
        var result = AdbDeviceParser.Parse(string.Empty);

        Assert.Empty(result.Devices);
        Assert.False(result.HasMultipleConnectedDevices);
        Assert.False(result.RequiresAuthorization);
    }
}
