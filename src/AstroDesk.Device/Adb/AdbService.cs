using AstroDesk.Device.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace AstroDesk.Device.Adb;

public interface IAdbCommandExecutor
{
    Task<ProcessExecutionResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public interface IAdbDeviceClient
{
    Task<AdbDeviceList> GetDevicesAsync(CancellationToken cancellationToken = default);

    Task<AdbConnectionSnapshot> GetConnectionAsync(
        string? preferredSerial = null,
        CancellationToken cancellationToken = default);

    Task<PhoneStatus> GetPhoneStatusAsync(
        string serial,
        CancellationToken cancellationToken = default);

    Task ReconnectAsync(CancellationToken cancellationToken = default);
}

public interface IAdbWirelessClient
{
    Task<string> GetWifiAddressAsync(
        string serial,
        CancellationToken cancellationToken = default);

    Task EnableTcpIpAsync(
        string serial,
        int port = 5555,
        CancellationToken cancellationToken = default);

    Task PairWirelessAsync(
        string endpoint,
        string pairingCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdbMdnsService>> GetMdnsServicesAsync(
        CancellationToken cancellationToken = default);

    Task ConnectWirelessAsync(
        string endpoint,
        CancellationToken cancellationToken = default);

    Task DisconnectWirelessAsync(
        string endpoint,
        CancellationToken cancellationToken = default);
}

public sealed class AdbService : IAdbCommandExecutor, IAdbDeviceClient, IAdbWirelessClient
{
    private readonly IProcessRunner _processRunner;
    private readonly IExecutableLocator _executableLocator;
    private readonly DeviceToolOptions _options;
    private readonly ILogger<AdbService> _logger;

    public AdbService(
        IProcessRunner processRunner,
        IExecutableLocator executableLocator,
        DeviceToolOptions options,
        ILogger<AdbService>? logger = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<AdbService>.Instance;
    }

    public async Task<ProcessExecutionResult> ExecuteAsync(
        string? serial,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var adbPath = _executableLocator.Resolve(
            new ExecutableRequest("adb.exe", _options.AdbExecutablePath, "ASTRODESK_ADB_PATH"));

        var finalArguments = new List<string>(arguments.Count + (string.IsNullOrWhiteSpace(serial) ? 0 : 2));
        if (!string.IsNullOrWhiteSpace(serial))
        {
            finalArguments.Add("-s");
            finalArguments.Add(serial);
        }

        finalArguments.AddRange(arguments);
        var sensitiveValues = string.IsNullOrWhiteSpace(serial)
            ? Array.Empty<string>()
            : new[] { serial };

        return await _processRunner.RunAsync(
            new ProcessInvocation(
                adbPath,
                finalArguments,
                SensitiveValues: sensitiveValues,
                CreateNoWindow: true),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdbDeviceList> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(null, ["devices", "-l"], cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "devices");
        return AdbDeviceParser.Parse(result.StandardOutput);
    }

    public async Task<AdbConnectionSnapshot> GetConnectionAsync(
        string? preferredSerial = null,
        CancellationToken cancellationToken = default)
    {
        var list = await GetDevicesAsync(cancellationToken).ConfigureAwait(false);
        var devices = list.Devices;

        if (!string.IsNullOrWhiteSpace(preferredSerial))
        {
            var preferred = devices.FirstOrDefault(
                device => device.Serial.Equals(preferredSerial, StringComparison.Ordinal));
            if (preferred is not null)
            {
                return preferred.State switch
                {
                    AdbDeviceState.Device => new AdbConnectionSnapshot(
                        AdbConnectionState.Connected,
                        devices,
                        preferred,
                        $"{preferred.DisplayName} connected."),
                    AdbDeviceState.Unauthorized => new AdbConnectionSnapshot(
                        AdbConnectionState.Unauthorized,
                        devices,
                        preferred,
                        AdbDeviceList.AuthorizationInstructions),
                    AdbDeviceState.Offline => new AdbConnectionSnapshot(
                        AdbConnectionState.Offline,
                        devices,
                        preferred,
                        "The selected Android device is offline. Reconnect USB and retry."),
                    AdbDeviceState.NoPermissions => new AdbConnectionSnapshot(
                        AdbConnectionState.NoPermissions,
                        devices,
                        preferred,
                        "Windows cannot access the selected Android device. Check its USB driver and permissions."),
                    _ => new AdbConnectionSnapshot(
                        AdbConnectionState.Unknown,
                        devices,
                        preferred,
                        "The selected Android device is in an unsupported ADB state."),
                };
            }
        }

        var connected = list.ConnectedDevices;
        if (connected.Count == 1)
        {
            return new AdbConnectionSnapshot(
                AdbConnectionState.Connected,
                devices,
                connected[0],
                $"{connected[0].DisplayName} connected.");
        }

        if (connected.Count > 1)
        {
            return new AdbConnectionSnapshot(
                AdbConnectionState.MultipleDevices,
                devices,
                null,
                "Multiple authorized Android devices are connected. Select one to continue.");
        }

        if (devices.Any(static device => device.State == AdbDeviceState.Unauthorized))
        {
            return new AdbConnectionSnapshot(
                AdbConnectionState.Unauthorized,
                devices,
                null,
                AdbDeviceList.AuthorizationInstructions);
        }

        if (devices.Any(static device => device.State == AdbDeviceState.Offline))
        {
            return new AdbConnectionSnapshot(
                AdbConnectionState.Offline,
                devices,
                null,
                "An Android device is visible but offline. Reconnect USB and retry.");
        }

        if (devices.Any(static device => device.State == AdbDeviceState.NoPermissions))
        {
            return new AdbConnectionSnapshot(
                AdbConnectionState.NoPermissions,
                devices,
                null,
                "Windows cannot access the Android device. Check the USB driver and permissions.");
        }

        return new AdbConnectionSnapshot(
            AdbConnectionState.Disconnected,
            devices,
            null,
            "No authorized Android device is connected.");
    }

    public async Task<PhoneStatus> GetPhoneStatusAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);

