using System.Runtime.InteropServices;
using System.Text;

namespace AstroDesk.Device.Input;

public interface IWindowMessageSink
{
    bool Post(nint windowHandle, TranslatedInputMessage message);

    bool TryClientToScreen(nint windowHandle, MappedPoint clientPoint, out MappedPoint screenPoint);

    bool TryFocus(nint windowHandle);
}

public sealed class NativeWindowMessageSink : IWindowMessageSink
{
    public bool Post(nint windowHandle, TranslatedInputMessage message) =>
        windowHandle != nint.Zero
        && PostMessageW(windowHandle, message.Message, message.WParam, message.LParam);

    public bool TryClientToScreen(nint windowHandle, MappedPoint clientPoint, out MappedPoint screenPoint)
    {
        var point = new NativePoint { X = clientPoint.X, Y = clientPoint.Y };
        var succeeded = windowHandle != nint.Zero && ClientToScreen(windowHandle, ref point);
        screenPoint = succeeded ? new MappedPoint(point.X, point.Y) : clientPoint;
        return succeeded;
    }

    public bool TryFocus(nint windowHandle) =>
        windowHandle != nint.Zero && SetForegroundWindow(windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessageW(nint windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint windowHandle, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}

public interface IClipboardBridge
{
    bool TrySetText(string text);
}

public sealed class Win32ClipboardBridge : IClipboardBridge
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    public bool TrySetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!OpenClipboard(nint.Zero))
        {
            return false;
        }

        nint memory = nint.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var bytes = Encoding.Unicode.GetBytes(text + '\0');
            memory = GlobalAlloc(GmemMoveable, (nuint)bytes.Length);
            if (memory == nint.Zero)
            {
                return false;
            }

            var destination = GlobalLock(memory);
            if (destination == nint.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(bytes, 0, destination, bytes.Length);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == nint.Zero)
            {
                return false;
            }

            memory = nint.Zero;
            return true;
        }
        finally
        {
            if (memory != nint.Zero)
            {
                _ = GlobalFree(memory);
            }

            _ = CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(nint newOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetClipboardData(uint format, nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalLock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(nint memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GlobalFree(nint memory);
}

public interface IInputForwarder
{
    bool ForwardPointer(nint windowHandle, PointerInput input);

    bool ForwardKey(nint windowHandle, KeyboardInput input);

    bool ForwardText(nint windowHandle, string text);

    bool ForwardSpecialKey(nint windowHandle, DeviceSpecialKey key);

    bool PasteClipboard(nint windowHandle, string text);
}

/// <summary>
/// Posts already-mapped input directly to the hidden scrcpy client window.
/// Mouse-down focuses the target so subsequent physical keyboard input follows it.
/// </summary>
public sealed class Win32InputForwarder : IInputForwarder
{
    private readonly IWindowMessageSink _messageSink;
    private readonly IClipboardBridge _clipboard;

    public Win32InputForwarder(
        IWindowMessageSink? messageSink = null,
        IClipboardBridge? clipboard = null)
    {
        _messageSink = messageSink ?? new NativeWindowMessageSink();
        _clipboard = clipboard ?? new Win32ClipboardBridge();
    }

    public bool ForwardPointer(nint windowHandle, PointerInput input)
    {
        if (input.Action == PointerAction.LeftButtonDown)
        {
            _ = _messageSink.TryFocus(windowHandle);
        }

        var message = InputMessageTranslator.Translate(input);
        if (message.UsesScreenCoordinates)
        {
            if (!_messageSink.TryClientToScreen(windowHandle, input.Position, out var screenPoint))
            {
                return false;
            }

            message = message with { LParam = InputMessageTranslator.PackPoint(screenPoint) };
        }

        return _messageSink.Post(windowHandle, message);
    }

    public bool ForwardKey(nint windowHandle, KeyboardInput input) =>
        _messageSink.Post(windowHandle, InputMessageTranslator.Translate(input));

    public bool ForwardText(nint windowHandle, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var succeeded = true;
        foreach (var message in InputMessageTranslator.TranslateText(text))
        {
            succeeded &= _messageSink.Post(windowHandle, message);
        }

        return succeeded;
    }

    public bool ForwardSpecialKey(nint windowHandle, DeviceSpecialKey key)
    {
        var chord = SpecialKeyMap.Get(key);
        return SendChord(windowHandle, chord);
    }

    public bool PasteClipboard(nint windowHandle, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _clipboard.TrySetText(text)
               && ForwardSpecialKey(windowHandle, DeviceSpecialKey.PasteClipboard);
    }

    private bool SendChord(nint windowHandle, KeyChord chord)
    {
        var succeeded = true;
        foreach (var modifier in EnumerateModifiers(chord.Modifiers))
        {
            succeeded &= ForwardKey(
                windowHandle,
                new KeyboardInput(modifier, KeyAction.Down, chord.Modifiers));
        }

        succeeded &= ForwardKey(
            windowHandle,
            new KeyboardInput(chord.Key, KeyAction.Down, chord.Modifiers));
        succeeded &= ForwardKey(
            windowHandle,
            new KeyboardInput(chord.Key, KeyAction.Up, chord.Modifiers));

        foreach (var modifier in EnumerateModifiers(chord.Modifiers).Reverse())
        {
            succeeded &= ForwardKey(
                windowHandle,
                new KeyboardInput(modifier, KeyAction.Up, chord.Modifiers));
        }

        return succeeded;
    }

    private static IEnumerable<VirtualKey> EnumerateModifiers(InputModifiers modifiers)
    {
        if (modifiers.HasFlag(InputModifiers.Control))
        {
            yield return VirtualKey.Control;
        }

        if (modifiers.HasFlag(InputModifiers.Shift))
        {
            yield return VirtualKey.Shift;
        }

        if (modifiers.HasFlag(InputModifiers.Alt))
        {
            yield return VirtualKey.Alt;
        }
    }
}
