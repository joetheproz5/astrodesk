namespace AstroDesk.Device.Scrcpy;

public enum DeviceTransport
{
    /// <summary>No device, or a serial that cannot be classified.</summary>
    Unknown,

    /// <summary>Plugged in over USB.</summary>
    Cable,

    /// <summary>Reached over TCP/IP, either by address or by mDNS pairing.</summary>
    Wireless,
}

/// <summary>
/// Picks the scrcpy stream quality that the link can actually carry.
/// </summary>
/// <remarks>
/// <para>
/// A cable has bandwidth to spare and no contention, so it can carry a stream
/// that would stutter badly over Wi-Fi. Running one conservative profile
/// everywhere means the cable case is throttled to what the wireless case can
/// survive, which is most of the resolution left on the table exactly when the
/// user is at the tripod with the phone plugged in.
/// </para>
/// <para>
/// Resolution is the setting that matters here. Framing and focusing on stars is
/// a detail problem, and the preview is downscaled into a window barely 1100 px
/// wide, so a sharper source visibly improves what lands there and matters much
/// more once the preview is fullscreen or zoomed. Raising the frame rate mostly
/// does not help: the phone's own camera preview drops well below 60 fps in the
/// dark, so the extra frames are duplicates.
/// </para>
/// </remarks>
public sealed record ScrcpyQualityProfile(int MaxSize, int MaxFps, int VideoBitRateMbps)
{
    /// <summary>
    /// Full-rate profile for USB.
    /// </summary>
    /// <remarks>
    /// 2560 leaves an S23 Ultra running at FHD+ (1080x2340) completely
    /// unscaled and only mildly downscales QHD+ (1440x3088). 40 Mbps is well
    /// inside what USB 2.0 carries in practice and stops the encoder smearing
    /// faint stars into blocking artefacts, which is the failure mode that
    /// actually costs you a focus check.
    /// </remarks>
    public static ScrcpyQualityProfile Cable { get; } = new(2560, 90, 40);

    /// <summary>
    /// Conservative profile for Wi-Fi, where the link is shared and lossy.
    /// </summary>
    public static ScrcpyQualityProfile Wireless { get; } = new(1920, 60, 12);

    public static ScrcpyQualityProfile For(DeviceTransport transport) =>
        transport == DeviceTransport.Cable ? Cable : Wireless;
}

/// <summary>
/// Works out how a device is attached from its ADB serial.
/// </summary>
/// <remarks>
/// ADB does not report the transport directly, but the serial gives it away.
/// A network device is either "host:port" or an mDNS name ending in
/// "._tcp"; anything else is the hardware serial of a device on USB.
/// </remarks>
public static class DeviceTransportDetector
{
    public static DeviceTransport Detect(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return DeviceTransport.Unknown;
        }

        string trimmed = serial.Trim();

        // mDNS pairing, e.g. adb-R5CT10ABCDE-Xy9Zab._adb-tls-connect._tcp
        if (trimmed.EndsWith("._tcp", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("_adb-tls-connect", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("_adb._tcp", StringComparison.OrdinalIgnoreCase))
        {
            return DeviceTransport.Wireless;
        }

        // host:port, e.g. 192.168.1.42:5555. Guarding on a numeric port keeps a
        // hardware serial that merely contains a colon from being misread.
        int separator = trimmed.LastIndexOf(':');
        if (separator > 0 &&
            separator < trimmed.Length - 1 &&
            int.TryParse(trimmed[(separator + 1)..], out int port) &&
            port is > 0 and <= 65535)
        {
            return DeviceTransport.Wireless;
        }

        return DeviceTransport.Cable;
    }
}
