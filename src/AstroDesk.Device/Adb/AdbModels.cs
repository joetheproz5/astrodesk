namespace AstroDesk.Device.Adb;

public enum AdbDeviceState
{
    Device,
    Unauthorized,
    Offline,
    Bootloader,
    Recovery,
    Sideload,
    NoPermissions,
    Unknown,
}

public sealed record AdbDevice(
    string Serial,
    AdbDeviceState State,
    string? Model = null,
    string? Product = null,
    string? DeviceName = null,
    string? TransportId = null)
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(Model)
            ? "Android device"
            : Model.Replace('_', ' ');
}

public sealed record AdbDeviceList(IReadOnlyList<AdbDevice> Devices)
{
    public IReadOnlyList<AdbDevice> ConnectedDevices =>
        Devices.Where(static device => device.State == AdbDeviceState.Device).ToArray();

    public bool HasMultipleConnectedDevices => ConnectedDevices.Count > 1;

    public bool RequiresAuthorization => Devices.Any(static device => device.State == AdbDeviceState.Unauthorized);

    public const string AuthorizationInstructions =
        "Unlock the phone, accept the USB debugging authorization prompt, optionally select 'Always allow', then reconnect.";
}

public enum AdbConnectionState
{
    Disconnected,
    Connected,
    Unauthorized,
    Offline,
    MultipleDevices,
    NoPermissions,
    Unknown,
}

public sealed record AdbConnectionSnapshot(
    AdbConnectionState State,
    IReadOnlyList<AdbDevice> Devices,
    AdbDevice? SelectedDevice,
    string Message)
{
    public bool IsConnected => State == AdbConnectionState.Connected && SelectedDevice is not null;
}

public enum ChargingState
{
    Charging,
    Discharging,
    NotCharging,
    Full,
}

public enum DeviceOrientation
{
    Portrait,
    Landscape,
    ReversePortrait,
    ReverseLandscape,
}

public enum UsbConnectionState
{
    Connected,
    Disconnected,
}

public readonly record struct StorageInfo(long FreeBytes, long TotalBytes)
{
    public double FreePercentage => TotalBytes <= 0 ? 0 : (double)FreeBytes / TotalBytes * 100;
}

public readonly record struct ScreenResolution(int Width, int Height);

public sealed record BatteryStatus(
    int? Percentage,
    ChargingState? ChargingState,
    decimal? TemperatureCelsius,
    decimal? VoltageVolts);

public sealed record PhoneStatus(
    string? Model,
    string? AndroidVersion,
    int? BatteryPercentage,
    ChargingState? ChargingState,
    decimal? BatteryTemperatureCelsius,
    decimal? BatteryVoltageVolts,
    decimal? EstimatedPhoneTemperatureCelsius,
    StorageInfo? InternalStorage,
    ScreenResolution? ScreenResolution,
    DeviceOrientation? Orientation,
    UsbConnectionState? UsbConnection,
    DateTimeOffset RetrievedAt)
{
    public const string UnavailableText = "Unavailable";
}

public static class PhoneStatusDisplay
{
    public static string OrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? PhoneStatus.UnavailableText : value;

    public static string OrUnavailable<T>(T? value, string? format = null)
        where T : struct, IFormattable =>
        value is null
            ? PhoneStatus.UnavailableText
            : value.Value.ToString(format, System.Globalization.CultureInfo.CurrentCulture);
}

public sealed class AdbCommandException : InvalidOperationException
{
    public AdbCommandException(string command, int exitCode, string safeError)
        : base($"ADB command '{command}' failed with exit code {exitCode}: {safeError}")
    {
        Command = command;
        ExitCode = exitCode;
    }

    public string Command { get; }

    public int ExitCode { get; }
}

public sealed class MultipleAdbDevicesException : InvalidOperationException
{
    public MultipleAdbDevicesException()
        : base("Multiple authorized Android devices are connected. Select a device before continuing.")
    {
    }
}

public sealed class AdbDeviceUnavailableException : InvalidOperationException
{
    public AdbDeviceUnavailableException(string message)
        : base(message)
    {
    }
}
