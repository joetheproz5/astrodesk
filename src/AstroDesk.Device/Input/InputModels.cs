using AstroDesk.Device.Adb;

namespace AstroDesk.Device.Input;

public readonly record struct MappedPoint(int X, int Y);

[Flags]
public enum PointerButtons
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
}

[Flags]
public enum InputModifiers
{
    None = 0,
    Shift = 1,
    Control = 2,
    Alt = 4,
}

public enum PointerAction
{
    Move,
    LeftButtonDown,
    LeftButtonUp,
    RightButtonDown,
    RightButtonUp,
    MiddleButtonDown,
    MiddleButtonUp,
    Wheel,
}

public readonly record struct PointerInput(
    PointerAction Action,
    MappedPoint Position,
    PointerButtons Buttons = PointerButtons.None,
    InputModifiers Modifiers = InputModifiers.None,
    int WheelDelta = 0);

public enum KeyAction
{
    Down,
    Up,
}

public enum VirtualKey : ushort
{
    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Shift = 0x10,
    Control = 0x11,
    Alt = 0x12,
    Escape = 0x1B,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    Insert = 0x2D,
    Delete = 0x2E,
    A = 0x41,
    B = 0x42,
    C = 0x43,
    H = 0x48,
    O = 0x4F,
    P = 0x50,
    R = 0x52,
    S = 0x53,
    V = 0x56,
    VolumeDown = 0xAE,
    VolumeUp = 0xAF,
}

public readonly record struct KeyboardInput(
    VirtualKey Key,
    KeyAction Action,
    InputModifiers Modifiers = InputModifiers.None,
    bool IsRepeat = false);

public enum DeviceSpecialKey
{
    Back,
    Home,
    RecentApps,
    VolumeUp,
    VolumeDown,
    Power,
    Escape,
    ArrowUp,
    ArrowDown,
    ArrowLeft,
    ArrowRight,
    RotatePhone,
    PasteClipboard,
    ScreenOffWhileMirroring,
}

public sealed record KeyChord(
    VirtualKey Key,
    InputModifiers Modifiers,
    AndroidKeyCode? AdbFallbackKey);

public static class SpecialKeyMap
{
    public static KeyChord Get(DeviceSpecialKey key) =>
        key switch
        {
            DeviceSpecialKey.Back => new KeyChord(VirtualKey.Escape, InputModifiers.None, AndroidKeyCode.Back),
            DeviceSpecialKey.Home => new KeyChord(VirtualKey.H, InputModifiers.Alt, AndroidKeyCode.Home),
            DeviceSpecialKey.RecentApps => new KeyChord(VirtualKey.S, InputModifiers.Alt, AndroidKeyCode.AppSwitch),
            DeviceSpecialKey.VolumeUp => new KeyChord(VirtualKey.Up, InputModifiers.Alt, AndroidKeyCode.VolumeUp),
            DeviceSpecialKey.VolumeDown => new KeyChord(VirtualKey.Down, InputModifiers.Alt, AndroidKeyCode.VolumeDown),
            DeviceSpecialKey.Power => new KeyChord(VirtualKey.P, InputModifiers.Alt, AndroidKeyCode.Power),
            DeviceSpecialKey.Escape => new KeyChord(VirtualKey.Escape, InputModifiers.None, AndroidKeyCode.Escape),
            DeviceSpecialKey.ArrowUp => new KeyChord(VirtualKey.Up, InputModifiers.None, AndroidKeyCode.DpadUp),
            DeviceSpecialKey.ArrowDown => new KeyChord(VirtualKey.Down, InputModifiers.None, AndroidKeyCode.DpadDown),
            DeviceSpecialKey.ArrowLeft => new KeyChord(VirtualKey.Left, InputModifiers.None, AndroidKeyCode.DpadLeft),
            DeviceSpecialKey.ArrowRight => new KeyChord(VirtualKey.Right, InputModifiers.None, AndroidKeyCode.DpadRight),
            DeviceSpecialKey.RotatePhone => new KeyChord(VirtualKey.R, InputModifiers.Alt, null),
            DeviceSpecialKey.PasteClipboard => new KeyChord(VirtualKey.V, InputModifiers.Alt, null),
            DeviceSpecialKey.ScreenOffWhileMirroring => new KeyChord(VirtualKey.O, InputModifiers.Alt, null),
            _ => throw new ArgumentOutOfRangeException(nameof(key)),
        };
}

public readonly record struct TranslatedInputMessage(
    uint Message,
    nuint WParam,
    nint LParam,
    bool UsesScreenCoordinates = false);

