using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AstroDesk.Device.Scrcpy;

public readonly record struct NativeWindowRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

public sealed record WindowPresentationState(
    nint WindowHandle,
    NativeWindowRect OriginalBounds,
    nint OriginalExtendedStyle);

public interface IScrcpyWindowManager
{
    Task<nint> FindWindowAsync(
        string exactTitle,
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    WindowPresentationState HideOffscreenWithoutMinimizing(nint windowHandle);

    void Restore(WindowPresentationState state);
}

/// <summary>
/// Keeps the real scrcpy window rendered, but moves it offscreen and removes its taskbar style.
/// </summary>
public sealed class Win32ScrcpyWindowManager : IScrcpyWindowManager
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const int SwShowNoActivate = 4;
    private const int HiddenCoordinate = -32000;

    public async Task<nint> FindWindowAsync(
        string exactTitle,
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exactTitle);
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handle = FindWindowW(null, exactTitle);
            if (handle != nint.Zero)
            {
                _ = GetWindowThreadProcessId(handle, out var actualProcessId);
                if (actualProcessId == (uint)processId)
                {
                    return handle;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }

        return nint.Zero;
    }

    public WindowPresentationState HideOffscreenWithoutMinimizing(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            throw new ArgumentException("A valid window handle is required.", nameof(windowHandle));
        }

        if (!GetWindowRect(windowHandle, out var rect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not read the scrcpy window bounds.");
        }

        var originalStyle = GetWindowLongPtr(windowHandle, GwlExStyle);
        var updatedStyle = new nint(
            (originalStyle.ToInt64() | WsExToolWindow) & ~WsExAppWindow);
        _ = SetWindowLongPtr(windowHandle, GwlExStyle, updatedStyle);
        _ = ShowWindow(windowHandle, SwShowNoActivate);

        if (!SetWindowPos(
                windowHandle,
                nint.Zero,
                HiddenCoordinate,
                HiddenCoordinate,
                Math.Max(1, rect.Right - rect.Left),
                Math.Max(1, rect.Bottom - rect.Top),
                SwpNoActivate | SwpNoOwnerZOrder | SwpFrameChanged | SwpShowWindow))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not move the scrcpy window offscreen.");
        }

        return new WindowPresentationState(
            windowHandle,
            new NativeWindowRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            originalStyle);
    }

    public void Restore(WindowPresentationState state)
    {
        if (state.WindowHandle == nint.Zero)
        {
            return;
        }

        _ = SetWindowLongPtr(state.WindowHandle, GwlExStyle, state.OriginalExtendedStyle);
        _ = SetWindowPos(
            state.WindowHandle,
            nint.Zero,
            state.OriginalBounds.Left,
            state.OriginalBounds.Top,
            Math.Max(1, state.OriginalBounds.Width),
            Math.Max(1, state.OriginalBounds.Height),
            SwpNoActivate | SwpNoOwnerZOrder | SwpFrameChanged);
    }

    private static nint GetWindowLongPtr(nint windowHandle, int index) =>
        nint.Size == 8
            ? GetWindowLongPtr64(windowHandle, index)
            : new nint(GetWindowLong32(windowHandle, index));

    private static nint SetWindowLongPtr(nint windowHandle, int index, nint value) =>
        nint.Size == 8
            ? SetWindowLongPtr64(windowHandle, index, value)
            : new nint(SetWindowLong32(windowHandle, index, value.ToInt32()));

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowW(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint windowHandle, out Rect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint windowHandle, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint windowHandle, int index, nint value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
