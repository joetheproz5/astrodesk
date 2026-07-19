using AstroDesk.Device.Scrcpy;

namespace AstroDesk.Device.Adb;

/// <summary>
/// Works out whether two adb entries are the same physical phone.
/// </summary>
/// <remarks>
/// <para>
/// A phone plugged in over USB while also paired over Wi-Fi appears twice, and
/// adb has no notion that they are one device. The app then reported "multiple
/// devices, select one to continue" and refused to start the preview - correct
/// on its own terms, and useless, because there is only one phone and the user
/// has no reason to think they must choose between two views of it.
/// </para>
/// <para>
/// The mDNS name gives the game away: it is built as
/// adb-SERIAL-random._adb-tls-connect._tcp, so the hardware serial can be
/// recovered from it and matched against the USB entry. A plain host:port
/// pairing carries no serial, so those fall back to the model and product,
/// which is weaker but still catches the common case of one phone reached two
/// ways.
/// </para>
/// </remarks>
public static class PhysicalDeviceIdentity
{
    /// <summary>
    /// A stable key for the physical device behind an adb entry.
    /// </summary>
    public static string KeyFor(AdbDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        string? hardwareSerial = TryGetHardwareSerial(device.Serial);
        if (hardwareSerial is not null)
        {
            return "serial:" + hardwareSerial.ToLowerInvariant();
        }

        // No serial to be had - a host:port pairing. Model and product together
        // are not unique in general, but they are enough to recognise the same
        // handset reached two ways, which is the only case that matters here.
        string descriptor = $"{device.Model}|{device.Product}|{device.DeviceName}";
        return descriptor.Trim('|').Length == 0
            ? "serial:" + device.Serial.ToLowerInvariant()
            : "descriptor:" + descriptor.ToLowerInvariant();
    }

    /// <summary>
    /// The hardware serial behind an adb serial, or null when it carries none.
    /// </summary>
    public static string? TryGetHardwareSerial(string? adbSerial)
    {
        if (string.IsNullOrWhiteSpace(adbSerial))
        {
            return null;
        }

        string serial = adbSerial.Trim();

        // adb-R3XT00SAMPLE-Qx7bZk._adb-tls-connect._tcp
        if (serial.StartsWith("adb-", StringComparison.OrdinalIgnoreCase) &&
            serial.Contains("._", StringComparison.Ordinal))
        {
            string withoutSuffix = serial[4..serial.IndexOf("._", StringComparison.Ordinal)];
            int lastDash = withoutSuffix.LastIndexOf('-');
            return lastDash > 0 ? withoutSuffix[..lastDash] : withoutSuffix;
        }

        // A host:port pairing identifies the endpoint, not the handset.
        return DeviceTransportDetector.Detect(serial) == DeviceTransport.Wireless
            ? null
            : serial;
    }

    /// <summary>
    /// Picks the entry to actually use when several point at the same phone.
    /// </summary>
    /// <remarks>
    /// The cable wins. It is faster, it does not drop when the phone changes
    /// network, and if it is plugged in the user has already expressed a
    /// preference by plugging it in.
    /// </remarks>
    public static AdbDevice PreferredOf(IReadOnlyList<AdbDevice> sameDevice)
    {
        ArgumentNullException.ThrowIfNull(sameDevice);

        return sameDevice.FirstOrDefault(device =>
                   DeviceTransportDetector.Detect(device.Serial) == DeviceTransport.Cable)
               ?? sameDevice[0];
    }

    /// <summary>
    /// Collapses entries that are the same phone, leaving genuinely distinct
    /// devices alone.
    /// </summary>
    public static IReadOnlyList<AdbDevice> Collapse(IReadOnlyList<AdbDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        return
        [
            .. devices
                .GroupBy(KeyFor)
                .Select(group => PreferredOf([.. group])),
        ];
    }
}
