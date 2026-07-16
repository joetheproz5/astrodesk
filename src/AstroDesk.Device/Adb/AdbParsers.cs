using System.Globalization;
using System.Text.RegularExpressions;

namespace AstroDesk.Device.Adb;

public static partial class AdbDeviceParser
{
    public static AdbDeviceList Parse(string output)
    {
        var devices = new List<AdbDevice>();
        using var reader = new StringReader(output ?? string.Empty);
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (ShouldSkip(line))
            {
                continue;
            }

            var parts = WhitespaceRegex().Split(line);
            if (parts.Length < 2)
            {
                continue;
            }

            var serial = parts[0];
            var state = ParseState(line, parts[1]);
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in parts.Skip(2))
            {
                var separator = part.IndexOf(':');
                if (separator <= 0 || separator == part.Length - 1)
                {
                    continue;
                }

                properties[part[..separator]] = part[(separator + 1)..];
            }

            properties.TryGetValue("model", out var model);
            properties.TryGetValue("product", out var product);
            properties.TryGetValue("device", out var deviceName);
            properties.TryGetValue("transport_id", out var transportId);
            devices.Add(new AdbDevice(serial, state, model, product, deviceName, transportId));
        }

        return new AdbDeviceList(devices);
    }

    private static bool ShouldSkip(string line) =>
        string.IsNullOrWhiteSpace(line)
        || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("* daemon", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("adb server", StringComparison.OrdinalIgnoreCase);

    private static AdbDeviceState ParseState(string line, string stateToken)
    {
        if (line.Contains("no permissions", StringComparison.OrdinalIgnoreCase))
        {
            return AdbDeviceState.NoPermissions;
        }

        return stateToken.ToLowerInvariant() switch
        {
            "device" => AdbDeviceState.Device,
            "unauthorized" => AdbDeviceState.Unauthorized,
            "offline" => AdbDeviceState.Offline,
            "bootloader" => AdbDeviceState.Bootloader,
            "recovery" => AdbDeviceState.Recovery,
            "sideload" => AdbDeviceState.Sideload,
            _ => AdbDeviceState.Unknown,
        };
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}

public static partial class AdbStatusParser
{
    public static IReadOnlyDictionary<string, string> ParseProperties(string output)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(output ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            var match = PropertyRegex().Match(line);
            if (match.Success)
            {
                properties[match.Groups["key"].Value] = match.Groups["value"].Value;
            }
        }

        return properties;
    }

    public static BatteryStatus ParseBattery(string output)
    {
        var values = ParseKeyValueLines(output);
        var percentage = TryInt(values, "level");
        if (percentage is < 0 or > 100)
        {
            percentage = null;
        }

        var rawTemperature = TryDecimal(values, "temperature");
        decimal? temperature = rawTemperature is null
            ? null
            : Math.Abs(rawTemperature.Value) > 100
                ? rawTemperature.Value / 10m
                : rawTemperature.Value;

        var rawVoltage = TryDecimal(values, "voltage");
        decimal? voltage = rawVoltage is null
            ? null
            : Math.Abs(rawVoltage.Value) > 100
                ? rawVoltage.Value / 1000m
                : rawVoltage.Value;

        var statusCode = TryInt(values, "status");
        var powered = IsTrue(values, "AC powered")
                      || IsTrue(values, "USB powered")
                      || IsTrue(values, "Wireless powered");
        var chargingState = statusCode switch
        {
            2 => ChargingState.Charging,
            3 => ChargingState.Discharging,
            4 => ChargingState.NotCharging,
            5 => ChargingState.Full,
            _ when powered => ChargingState.Charging,
            _ => (ChargingState?)null,
        };

        return new BatteryStatus(percentage, chargingState, temperature, voltage);
    }

    public static StorageInfo? ParseStorage(string output)
    {
        StorageInfo? fallback = null;
        using var reader = new StringReader(output ?? string.Empty);
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)
                || line.StartsWith("Filesystem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parts = WhitespaceRegex().Split(line);
            if (parts.Length < 5
                || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalBlocks)
                || !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var availableBlocks))
            {
                continue;
            }

            try
            {
                var storage = new StorageInfo(
                    checked(availableBlocks * 1024),
                    checked(totalBlocks * 1024));
                if (parts[^1].Equals("/data", StringComparison.Ordinal))
                {
                    return storage;
                }

                fallback = storage;
            }
            catch (OverflowException)
            {
            }
        }

        return fallback;
    }

    public static ScreenResolution? ParseResolution(string output)
    {
        ScreenResolution? physical = null;
        ScreenResolution? overridden = null;
        foreach (Match match in ResolutionRegex().Matches(output ?? string.Empty))
        {
            if (!int.TryParse(match.Groups["width"].Value, out var width)
                || !int.TryParse(match.Groups["height"].Value, out var height)
                || width <= 0
                || height <= 0)
            {
                continue;
            }

            var resolution = new ScreenResolution(width, height);
            if (match.Groups["kind"].Value.Equals("Override", StringComparison.OrdinalIgnoreCase))
            {
                overridden = resolution;
            }
            else
            {
                physical = resolution;
            }
        }

        return overridden ?? physical;
    }

    public static DeviceOrientation? ParseOrientation(string output)
    {
        var match = SurfaceOrientationRegex().Match(output ?? string.Empty);
        if (match.Success && int.TryParse(match.Groups["rotation"].Value, out var surfaceRotation))
        {
            return FromRotation(surfaceRotation);
        }

        match = CurrentRotationRegex().Match(output ?? string.Empty);
        if (!match.Success || !int.TryParse(match.Groups["rotation"].Value, out var currentRotation))
        {
            return null;
        }

        return FromRotation(currentRotation);
    }

    public static UsbConnectionState? ParseUsbConnection(IReadOnlyDictionary<string, string> properties)
    {
        if (!properties.TryGetValue("sys.usb.state", out var usbState)
            && !properties.TryGetValue("sys.usb.config", out usbState)
            && !properties.TryGetValue("persist.sys.usb.config", out usbState))
        {
            return null;
        }

        return usbState.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static value => value.Equals("adb", StringComparison.OrdinalIgnoreCase))
            ? UsbConnectionState.Connected
            : UsbConnectionState.Disconnected;
    }

    public static decimal? ParseEstimatedPhoneTemperature(string output)
    {
        var candidates = new List<(int Priority, decimal Value)>();
        using var reader = new StringReader(output ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            var valueMatch = ThermalValueRegex().Match(line);
            var nameMatch = ThermalNameRegex().Match(line);
            if (!valueMatch.Success
                || !nameMatch.Success
                || !decimal.TryParse(
                    valueMatch.Groups["value"].Value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value)
                || value is < -50 or > 200)
            {
                continue;
            }

            var name = nameMatch.Groups["name"].Value.Trim().ToLowerInvariant();
            var priority = name switch
            {
                var item when item.Contains("skin", StringComparison.Ordinal) => 0,
                var item when item.Contains("soc", StringComparison.Ordinal) => 1,
                var item when item.Contains("cpu", StringComparison.Ordinal) => 2,
                var item when item.Contains("battery", StringComparison.Ordinal) => 3,
                _ => 10,
            };
            candidates.Add((priority, value));
        }

        return candidates.OrderBy(static item => item.Priority).Select(static item => (decimal?)item.Value).FirstOrDefault();
    }

    private static Dictionary<string, string> ParseKeyValueLines(string output)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = new StringReader(output ?? string.Empty);
        while (reader.ReadLine() is { } line)
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static int? TryInt(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static decimal? TryDecimal(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
        && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;

    private static bool IsTrue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value)
        && bool.TryParse(value, out var result)
        && result;

    private static DeviceOrientation? FromRotation(int rotation) =>
        rotation switch
        {
            0 or 360 => DeviceOrientation.Portrait,
            1 or 90 => DeviceOrientation.Landscape,
            2 or 180 => DeviceOrientation.ReversePortrait,
            3 or 270 => DeviceOrientation.ReverseLandscape,
            _ => null,
        };

    [GeneratedRegex(@"^\[(?<key>[^\]]+)\]:\s*\[(?<value>.*)\]\s*$")]
    private static partial Regex PropertyRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"(?<kind>Physical|Override)\s+size:\s*(?<width>\d+)x(?<height>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ResolutionRegex();

    [GeneratedRegex(@"SurfaceOrientation:\s*(?<rotation>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SurfaceOrientationRegex();

    [GeneratedRegex(@"(?:mCurrentRotation|rotation)\s*=\s*(?:ROTATION_)?(?<rotation>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CurrentRotationRegex();

    [GeneratedRegex(@"mValue=(?<value>-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex ThermalValueRegex();

    [GeneratedRegex(@"mName=(?<name>[^,}]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ThermalNameRegex();
}
