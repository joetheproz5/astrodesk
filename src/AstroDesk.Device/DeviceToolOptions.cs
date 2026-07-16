namespace AstroDesk.Device;

/// <summary>
/// User-configurable paths for the external Android tools.
/// </summary>
public sealed class DeviceToolOptions
{
    public string? AdbExecutablePath { get; set; }

    public string? ScrcpyExecutablePath { get; set; }
}
