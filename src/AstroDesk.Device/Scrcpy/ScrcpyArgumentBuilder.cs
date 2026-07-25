using System.Globalization;

namespace AstroDesk.Device.Scrcpy;

public static class ScrcpyArgumentBuilder
{
    public static string CreateUniqueWindowTitle(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("A scrcpy window title prefix is required.", nameof(prefix));
        }

        return $"{prefix.Trim()}-{Guid.NewGuid():N}";
    }

    public static IReadOnlyList<string> Build(ScrcpyLaunchOptions options, string windowTitle)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowTitle);
        Validate(options);

        var arguments = new List<string>
        {
            "--window-title",
            windowTitle,
            "--window-x=-32000",
            "--window-y=-32000",
            "--capture-orientation=@270",
            "--no-audio",
            $"--video-bit-rate={options.VideoBitRateMbps.ToString(CultureInfo.InvariantCulture)}M",
        };

        if (options.MaxSize is { } maxSize)
        {
            arguments.Add($"--max-size={maxSize.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.MaxFps is { } maxFps)
        {
            arguments.Add($"--max-fps={maxFps.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.KeepAwake)
        {
            arguments.Add("--stay-awake");
        }

        if (options.TurnScreenOff)
        {
            arguments.Add("--turn-screen-off");
        }

        if (!string.IsNullOrWhiteSpace(options.DeviceSerial))
        {
            arguments.Add("--serial");
            arguments.Add(options.DeviceSerial);
        }

        return arguments;
    }

    public static void Validate(ScrcpyLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.VideoBitRateMbps is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "scrcpy video bitrate must be between 1 and 100 Mbps.");
        }

        if (options.MaxSize is <= 0 or > 16384)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "scrcpy maximum size must be between 1 and 16384 pixels.");
        }

        if (options.MaxFps is <= 0 or > 240)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "scrcpy maximum FPS must be between 1 and 240.");
        }

        if (string.IsNullOrWhiteSpace(options.WindowTitlePrefix))
        {
            throw new ArgumentException("A scrcpy window title prefix is required.", nameof(options));
        }

        if (options.WindowDiscoveryTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Window discovery timeout must be greater than zero.");
        }
    }
}
