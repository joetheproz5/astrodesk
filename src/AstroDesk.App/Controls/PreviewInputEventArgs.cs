using System.Windows;
using System.Windows.Input;

namespace AstroDesk.App.Controls;

public enum PreviewInputKind
{
    MouseDown,
    MouseMove,
    MouseUp,
    TouchDown,
    TouchMove,
    TouchUp,
    MouseWheel,
    KeyDown,
    TextInput,
}

public sealed class PreviewInputEventArgs : EventArgs
{
    public PreviewInputEventArgs(
        PreviewInputKind kind,
        Point position,
        MouseButton? mouseButton = null,
        int wheelDelta = 0,
        Key? key = null,
        string? text = null)
    {
        Kind = kind;
        Position = position;
        MouseButton = mouseButton;
        WheelDelta = wheelDelta;
        Key = key;
        Text = text;
    }

    public PreviewInputKind Kind { get; }

    public Point Position { get; }

    public MouseButton? MouseButton { get; }

    public int WheelDelta { get; }

    public Key? Key { get; }

    public string? Text { get; }
}
