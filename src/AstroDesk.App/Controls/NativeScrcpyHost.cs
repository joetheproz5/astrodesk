using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AstroDesk.App.Controls;

/// <summary>
/// Hosts the real scrcpy SDL window as a native child of the WPF layout.
/// Input goes directly to scrcpy; no captured-bitmap pointer forwarding is involved.
/// </summary>
public sealed class NativeScrcpyHost : HwndHost
{
    public static readonly DependencyProperty SourceWindowHandleProperty = DependencyProperty.Register(
        nameof(SourceWindowHandle),
        typeof(nint),
        typeof(NativeScrcpyHost),
        new FrameworkPropertyMetadata(nint.Zero, HandleSourceWindowChanged));

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsClipChildren = 0x02000000L;
    private const long WsClipSiblings = 0x04000000L;
    private const long WsExAppWindow = 0x00040000L;
    private const long WsExToolWindow = 0x00000080L;
    private const int WmPaint = 0x000F;
    private const int WmEraseBackground = 0x0014;
    private const int BlackBrush = 4;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    private nint _hostWindow;
    private nint _attachedWindow;

    public NativeScrcpyHost()
    {
        Focusable = true;
        SizeChanged += (_, _) => ResizeAttachedWindow();
    }

    public nint SourceWindowHandle
    {
        get => (nint)GetValue(SourceWindowHandleProperty);
        set => SetValue(SourceWindowHandleProperty, value);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostWindow = CreateWindowExW(
            0,
            "STATIC",
            string.Empty,
            unchecked((int)(WsChild | WsVisible | WsClipChildren | WsClipSiblings)),
            0,
            0,
            Math.Max(1, (int)Math.Ceiling(ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(ActualHeight)),
            hwndParent.Handle,
            nint.Zero,
            nint.Zero,
            nint.Zero);
        if (_hostWindow == nint.Zero)
        {
            throw new InvalidOperationException("Windows could not create the native scrcpy host container.");
        }

        AttachSourceWindow(SourceWindowHandle);
        return new HandleRef(this, _hostWindow);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DetachSourceWindow();
        if (hwnd.Handle != nint.Zero)
        {
            _ = DestroyWindow(hwnd.Handle);
        }

        _hostWindow = nint.Zero;
    }

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        EnsureAttached();
        ResizeAttachedWindow();
    }

    protected override nint WndProc(
        nint hwnd,
        int message,
        nint wParam,
        nint lParam,
        ref bool handled)
    {
        if (message == WmEraseBackground)
        {
            FillHostBlack(hwnd, wParam);
            handled = true;
            return new nint(1);
        }

        if (message == WmPaint)
        {
            nint deviceContext = GetDC(hwnd);
            if (deviceContext != nint.Zero)
            {
                FillHostBlack(hwnd, deviceContext);
                _ = ReleaseDC(hwnd, deviceContext);
            }

            _ = ValidateRect(hwnd, nint.Zero);
            handled = true;
            return nint.Zero;
        }

        return base.WndProc(hwnd, message, wParam, lParam, ref handled);
    }

    private static void FillHostBlack(nint window, nint deviceContext)
    {
        if (deviceContext != nint.Zero && GetClientRect(window, out NativeRect bounds))
        {
            _ = FillRect(deviceContext, ref bounds, GetStockObject(BlackBrush));
        }
    }

    private static void HandleSourceWindowChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is NativeScrcpyHost host)
        {
            host.AttachSourceWindow((nint)args.NewValue);
        }
    }

    private void EnsureAttached()
    {
        nint source = SourceWindowHandle;
        if (_hostWindow == nint.Zero)
        {
            return;
        }

        if (source == nint.Zero || !IsWindow(source))
        {
            _ = ShowWindow(_hostWindow, SwHide);
            return;
        }

        _ = ShowWindow(_hostWindow, SwShowNoActivate);
        if (GetParent(source) != _hostWindow)
        {
            AttachSourceWindow(source);
        }
    }

    private void AttachSourceWindow(nint sourceWindow)
    {
        if (_hostWindow == nint.Zero)
        {
            return;
        }

        if (_attachedWindow != sourceWindow)
        {
            DetachSourceWindow();
        }

        if (sourceWindow == nint.Zero || !IsWindow(sourceWindow))
        {
            _ = ShowWindow(_hostWindow, SwHide);
            return;
        }

        _ = ShowWindow(_hostWindow, SwShowNoActivate);

        long style = GetWindowLongPtr(sourceWindow, GwlStyle).ToInt64();
        style &= ~(WsPopup | WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
        style |= WsChild | WsVisible | WsClipChildren | WsClipSiblings;
        _ = SetWindowLongPtr(sourceWindow, GwlStyle, new nint(style));

        long extendedStyle = GetWindowLongPtr(sourceWindow, GwlExStyle).ToInt64();
        extendedStyle = (extendedStyle & ~WsExAppWindow) | WsExToolWindow;
        _ = SetWindowLongPtr(sourceWindow, GwlExStyle, new nint(extendedStyle));

        _ = SetParent(sourceWindow, _hostWindow);
        _attachedWindow = sourceWindow;
        ResizeAttachedWindow();
    }

    private void DetachSourceWindow()
    {
        nint source = _attachedWindow;
        _attachedWindow = nint.Zero;
        if (source == nint.Zero || !IsWindow(source) || GetParent(source) != _hostWindow)
        {
            return;
        }

        _ = ShowWindow(source, SwHide);
        _ = SetParent(source, nint.Zero);
    }

    private void ResizeAttachedWindow()
    {
        nint source = _attachedWindow;
        if (_hostWindow == nint.Zero || source == nint.Zero || !IsWindow(source))
        {
            return;
        }

        if (!GetClientRect(_hostWindow, out NativeRect bounds))
        {
            return;
        }

        _ = SetWindowPos(
            source,
            nint.Zero,
            0,
            0,
            Math.Max(1, bounds.Right - bounds.Left),
            Math.Max(1, bounds.Bottom - bounds.Top),
            SwpNoActivate | SwpNoOwnerZOrder | SwpFrameChanged | SwpShowWindow);
    }

    private static nint GetWindowLongPtr(nint window, int index) =>
        nint.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new nint(GetWindowLong32(window, index));

    private static nint SetWindowLongPtr(nint window, int index, nint value) =>
        nint.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new nint(SetWindowLong32(window, index, value.ToInt32()));

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint newParent);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("user32.dll")]
    private static extern int FillRect(nint deviceContext, ref NativeRect rect, nint brush);

    [DllImport("gdi32.dll")]
    private static extern nint GetStockObject(int objectIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ValidateRect(nint window, nint rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint window, int index, nint value);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
