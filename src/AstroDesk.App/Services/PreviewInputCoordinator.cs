using System.Windows;
using System.Windows.Input;
using AstroDesk.App.Controls;
using AstroDesk.Capture.Geometry;
using AstroDesk.Device.Adb;
using AstroDesk.Device.Input;
using AstroDesk.Device.Scrcpy;

namespace AstroDesk.App.Services;

public interface IPreviewInputCoordinator
{
    Task HandleAsync(
        PreviewInputEventArgs input,
        CoordinateMappingContext context,
        CancellationToken cancellationToken = default);
}

public sealed class PreviewInputCoordinator(
    IInputForwarder directInput,
    IInputRouter inputRouter,
    IAdbInputService adbInput,
    IScrcpyService scrcpy,
    IDeviceMonitor deviceMonitor,
    CoordinateMapper coordinateMapper) : IPreviewInputCoordinator
{
    private readonly IInputForwarder _directInput = directInput;
    private readonly IInputRouter _inputRouter = inputRouter;
    private readonly IAdbInputService _adbInput = adbInput;
    private readonly IScrcpyService _scrcpy = scrcpy;
    private readonly IDeviceMonitor _deviceMonitor = deviceMonitor;
    private readonly CoordinateMapper _coordinateMapper = coordinateMapper;
    private MappedPoint? _dragStart;
    private DateTimeOffset _dragStartedAt;
    private bool _leftDown;
    private bool _rightDown;
    private bool _directFailed;
    private MappedPoint? _touchStart;
    private MappedPoint? _touchLast;
    private DateTimeOffset _touchStartedAt;

    public async Task HandleAsync(
        PreviewInputEventArgs input,
        CoordinateMappingContext context,
        CancellationToken cancellationToken = default)
    {
        nint window = _scrcpy.CurrentSession?.WindowHandle ?? nint.Zero;
        string? serial = _deviceMonitor.LastSnapshot?.Connection.SelectedDevice?.Serial;
        CoordinateMappingResult mapping = _coordinateMapper.MapToScrcpy(
            new PointD(input.Position.X, input.Position.Y),
            context);

        if (!mapping.IsInsidePreview)
        {
            if (input.Kind == PreviewInputKind.TouchUp)
            {
                await FinishTouchAsync(serial, _touchLast, context, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (input.Kind is not (PreviewInputKind.KeyDown or PreviewInputKind.TextInput))
            {
                return;
            }
        }

        MappedPoint point = new(
            (int)Math.Round(mapping.ScrcpyClientPointPixels.X),
            (int)Math.Round(mapping.ScrcpyClientPointPixels.Y));

        switch (input.Kind)
        {
            case PreviewInputKind.MouseDown:
                await HandleMouseDownAsync(window, input.MouseButton, point).ConfigureAwait(false);
                break;
            case PreviewInputKind.MouseMove:
                HandleMouseMove(window, point);
                break;
            case PreviewInputKind.MouseUp:
                await HandleMouseUpAsync(
                        window,
                        serial,
                        input.MouseButton,
                        point,
                        context,
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
            case PreviewInputKind.TouchDown:
                _touchStart = point;
                _touchLast = point;
                _touchStartedAt = DateTimeOffset.UtcNow;
                break;
            case PreviewInputKind.TouchMove:
                if (_touchStart is not null)
                {
                    _touchLast = point;
                }

                break;
            case PreviewInputKind.TouchUp:
                await FinishTouchAsync(serial, point, context, cancellationToken).ConfigureAwait(false);
                break;
            case PreviewInputKind.MouseWheel:
                _ = _directInput.ForwardPointer(
                    window,
                    new PointerInput(
                        PointerAction.Wheel,
                        point,
                        CurrentButtons(),
                        CurrentModifiers(),
                        input.WheelDelta));
                break;
            case PreviewInputKind.KeyDown:
                await HandleKeyAsync(window, serial, input.Key, cancellationToken).ConfigureAwait(false);
                break;
            case PreviewInputKind.TextInput:
                if (!string.IsNullOrEmpty(input.Text))
                {
                    _ = await _inputRouter
                        .SendTextAsync(window, input.Text, serial, cancellationToken)
                        .ConfigureAwait(false);
                }

                break;
        }
    }

    private async Task FinishTouchAsync(
        string? serial,
        MappedPoint? end,
        CoordinateMappingContext context,
        CancellationToken cancellationToken)
    {
        MappedPoint? start = _touchStart;
        _touchStart = null;
        _touchLast = null;
        if (string.IsNullOrWhiteSpace(serial) || start is null || end is null)
        {
            return;
        }

        MappedPoint adbStart = ScaleForAdb(start.Value, context);
        MappedPoint adbEnd = ScaleForAdb(end.Value, context);
        double distance = Math.Sqrt(
            Math.Pow(end.Value.X - start.Value.X, 2) +
            Math.Pow(end.Value.Y - start.Value.Y, 2));
        if (distance < 5)
        {
            await _adbInput.TapAsync(serial, adbEnd.X, adbEnd.Y, cancellationToken).ConfigureAwait(false);
            return;
        }

        double durationMilliseconds = Math.Clamp(
            (DateTimeOffset.UtcNow - _touchStartedAt).TotalMilliseconds,
            0,
            TimeSpan.FromMinutes(1).TotalMilliseconds);
        TimeSpan duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        await _adbInput.SwipeAsync(
            serial,
            adbStart.X,
            adbStart.Y,
            adbEnd.X,
            adbEnd.Y,
            duration,
            cancellationToken).ConfigureAwait(false);
    }

    private Task HandleMouseDownAsync(nint window, MouseButton? button, MappedPoint point)
    {
        PointerAction action = button switch
        {
            MouseButton.Left => PointerAction.LeftButtonDown,
            MouseButton.Right => PointerAction.RightButtonDown,
            MouseButton.Middle => PointerAction.MiddleButtonDown,
            _ => PointerAction.LeftButtonDown,
        };

        if (button == MouseButton.Left)
        {
            _leftDown = true;
            _dragStart = point;
            _dragStartedAt = DateTimeOffset.UtcNow;
            _directFailed = false;
        }
        else if (button == MouseButton.Right)
        {
            _rightDown = true;
        }

        bool direct = _directInput.ForwardPointer(
            window,
            new PointerInput(action, point, CurrentButtons(), CurrentModifiers()));
        _directFailed |= !direct;
        return Task.CompletedTask;
    }

    private void HandleMouseMove(nint window, MappedPoint point)
    {
        if (!_leftDown && !_rightDown)
        {
            return;
        }

        bool direct = _directInput.ForwardPointer(
            window,
            new PointerInput(PointerAction.Move, point, CurrentButtons(), CurrentModifiers()));
        _directFailed |= !direct;
    }

    private async Task HandleMouseUpAsync(
        nint window,
        string? serial,
        MouseButton? button,
        MappedPoint point,
        CoordinateMappingContext context,
        CancellationToken cancellationToken)
    {
        PointerAction action = button switch
        {
            MouseButton.Left => PointerAction.LeftButtonUp,
            MouseButton.Right => PointerAction.RightButtonUp,
            MouseButton.Middle => PointerAction.MiddleButtonUp,
            _ => PointerAction.LeftButtonUp,
        };
        bool direct = _directInput.ForwardPointer(
            window,
            new PointerInput(action, point, CurrentButtons(), CurrentModifiers()));
        _directFailed |= !direct;

        if (button == MouseButton.Left)
        {
            _leftDown = false;
            if (_directFailed && _dragStart is { } start)
            {
                MappedPoint adbStart = ScaleForAdb(start, context);
                MappedPoint adbEnd = ScaleForAdb(point, context);
                double distance = Math.Sqrt(
                    Math.Pow(point.X - start.X, 2) +
                    Math.Pow(point.Y - start.Y, 2));
                if (!string.IsNullOrWhiteSpace(serial) && distance < 5)
                {
                    await _adbInput
                        .TapAsync(serial, adbEnd.X, adbEnd.Y, cancellationToken)
                        .ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(serial))
                {
                    TimeSpan duration = DateTimeOffset.UtcNow - _dragStartedAt;
                    await _adbInput
                        .SwipeAsync(
                            serial,
                            adbStart.X,
                            adbStart.Y,
                            adbEnd.X,
                            adbEnd.Y,
                            duration,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            _dragStart = null;
            _directFailed = false;
        }
        else if (button == MouseButton.Right)
        {
            _rightDown = false;
        }
    }

    private async Task HandleKeyAsync(
        nint window,
        string? serial,
        Key? key,
        CancellationToken cancellationToken)
    {
        if (key is null)
        {
            return;
        }

        DeviceSpecialKey? special = key switch
        {
            Key.Escape or Key.Back => DeviceSpecialKey.Back,
            Key.Home => DeviceSpecialKey.Home,
            Key.Up => DeviceSpecialKey.ArrowUp,
            Key.Down => DeviceSpecialKey.ArrowDown,
            Key.Left => DeviceSpecialKey.ArrowLeft,
            Key.Right => DeviceSpecialKey.ArrowRight,
            _ => null,
        };

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && key == Key.V)
        {
            string text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            if (text.Length > 0)
            {
                _ = await _inputRouter
                    .PasteClipboardAsync(window, text, serial, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (special is { } specialKey)
        {
            _ = await _inputRouter
                .SendSpecialKeyAsync(window, specialKey, serial, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        VirtualKey? virtualKey = key switch
        {
            Key.Enter => VirtualKey.Enter,
            Key.Tab => VirtualKey.Tab,
            Key.Space => VirtualKey.Space,
            Key.Delete => VirtualKey.Delete,
            Key.Insert => VirtualKey.Insert,
            Key.PageUp => VirtualKey.PageUp,
            Key.PageDown => VirtualKey.PageDown,
            Key.End => VirtualKey.End,
            _ => null,
        };
        if (virtualKey is { } mapped)
        {
            InputModifiers modifiers = CurrentModifiers();
            _ = _directInput.ForwardKey(window, new KeyboardInput(mapped, KeyAction.Down, modifiers));
            _ = _directInput.ForwardKey(window, new KeyboardInput(mapped, KeyAction.Up, modifiers));
        }
    }

    private PointerButtons CurrentButtons()
    {
        PointerButtons buttons = PointerButtons.None;
        if (_leftDown)
        {
            buttons |= PointerButtons.Left;
        }

        if (_rightDown)
        {
            buttons |= PointerButtons.Right;
        }

        return buttons;
    }

    private static InputModifiers CurrentModifiers()
    {
        InputModifiers modifiers = InputModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            modifiers |= InputModifiers.Shift;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            modifiers |= InputModifiers.Control;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            modifiers |= InputModifiers.Alt;
        }

        return modifiers;
    }

    private MappedPoint ScaleForAdb(MappedPoint clientPoint, CoordinateMappingContext context)
    {
        PhoneStatus? status = _deviceMonitor.LastSnapshot?.PhoneStatus;
        ScreenResolution? resolution = status?.ScreenResolution;
        if (resolution is null)
        {
            return clientPoint;
        }

        bool streamIsLandscape = context.ScrcpyClientSizePixels.Width >= context.ScrcpyClientSizePixels.Height;
        bool resolutionIsLandscape = resolution.Value.Width >= resolution.Value.Height;
        if (streamIsLandscape != resolutionIsLandscape)
        {
            resolution = new ScreenResolution(resolution.Value.Height, resolution.Value.Width);
        }

        double sourceWidth = Math.Max(1, context.ScrcpyClientSizePixels.Width - 1);
        double sourceHeight = Math.Max(1, context.ScrcpyClientSizePixels.Height - 1);
        int x = (int)Math.Round(
            Math.Clamp(clientPoint.X / sourceWidth, 0, 1) *
            Math.Max(0, resolution.Value.Width - 1));
        int y = (int)Math.Round(
            Math.Clamp(clientPoint.Y / sourceHeight, 0, 1) *
            Math.Max(0, resolution.Value.Height - 1));
        return new MappedPoint(x, y);
    }
}