public static class InputMessageTranslator
{
    public const uint WmKeyDown = 0x0100;
    public const uint WmKeyUp = 0x0101;
    public const uint WmChar = 0x0102;
    public const uint WmSysKeyDown = 0x0104;
    public const uint WmSysKeyUp = 0x0105;
    public const uint WmMouseMove = 0x0200;
    public const uint WmLeftButtonDown = 0x0201;
    public const uint WmLeftButtonUp = 0x0202;
    public const uint WmRightButtonDown = 0x0204;
    public const uint WmRightButtonUp = 0x0205;
    public const uint WmMiddleButtonDown = 0x0207;
    public const uint WmMiddleButtonUp = 0x0208;
    public const uint WmMouseWheel = 0x020A;

    private const uint MkLeftButton = 0x0001;
    private const uint MkRightButton = 0x0002;
    private const uint MkShift = 0x0004;
    private const uint MkControl = 0x0008;
    private const uint MkMiddleButton = 0x0010;

    public static TranslatedInputMessage Translate(PointerInput input)
    {
        var flags = BuildPointerFlags(input.Buttons, input.Modifiers);
        var message = input.Action switch
        {
            PointerAction.Move => WmMouseMove,
            PointerAction.LeftButtonDown => WmLeftButtonDown,
            PointerAction.LeftButtonUp => WmLeftButtonUp,
            PointerAction.RightButtonDown => WmRightButtonDown,
            PointerAction.RightButtonUp => WmRightButtonUp,
            PointerAction.MiddleButtonDown => WmMiddleButtonDown,
            PointerAction.MiddleButtonUp => WmMiddleButtonUp,
            PointerAction.Wheel => WmMouseWheel,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };

        flags = input.Action switch
        {
            PointerAction.LeftButtonDown => flags | MkLeftButton,
            PointerAction.LeftButtonUp => flags & ~MkLeftButton,
            PointerAction.RightButtonDown => flags | MkRightButton,
            PointerAction.RightButtonUp => flags & ~MkRightButton,
            PointerAction.MiddleButtonDown => flags | MkMiddleButton,
            PointerAction.MiddleButtonUp => flags & ~MkMiddleButton,
            _ => flags,
        };

        nuint wParam = flags;
        if (input.Action == PointerAction.Wheel)
        {
            var wheel = unchecked((ushort)(short)input.WheelDelta);
            wParam = (nuint)(flags | ((uint)wheel << 16));
        }

        return new TranslatedInputMessage(
            message,
            wParam,
            PackPoint(input.Position),
            input.Action == PointerAction.Wheel);
    }

    public static TranslatedInputMessage Translate(KeyboardInput input)
    {
        var systemKey = input.Modifiers.HasFlag(InputModifiers.Alt);
        var message = (input.Action, systemKey) switch
        {
            (KeyAction.Down, false) => WmKeyDown,
            (KeyAction.Up, false) => WmKeyUp,
            (KeyAction.Down, true) => WmSysKeyDown,
            (KeyAction.Up, true) => WmSysKeyUp,
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };

        uint keyData = 1;
        if (systemKey)
        {
            keyData |= 1u << 29;
        }

        if (input.IsRepeat || input.Action == KeyAction.Up)
        {
            keyData |= 1u << 30;
        }

        if (input.Action == KeyAction.Up)
        {
            keyData |= 1u << 31;
        }

        return new TranslatedInputMessage(message, (nuint)input.Key, unchecked((nint)(int)keyData));
    }

    public static IReadOnlyList<TranslatedInputMessage> TranslateText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Select(character =>
                new TranslatedInputMessage(WmChar, character, nint.Zero))
            .ToArray();
    }

    public static nint PackPoint(MappedPoint point)
    {
        if (point.X is < short.MinValue or > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Mapped X coordinate must fit in a Win32 signed 16-bit coordinate.");
        }

        if (point.Y is < short.MinValue or > short.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "Mapped Y coordinate must fit in a Win32 signed 16-bit coordinate.");
        }

        var packed = unchecked((uint)(ushort)(short)point.X)
                     | (unchecked((uint)(ushort)(short)point.Y) << 16);
        return unchecked((nint)(int)packed);
    }

    private static uint BuildPointerFlags(PointerButtons buttons, InputModifiers modifiers)
    {
        uint flags = 0;
        if (buttons.HasFlag(PointerButtons.Left))
        {
            flags |= MkLeftButton;
        }

        if (buttons.HasFlag(PointerButtons.Right))
        {
            flags |= MkRightButton;
        }

        if (buttons.HasFlag(PointerButtons.Middle))
        {
            flags |= MkMiddleButton;
        }

        if (modifiers.HasFlag(InputModifiers.Shift))
        {
            flags |= MkShift;
        }

        if (modifiers.HasFlag(InputModifiers.Control))
        {
            flags |= MkControl;
        }

        return flags;
    }
}
