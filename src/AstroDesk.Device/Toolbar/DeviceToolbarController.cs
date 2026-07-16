using AstroDesk.Device.Adb;
using AstroDesk.Device.Input;
using AstroDesk.Device.Scrcpy;

namespace AstroDesk.Device.Toolbar;

public enum DeviceToolbarAction
{
    StartScrcpy,
    StopScrcpy,
    Reconnect,
    RotatePhone,
    FullscreenPreview,
    Back,
    Home,
    RecentApps,
    VolumeUp,
    VolumeDown,
    Power,
    Screenshot,
    ClipboardPaste,
    ScreenOffWhileMirroring,
    KeepAwake,
}

public sealed record DeviceToolbarCapabilities
{
    public bool SupportsScreenOffWhileMirroring { get; init; }

    public bool SupportsClipboardPaste { get; init; } = true;

    public bool SupportsKeepAwake { get; init; } = true;
}

public sealed record DeviceToolbarActionDescriptor(
    DeviceToolbarAction Action,
    string Label,
    bool IsSupported,
    bool IsHandledByHost = false,
    string? UnsupportedReason = null);

public sealed record ToolbarActionContext(
    ScrcpyLaunchOptions? LaunchOptions = null,
    string? Serial = null,
    string? ClipboardText = null,
    nint? WindowHandle = null);

public sealed record ToolbarActionResult(
    bool Succeeded,
    bool HostActionRequired = false,
    string? Message = null);

public interface IDeviceToolbarController
{
    IReadOnlyList<DeviceToolbarActionDescriptor> GetActions();