        var propertiesOutput = await RunStatusCommandAsync(
            serial,
            ["shell", "getprop"],
            cancellationToken).ConfigureAwait(false);
        var batteryOutput = await RunStatusCommandAsync(
            serial,
            ["shell", "dumpsys", "battery"],
            cancellationToken).ConfigureAwait(false);
        var storageOutput = await RunStatusCommandAsync(
            serial,
            ["shell", "df", "-k", "/data"],
            cancellationToken).ConfigureAwait(false);
        var resolutionOutput = await RunStatusCommandAsync(
            serial,
            ["shell", "wm", "size"],
            cancellationToken).ConfigureAwait(false);
        var orientationOutput = await RunStatusCommandAsync(
            serial,
            ["shell", "dumpsys", "input"],
            cancellationToken).ConfigureAwait(false);
        var thermalOutput = await RunStatusCommandAsync(
            serial,
            ["shell", "dumpsys", "thermalservice"],
            cancellationToken).ConfigureAwait(false);

        var properties = AdbStatusParser.ParseProperties(propertiesOutput);
        var battery = AdbStatusParser.ParseBattery(batteryOutput);
        properties.TryGetValue("ro.product.model", out var model);
        properties.TryGetValue("ro.build.version.release", out var androidVersion);

        return new PhoneStatus(
            EmptyToNull(model),
            EmptyToNull(androidVersion),
            battery.Percentage,
            battery.ChargingState,
            battery.TemperatureCelsius,
            battery.VoltageVolts,
            AdbStatusParser.ParseEstimatedPhoneTemperature(thermalOutput),
            AdbStatusParser.ParseStorage(storageOutput),
            AdbStatusParser.ParseResolution(resolutionOutput),
            AdbStatusParser.ParseOrientation(orientationOutput),
            AdbStatusParser.ParseUsbConnection(properties),
            DateTimeOffset.UtcNow);
    }

    public async Task<string> GetPropertyAsync(
        string serial,
        string propertyName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (propertyName.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("ADB property names cannot contain whitespace.", nameof(propertyName));
        }

        var result = await ExecuteAsync(
            serial,
            ["shell", "getprop", propertyName],
            cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "getprop", [serial]);
        return result.StandardOutput.Trim();
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        var result = await ExecuteAsync(null, ["reconnect"], cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "reconnect");
    }

    public async Task ConnectWirelessAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var result = await ExecuteAsync(null, ["connect", endpoint.Trim()], cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "connect");
    }

