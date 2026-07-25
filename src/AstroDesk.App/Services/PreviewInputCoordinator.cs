using System.Diagnostics;
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
    IScrcpyService scrcpy,
    IDeviceMonitor deviceMonitor,
    CoordinateMapper coordinateMapper) : IPreviewInputCoordinator
{
    private static readonly long MinimumMoveIntervalTicks = Math.Max(1, Stopwatch.Frequency / 120);

    private readonly IInputForwarder _directInput = directInput;
    private readonly IInputRouter _inputRouter = inputRouter;
    private readonly IScrcpyService _scrcpy = scrcpy;
    private readonly IDeviceMonitor _deviceMonitor = deviceMonitor;
    private readonly CoordinateMapper _coordinateMapper = coordinateMapper;
    private bool _leftDown;
    private bool _rightDown;
    private MappedPoint? _mouseLast;
    private MappedPoint? _touchLast;
    private long _lastMoveForwardedTimestamp;

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
                if (_touchLast is { } lastTouchPoint)
                {
                    HandleMouseUp(window, MouseButton.Left, lastTouchPoint);
                    _touchLast = null;
                }

                return;
            }

            if (input.Kind == PreviewInputKind.MouseUp)
            {
                if (_mouseLast is { } lastMousePoint)
                {
                    HandleMouseUp(window, input.MouseButton, lastMousePoint);
                    _mouseLast = null;
                }

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
                _mouseLast = point;
                HandleMouseDown(window, input.MouseButton, point);
                break;
            case PreviewInputKind.MouseMove:
                if (_leftDown || _rightDown)
                {
                    _mouseLast = point;
                    HandleMouseMove(window, point);
                }

                break;
            case PreviewInputKind.MouseUp:
                HandleMouseUp(window, input.MouseButton, point);
                _mouseLast = null;
                break;
            case PreviewInputKind.TouchDown:
                if (_touchLast is not null)
                {
                    break;
                }

                _touchLast = point;
                HandleMouseDown(window, MouseButton.Left, point);
                break;
            case PreviewInputKind.TouchMove:
                if (_touchLast is not null)
                {
                    _touchLast = point;
                    HandleMouseMove(window, point);
                }

                break;
            case PreviewInputKind.TouchUp:
                HandleMouseUp(window, MouseButton.Left, point);
                _touchLast = null;
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

    private void HandleMouseDown(nint window, MouseButton? button, MappedPoint point)
    {
        MouseButton effectiveButton = button ?? MouseButton.Left;
        if ((effectiveButton == MouseButton.Left && _leftDown) ||
            (effectiveButton == MouseButton.Right && _rightDown))
        {
            return;
        }

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
        }
        else if (button == MouseButton.Right)
        {
            _rightDown = true;
        }

        _lastMoveForwardedTimestamp = 0;

        _ = _directInput.ForwardPointer(
            window,
            new PointerInput(action, point, CurrentButtons(), CurrentModifiers()));
    }

    private void HandleMouseMove(nint window, MappedPoint point)
    {
        if (!_leftDown && !_rightDown)
        {
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();
        if (_lastMoveForwardedTimestamp != 0 &&
            timestamp - _lastMoveForwardedTimestamp < MinimumMoveIntervalTicks)
        {
            return;
        }

        _lastMoveForwardedTimestamp = timestamp;

        _ = _directInput.ForwardPointer(
            window,
            new PointerInput(PointerAction.Move, point, CurrentButtons(), CurrentModifiers()));
    }

    private void HandleMouseUp(
        nint window,
        MouseButton? button,
        MappedPoint point)
    {
        MouseButton effectiveButton = button ?? MouseButton.Left;
        if ((effectiveButton == MouseButton.Left && !_leftDown) ||
            (effectiveButton == MouseButton.Right && !_rightDown))
        {
            return;
        }

        PointerAction action = button switch
        {
            MouseButton.Left => PointerAction.LeftButtonUp,
            MouseButton.Right => PointerAction.RightButtonUp,
            MouseButton.Middle => PointerAction.MiddleButtonUp,
            _ => PointerAction.LeftButtonUp,
        };
        _ = _directInput.ForwardPointer(
            window,
            new PointerInput(action, point, CurrentButtons(), CurrentModifiers()));

        if (button == MouseButton.Left)
        {
            _leftDown = false;
        }
        else if (button == MouseButton.Right)
        {
            _rightDown = false;
        }


        _lastMoveForwardedTimestamp = 0;
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

}
