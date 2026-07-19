using AstroDesk.Device.Adb;
using Xunit;

namespace AstroDesk.Device.Tests;

/// <summary>
/// Covers recognising one phone reached two ways.
/// </summary>
/// <remarks>
/// A handset plugged in over USB while also paired over Wi-Fi appears twice, and
/// the app used to refuse to start the preview until one was chosen. That is a
/// choice with no meaning - there is one phone - and it blocked a session for a
/// reason the UI never explained.
/// </remarks>
public sealed class PhysicalDeviceIdentityTests
{
    private static AdbDevice Device(string serial, string model = "SCG20", string product = "SCG20_jp_kdi") =>
        new(serial, AdbDeviceState.Device, model, product, "SCG20");

    // The real pair seen on the bench.
    private const string UsbSerial = "R3XT00SAMPLE";
    private const string MdnsSerial = "adb-R3XT00SAMPLE-Qx7bZk._adb-tls-connect._tcp";

    [Fact]
    public void TheHardwareSerialIsRecoveredFromAnMdnsName() =>
        Assert.Equal(UsbSerial, PhysicalDeviceIdentity.TryGetHardwareSerial(MdnsSerial));

    [Fact]
    public void APlainSerialIsItsOwnHardwareSerial() =>
        Assert.Equal(UsbSerial, PhysicalDeviceIdentity.TryGetHardwareSerial(UsbSerial));

    [Fact]
    public void AHostAndPortPairingCarriesNoSerial() =>
        // The endpoint identifies the network address, not the handset, so
        // claiming a serial from it would merge unrelated phones.
        Assert.Null(PhysicalDeviceIdentity.TryGetHardwareSerial("192.168.1.42:5555"));

    [Fact]
    public void UsbAndMdnsEntriesForOnePhoneCollapseToOne()
    {
        IReadOnlyList<AdbDevice> collapsed =
            PhysicalDeviceIdentity.Collapse([Device(UsbSerial), Device(MdnsSerial)]);

        AdbDevice single = Assert.Single(collapsed);
        Assert.Equal(UsbSerial, single.Serial);
    }

    [Fact]
    public void TheCableEntryIsTheOneKept()
    {
        // Faster, and it does not drop when the phone changes network. Order of
        // discovery must not decide it.
        Assert.Equal(
            UsbSerial,
            PhysicalDeviceIdentity.Collapse([Device(MdnsSerial), Device(UsbSerial)])[0].Serial);
    }

    [Fact]
    public void GenuinelyDifferentPhonesAreLeftAlone()
    {
        // The case the warning exists for has to keep working.
        IReadOnlyList<AdbDevice> collapsed = PhysicalDeviceIdentity.Collapse(
        [
            Device(UsbSerial),
            Device("R3CT90XYZ12", "SM_G991B", "o1sxeea"),
        ]);

        Assert.Equal(2, collapsed.Count);
    }

    [Fact]
    public void AWirelessOnlyPhonePairedByAddressIsMatchedOnItsDescriptor()
    {
        // No serial is available from host:port, so model and product carry it.
        // Weaker, but it still catches one handset reached two ways.
        IReadOnlyList<AdbDevice> collapsed = PhysicalDeviceIdentity.Collapse(
        [
            Device(UsbSerial),
            Device("192.168.1.42:5555"),
        ]);

        // Different keys - the address entry cannot prove it is the same phone,
        // so it is not merged. Being cautious here is right: merging two phones
        // by accident would send captures to the wrong device.
        Assert.Equal(2, collapsed.Count);
    }

    [Fact]
    public void TwoAddressPairingsOfTheSameModelCollapse()
    {
        IReadOnlyList<AdbDevice> collapsed = PhysicalDeviceIdentity.Collapse(
        [
            Device("192.168.1.42:5555"),
            Device("192.168.1.42:37000"),
        ]);

        Assert.Single(collapsed);
    }

    [Fact]
    public void CollapsingAnEmptyListIsEmpty() =>
        Assert.Empty(PhysicalDeviceIdentity.Collapse([]));

    [Fact]
    public void TheSerialComparisonIgnoresCase() =>
        Assert.Single(PhysicalDeviceIdentity.Collapse(
        [
            Device(UsbSerial),
            Device(UsbSerial.ToLowerInvariant()),
        ]));
}