    Task<ToolbarActionResult> ExecuteAsync(
        DeviceToolbarAction action,
        ToolbarActionContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Routes device-owned toolbar actions while leaving preview fullscreen and screenshot
/// actions to the WPF/capture host.
/// </summary>
public sealed class DeviceToolbarController : IDeviceToolbarController
{
    private readonly IScrcpyService _scrcpy;
    private readonly IAdbDeviceClient _adbDeviceClient;
    private readonly IAdbInputService _adbInput;
    private readonly IInputRouter _input;
    private readonly DeviceToolbarCapabilities _capabilities;

    public DeviceToolbarController(
        IScrcpyService scrcpy,
        IAdbDeviceClient adbDeviceClient,
        IAdbInputService adbInput,
        IInputRouter input,
        DeviceToolbarCapabilities? capabilities = null)
    {
        _scrcpy = scrcpy ?? throw new ArgumentNullException(nameof(scrcpy));
        _adbDeviceClient = adbDeviceClient ?? throw new ArgumentNullException(nameof(adbDeviceClient));
        _adbInput = adbInput ?? throw new ArgumentNullException(nameof(adbInput));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _capabilities = capabilities ?? new DeviceToolbarCapabilities();
    }

    public IReadOnlyList<DeviceToolbarActionDescriptor> GetActions() =>
        [
            Supported(DeviceToolbarAction.StartScrcpy, "Start scrcpy"),
            Supported(DeviceToolbarAction.StopScrcpy, "Stop scrcpy"),
            Supported(DeviceToolbarAction.Reconnect, "Reconnect"),
            Supported(DeviceToolbarAction.RotatePhone, "Rotate phone"),
            HostHandled(DeviceToolbarAction.FullscreenPreview, "Fullscreen preview"),
            Supported(DeviceToolbarAction.Back, "Back"),
            Supported(DeviceToolbarAction.Home, "Home"),
            Supported(DeviceToolbarAction.RecentApps, "Recent apps"),
            Supported(DeviceToolbarAction.VolumeUp, "Volume up"),
            Supported(DeviceToolbarAction.VolumeDown, "Volume down"),
            Supported(DeviceToolbarAction.Power, "Power"),
            HostHandled(DeviceToolbarAction.Screenshot, "Screenshot"),
            Capability(
                DeviceToolbarAction.ClipboardPaste,
                "Clipboard paste",
                _capabilities.SupportsClipboardPaste,
                "Clipboard paste is not supported by the active scrcpy configuration."),
            Capability(
                DeviceToolbarAction.ScreenOffWhileMirroring,
                "Phone screen off",
                _capabilities.SupportsScreenOffWhileMirroring,
                "The installed scrcpy version has not been confirmed to support screen-off control."),
            Capability(
                DeviceToolbarAction.KeepAwake,
                "Keep awake",
                _capabilities.SupportsKeepAwake,
                "Keep-awake control is unavailable."),
        ];

    public async Task<ToolbarActionResult> ExecuteAsync(
        DeviceToolbarAction action,
        ToolbarActionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var descriptor = GetActions().Single(item => item.Action == action);
        if (!descriptor.IsSupported)
        {
            return new ToolbarActionResult(false, Message: descriptor.UnsupportedReason);
        }

        if (descriptor.IsHandledByHost)
        {
            return new ToolbarActionResult(
                true,
                HostActionRequired: true,
                Message: "The preview/capture host must complete this action.");
        }

        switch (action)
        {
            case DeviceToolbarAction.StartScrcpy:
                if (context.LaunchOptions is null)
                {
                    return new ToolbarActionResult(false, Message: "scrcpy launch options are required.");
                }

                _ = await _scrcpy.StartAsync(context.LaunchOptions, cancellationToken).ConfigureAwait(false);
                return Success();

            case DeviceToolbarAction.StopScrcpy:
                await _scrcpy.StopAsync(cancellationToken).ConfigureAwait(false);
                return Success();

            case DeviceToolbarAction.Reconnect:
                await _adbDeviceClient.ReconnectAsync(cancellationToken).ConfigureAwait(false);
                _ = await _scrcpy.ReconnectAsync(cancellationToken).ConfigureAwait(false);
                return Success();

            case DeviceToolbarAction.ClipboardPaste:
                if (context.ClipboardText is null)
                {
                    return new ToolbarActionResult(false, Message: "Clipboard text is required.");
                }

                return FromInputResult(
                    await _input.PasteClipboardAsync(
                        ResolveWindowHandle(context),
                        context.ClipboardText,
                        context.Serial,
                        cancellationToken).ConfigureAwait(false));

            case DeviceToolbarAction.KeepAwake:
                if (string.IsNullOrWhiteSpace(context.Serial))
                {
                    return new ToolbarActionResult(false, Message: "Select an ADB device first.");
                }

                await _adbInput.SetKeepAwakeAsync(context.Serial, true, cancellationToken).ConfigureAwait(false);
                return Success();

            case DeviceToolbarAction.RotatePhone:
            case DeviceToolbarAction.Back:
            case DeviceToolbarAction.Home:
            case DeviceToolbarAction.RecentApps:
            case DeviceToolbarAction.VolumeUp:
            case DeviceToolbarAction.VolumeDown:
            case DeviceToolbarAction.Power:
            case DeviceToolbarAction.ScreenOffWhileMirroring:
                var specialKey = ToSpecialKey(action);
                return FromInputResult(
                    await _input.SendSpecialKeyAsync(
                        ResolveWindowHandle(context),
                        specialKey,
                        context.Serial,
                        cancellationToken).ConfigureAwait(false));

            default:
                return new ToolbarActionResult(false, Message: "This toolbar action is not handled by the device layer.");
        }
    }

    private nint ResolveWindowHandle(ToolbarActionContext context) =>
        context.WindowHandle ?? _scrcpy.CurrentSession?.WindowHandle ?? nint.Zero;

    private static DeviceSpecialKey ToSpecialKey(DeviceToolbarAction action) =>
        action switch
        {
            DeviceToolbarAction.RotatePhone => DeviceSpecialKey.RotatePhone,
            DeviceToolbarAction.Back => DeviceSpecialKey.Back,
            DeviceToolbarAction.Home => DeviceSpecialKey.Home,
            DeviceToolbarAction.RecentApps => DeviceSpecialKey.RecentApps,
            DeviceToolbarAction.VolumeUp => DeviceSpecialKey.VolumeUp,
            DeviceToolbarAction.VolumeDown => DeviceSpecialKey.VolumeDown,
            DeviceToolbarAction.Power => DeviceSpecialKey.Power,
            DeviceToolbarAction.ScreenOffWhileMirroring => DeviceSpecialKey.ScreenOffWhileMirroring,
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

    private static ToolbarActionResult FromInputResult(InputForwardResult result) =>
        new(result.Succeeded, Message: result.Message);

    private static ToolbarActionResult Success() => new(true);

    private static DeviceToolbarActionDescriptor Supported(DeviceToolbarAction action, string label) =>
        new(action, label, true);

    private static DeviceToolbarActionDescriptor HostHandled(DeviceToolbarAction action, string label) =>
        new(action, label, true, IsHandledByHost: true);

    private static DeviceToolbarActionDescriptor Capability(
        DeviceToolbarAction action,
        string label,
        bool supported,
        string unsupportedReason) =>
        new(action, label, supported, UnsupportedReason: supported ? null : unsupportedReason);
}