    public async Task DisconnectWirelessAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        var result = await ExecuteAsync(null, ["disconnect", endpoint.Trim()], cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "disconnect");
    }

    public async Task<string> GetWifiAddressAsync(
        string serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        var result = await ExecuteAsync(
            serial,
            ["shell", "ip", "-f", "inet", "addr", "show", "wlan0"],
            cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "wifi-address", [serial]);

        Match match = Regex.Match(
            result.StandardOutput,
            @"\binet\s+(?<address>(?:\d{1,3}\.){3}\d{1,3})/",
            RegexOptions.CultureInvariant);
        if (match.Success &&
            IPAddress.TryParse(match.Groups["address"].Value, out IPAddress? address) &&
            address.AddressFamily == AddressFamily.InterNetwork &&
            !IPAddress.IsLoopback(address))
        {
            return address.ToString();
        }

        throw new InvalidOperationException(
            "Could not find the phone's Wi-Fi address. Connect the laptop and phone to the same Wi-Fi network, then try again.");
    }

    public async Task EnableTcpIpAsync(
        string serial,
        int port = 5555,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serial);
        if (port is < 1024 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), "The wireless ADB port must be between 1024 and 65535.");
        }

        var result = await ExecuteAsync(
            serial,
            ["tcpip", port.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "tcpip", [serial]);
    }

    public async Task PairWirelessAsync(
        string endpoint,
        string pairingCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(pairingCode);

        string safeEndpoint = endpoint.Trim();
        string secret = pairingCode.Trim();
        ProcessExecutionResult result = await ExecuteGlobalAsync(
            ["pair", safeEndpoint, secret],
            [safeEndpoint, secret],
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded &&
            (result.StandardError.Contains("protocol fault", StringComparison.OrdinalIgnoreCase) ||
             result.StandardOutput.Contains("protocol fault", StringComparison.OrdinalIgnoreCase)))
        {
            throw new AdbCommandException(
                "pair",
                result.ExitCode,
                "The phone did not answer at the temporary pairing address. Keep the 'Pair device with pairing code' screen open, use the current IP and port shown there, and make sure both devices are on the same Wi-Fi without a VPN or guest-network isolation.");
        }

        EnsureSucceeded(result, "pair", [safeEndpoint, secret]);
    }

    public async Task<IReadOnlyList<AdbMdnsService>> GetMdnsServicesAsync(
        CancellationToken cancellationToken = default)
    {
        ProcessExecutionResult result = await ExecuteAsync(
            null,
            ["mdns", "services"],
            cancellationToken).ConfigureAwait(false);
        EnsureSucceeded(result, "mdns services");
        return AdbMdnsParser.Parse(result.StandardOutput);
    }

    public async Task<string> ResolveConnectedSerialAsync(
        string? preferredSerial = null,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(preferredSerial, cancellationToken).ConfigureAwait(false);
        if (connection.IsConnected)
        {
            return connection.SelectedDevice!.Serial;
        }

        if (connection.State == AdbConnectionState.MultipleDevices)
        {
            throw new MultipleAdbDevicesException();
        }

        throw new AdbDeviceUnavailableException(connection.Message);
    }

    private async Task<string> RunStatusCommandAsync(
        string serial,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(serial, arguments, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            return result.StandardOutput;
        }

        _logger.LogDebug(
            "Optional ADB status command {Command} returned exit code {ExitCode}.",
            arguments.Count > 1 ? arguments[1] : arguments[0],
            result.ExitCode);
        return string.Empty;
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<ProcessExecutionResult> ExecuteGlobalAsync(
        IReadOnlyList<string> arguments,
        IReadOnlyCollection<string> sensitiveValues,
        CancellationToken cancellationToken)
    {
        var adbPath = _executableLocator.Resolve(
            new ExecutableRequest("adb.exe", _options.AdbExecutablePath, "ASTRODESK_ADB_PATH"));
        return await _processRunner.RunAsync(
            new ProcessInvocation(
                adbPath,
                arguments,
                SensitiveValues: sensitiveValues,
                CreateNoWindow: true),
            cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureSucceeded(
        ProcessExecutionResult result,
        string command,
        IReadOnlyCollection<string>? sensitiveValues = null)
    {
        if (!result.Succeeded)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? "No diagnostic output was returned."
                : SensitiveDataRedactor.Redact(result.StandardError.Trim(), sensitiveValues);
            throw new AdbCommandException(command, result.ExitCode, error);
        }
    }
}
