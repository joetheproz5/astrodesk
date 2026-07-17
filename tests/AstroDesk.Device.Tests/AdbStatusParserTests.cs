using AstroDesk.Device.Adb;

namespace AstroDesk.Device.Tests;

public sealed class AdbStatusParserTests
{
    [Fact]
    public void MdnsParser_ReadsPairingAndConnectServices()
    {
        const string output = """
            List of discovered mdns services
            adb-ABC123-QXjCrW  _adb-tls-pairing._tcp  192.168.1.101:34941
            studio-A1b2C3d4E5  _adb-tls-pairing._tcp  192.168.1.101:40117
            adb-ABC123-TnSdi9  _adb-tls-connect._tcp  192.168.1.101:37777
            """;

        IReadOnlyList<AdbMdnsService> services = AdbMdnsParser.Parse(output);

        Assert.Equal(3, services.Count);
        Assert.Equal("studio-A1b2C3d4E5", services[1].InstanceName);
        Assert.Equal("_adb-tls-pairing._tcp", services[1].ServiceType);
        Assert.Equal("192.168.1.101:40117", services[1].Endpoint);
    }

    [Fact]
    public void Parsers_ReadAvailablePhoneStatusValues()
    {
        const string propertiesOutput = """
            [ro.product.model]: [SM-S918B]
            [ro.build.version.release]: [14]
            [sys.usb.state]: [mtp,adb]
            """;
        const string batteryOutput = """
              AC powered: false
              USB powered: true
              Wireless powered: false
              status: 2
              level: 73
              voltage: 4217
              temperature: 326
            """;
        const string storageOutput = """
            Filesystem       1K-blocks      Used Available Use% Mounted on
            /dev/block/dm-10 244109824 45120000 198989824 19% /data
            """;
        const string resolutionOutput = """
            Physical size: 1440x3088
            Override size: 1080x2316
            """;
        const string orientationOutput = "SurfaceOrientation: 1";
        const string thermalOutput = """
            Temperature{mValue=38.5, mType=3, mName=CPU0, mStatus=0}
            Temperature{mValue=34.2, mType=3, mName=skin, mStatus=0}
            """;

        var properties = AdbStatusParser.ParseProperties(propertiesOutput);
        var battery = AdbStatusParser.ParseBattery(batteryOutput);
        var storage = AdbStatusParser.ParseStorage(storageOutput);
        var resolution = AdbStatusParser.ParseResolution(resolutionOutput);
        var orientation = AdbStatusParser.ParseOrientation(orientationOutput);
        var usb = AdbStatusParser.ParseUsbConnection(properties);
        var temperature = AdbStatusParser.ParseEstimatedPhoneTemperature(thermalOutput);

        Assert.Equal("SM-S918B", properties["ro.product.model"]);
        Assert.Equal(73, battery.Percentage);
        Assert.Equal(ChargingState.Charging, battery.ChargingState);
        Assert.Equal(32.6m, battery.TemperatureCelsius);
        Assert.Equal(4.217m, battery.VoltageVolts);
        Assert.Equal(new StorageInfo(198989824L * 1024, 244109824L * 1024), storage);
        Assert.Equal(new ScreenResolution(1080, 2316), resolution);
        Assert.Equal(DeviceOrientation.Landscape, orientation);
        Assert.Equal(UsbConnectionState.Connected, usb);
        Assert.Equal(34.2m, temperature);
    }

    [Fact]
    public void Parsers_ReturnNullForUnavailableValues()
    {
        Assert.Null(AdbStatusParser.ParseStorage("permission denied"));
        Assert.Null(AdbStatusParser.ParseResolution(""));
        Assert.Null(AdbStatusParser.ParseOrientation("unknown"));
        Assert.Null(AdbStatusParser.ParseUsbConnection(new Dictionary<string, string>()));
        Assert.Null(AdbStatusParser.ParseEstimatedPhoneTemperature(""));

        var battery = AdbStatusParser.ParseBattery("");
        Assert.Null(battery.Percentage);
        Assert.Null(battery.ChargingState);
        Assert.Null(battery.TemperatureCelsius);
        Assert.Null(battery.VoltageVolts);
        Assert.Equal("Unavailable", PhoneStatusDisplay.OrUnavailable((string?)null));
    }

    [Theory]
    [InlineData("SurfaceOrientation: 0", DeviceOrientation.Portrait)]
    [InlineData("SurfaceOrientation: 1", DeviceOrientation.Landscape)]
    [InlineData("SurfaceOrientation: 2", DeviceOrientation.ReversePortrait)]
    [InlineData("SurfaceOrientation: 3", DeviceOrientation.ReverseLandscape)]
    [InlineData("mCurrentRotation=ROTATION_90", DeviceOrientation.Landscape)]
    public void ParseOrientation_HandlesAndroidRotationFormats(string output, DeviceOrientation expected)
    {
        Assert.Equal(expected, AdbStatusParser.ParseOrientation(output));
    }
}
