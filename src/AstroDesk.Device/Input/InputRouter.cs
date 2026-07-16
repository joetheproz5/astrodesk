using AstroDesk.Device.Adb;

namespace AstroDesk.Device.Input;

public enum InputDeliveryMethod
{
    ScrcpyWindow,
    AdbFallback,
    Failed,
}

public sealed record InputForwardResult(
    bool Succeeded,
    InputDeliveryMethod DeliveryMethod,
    string? Message = null);

public interface IInputRouter
{
    Task<InputForwardResult> SendSpecialKeyAsync(
        nint windowHandle,
        DeviceSpecialKey key,
        string? serial,
        CancellationToken cancellationToken = default);

    Task<InputForwardResult> SendTapAsync(
        nint windowHandle,
        MappedPoint point,
        string? serial,
        CancellationToken cancellationToken = default);

    Task<InputForwardResult> SendSwipeAsync(
        nint windowHandle,
        MappedPoint start,
        MappedPoint end,
        TimeSpan duration,
        string? serial,
        CancellationToken cancellationToken = default);

    Task<InputForwardResult> SendTextAsync(
        nint windowHandle,
        string text,
        string? serial,
        CancellationToken cancellationToken = default);

    Task<InputForwardResult> PasteClipboardAsync(
        nint windowHandle,
        string text,
        string? serial,
        CancellationToken cancellationToken = default);
}

public sealed class InputRouter : IInputRouter
{
    private readonly IInputForwarder _direct;
    private readonly IAdbInputService _adb;

    public InputRouter(IInputForwarder direct, IAdbInputService adb)
    {
        _direct = direct ?? throw new ArgumentNullException(nameof(direct));
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
    }

    public async Task<InputForwardResult> SendSpecialKeyAsync(
        nint windowHandle,
        DeviceSpecialKey key,
        string? serial,
        CancellationToken cancellationToken = default)
    {
        if (windowHandle != nint.Zero && _direct.ForwardSpecialKey(windowHandle, key))
        {
            return DirectSuccess();
        }

        var fallback = SpecialKeyMap.Get(key).AdbFallbackKey;
        if (fallback is null || string.IsNullOrWhiteSpace(serial))
        {
            return Failure("Direct input failed and this action has no safe ADB fallback.");
        }

        await _adb.SendKeyAsync(serial, fallback.Value, cancellationToken).ConfigureAwait(false);
        return AdbSuccess();
    }

    public async Task<InputForwardResult> SendTapAsync(
        nint windowHandle,
        MappedPoint point,
        string? serial,
        CancellationToken cancellationToken = default)
    {
        var directSucceeded = windowHandle != nint.Zero
                              && _direct.ForwardPointer(
                                  windowHandle,
                                  new PointerInput(PointerAction.LeftButtonDown, point))
                              && _direct.ForwardPointer(
                                  windowHandle,
                                  new PointerInput(PointerAction.LeftButtonUp, point));
        if (directSucceeded)
        {
            return DirectSuccess();
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return Failure("Direct tap forwarding failed and no ADB device is selected.");
        }

        await _adb.TapAsync(serial, point.X, point.Y, cancellationToken).ConfigureAwait(false);
        return AdbSuccess();
    }

    public async Task<InputForwardResult> SendSwipeAsync(
        nint windowHandle,
        MappedPoint start,
        MappedPoint end,
        TimeSpan duration,
        string? serial,
        CancellationToken cancellationToken = default)
    {
        var directSucceeded = windowHandle != nint.Zero
                              && _direct.ForwardPointer(
                                  windowHandle,
                                  new PointerInput(PointerAction.LeftButtonDown, start))
                              && _direct.ForwardPointer(
                                  windowHandle,
                                  new PointerInput(
                                      PointerAction.Move,
                                      end,
                                      PointerButtons.Left))
                              && _direct.ForwardPointer(
                                  windowHandle,
                                  new PointerInput(PointerAction.LeftButtonUp, end));
        if (directSucceeded)
        {
            return DirectSuccess();
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return Failure("Direct drag forwarding failed and no ADB device is selected.");
        }

        await _adb.SwipeAsync(
            serial,
            start.X,
            start.Y,
            end.X,
            end.Y,
            duration,
            cancellationToken).ConfigureAwait(false);
        return AdbSuccess();
    }

    public async Task<InputForwardResult> SendTextAsync(
        nint windowHandle,
        string text,
        string? serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (windowHandle != nint.Zero && _direct.ForwardText(windowHandle, text))
        {
            return DirectSuccess();
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return Failure("Direct text forwarding failed and no ADB device is selected.");
        }

        await _adb.SendTextAsync(serial, text, cancellationToken).ConfigureAwait(false);
        return AdbSuccess();
    }

    public async Task<InputForwardResult> PasteClipboardAsync(
        nint windowHandle,
        string text,
        string? serial,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (windowHandle != nint.Zero && _direct.PasteClipboard(windowHandle, text))
        {
            return DirectSuccess();
        }

        if (string.IsNullOrWhiteSpace(serial))
        {
            return Failure("Clipboard forwarding failed and no ADB device is selected.");
        }

        await _adb.SendTextAsync(serial, text, cancellationToken).ConfigureAwait(false);
        return AdbSuccess();
    }

    private static InputForwardResult DirectSuccess() =>
        new(true, InputDeliveryMethod.ScrcpyWindow);

    private static InputForwardResult AdbSuccess() =>
        new(true, InputDeliveryMethod.AdbFallback);

    private static InputForwardResult Failure(string message) =>
        new(false, InputDeliveryMethod.Failed, message);
}
