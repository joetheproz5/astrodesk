using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace AstroDesk.App.Controls;

/// <summary>
/// Floats the framing guides above the native scrcpy window.
/// </summary>
/// <remarks>
/// <para>
/// WPF cannot composite its own content over an <see cref="HwndHost"/>. The
/// native child window scrcpy is reparented into owns those pixels outright, so
/// the guides drawn by the <see cref="PhonePreviewControl"/> underneath it were
/// simply never visible: ticking Thirds or Cross appeared to do nothing whenever
/// the real preview was running.
/// </para>
/// <para>
/// Another top-level window can sit above a native child window, and an owned
/// window is always above its owner in z-order. That gives the guides somewhere
/// to live without <see cref="Window.Topmost"/> - which would float them over
/// other applications - and without touching scrcpy at all.
/// </para>
/// <para>
/// The alternative was parking scrcpy off-screen and falling back to the
/// captured-frame preview, which already renders guides correctly. That was
/// rejected for two reasons: it depends on PrintWindow returning real pixels for
/// a GPU-rendered SDL window, which is the classic case where PrintWindow
/// returns black, and it would trade live video for a screen-scrape of it. It
/// also gets nowhere near the thing that must never happen here - collapsing the
/// host, which runs DestroyWindowCore and takes the scrcpy process down with it.
/// </para>
/// </remarks>
public sealed class PreviewOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private Window? _owner;
    private FrameworkElement? _target;
    private Rect _lastBounds = Rect.Empty;
    private bool _detached;

    public PreviewOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Focusable = false;

        // The guides are decoration, never a target. Without this a tap meant for
        // the phone would land on the overlay and never reach scrcpy.
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Binds the overlay to the window it rides above.
    /// </summary>
    public void Attach(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        _owner = owner;
        Owner = owner;

        owner.LocationChanged += HandleOwnerMoved;
        owner.SizeChanged += HandleOwnerResized;
        owner.StateChanged += HandleOwnerStateChanged;
    }

    /// <summary>
    /// Tracks <paramref name="target"/>, so the overlay stays exactly over it as
    /// the window moves, resizes, or changes DPI. Passing null hides the overlay.
    /// </summary>
    /// <remarks>
    /// Retargeting rather than owning a fixed element lets one overlay follow the
    /// preview between its docked and fullscreen positions, which are two
    /// different elements in the tree.
    /// </remarks>
    public void SetTarget(FrameworkElement? target)
    {
        if (ReferenceEquals(_target, target))
        {
            return;
        }

        if (_target is not null)
        {
            _target.LayoutUpdated -= HandleLayoutUpdated;
            _target.IsVisibleChanged -= HandleTargetVisibilityChanged;
        }

        _target = target;

        if (_target is not null)
        {
            _target.LayoutUpdated += HandleLayoutUpdated;
            _target.IsVisibleChanged += HandleTargetVisibilityChanged;
        }

        // The previous target's rect must not linger as the change gate.
        _lastBounds = Rect.Empty;
        UpdateBounds();
    }

    public void Detach()
    {
        if (_detached)
        {
            return;
        }

        _detached = true;

        if (_owner is not null)
        {
            _owner.LocationChanged -= HandleOwnerMoved;
            _owner.SizeChanged -= HandleOwnerResized;
            _owner.StateChanged -= HandleOwnerStateChanged;
        }

        if (_target is not null)
        {
            _target.LayoutUpdated -= HandleLayoutUpdated;
            _target.IsVisibleChanged -= HandleTargetVisibilityChanged;
        }

        _owner = null;
        _target = null;
    }

    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);

        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        // WS_EX_TRANSPARENT makes the whole window invisible to hit-testing at the
        // Win32 level, so clicks fall through to scrcpy underneath rather than
        // being swallowed. IsHitTestVisible alone only covers WPF's own routing.
        long extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        extendedStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
        _ = SetWindowLongPtr(handle, GwlExStyle, new nint(extendedStyle));
    }

    private void HandleOwnerMoved(object? sender, EventArgs args) => UpdateBounds();

    private void HandleOwnerResized(object? sender, SizeChangedEventArgs args) => UpdateBounds();

    private void HandleOwnerStateChanged(object? sender, EventArgs args) => UpdateBounds();

    private void HandleLayoutUpdated(object? sender, EventArgs args) => UpdateBounds();

    private void HandleTargetVisibilityChanged(
        object sender,
        DependencyPropertyChangedEventArgs args) => UpdateBounds();

    /// <summary>
    /// Aligns the overlay with the tracked element, or hides it when there is
    /// nothing to sit over.
    /// </summary>
    private void UpdateBounds()
    {
        if (_detached || _owner is null)
        {
            return;
        }

        if (_target is null)
        {
            HideOverlay();
            return;
        }

        // A minimised or hidden owner leaves the target with a stale layout rect,
        // which would strand the overlay somewhere on screen by itself.
        if (!_target.IsVisible ||
            _owner.WindowState == WindowState.Minimized ||
            _target.ActualWidth <= 0 ||
            _target.ActualHeight <= 0 ||
            PresentationSource.FromVisual(_target) is null)
        {
            HideOverlay();
            return;
        }

        Point topLeft;
        Point bottomRight;
        try
        {
            topLeft = _target.PointToScreen(new Point(0, 0));
            bottomRight = _target.PointToScreen(
                new Point(_target.ActualWidth, _target.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            // The visual lost its presentation source between the guard and here.
            HideOverlay();
            return;
        }

        Rect bounds = new(topLeft, bottomRight);
        if (bounds.Width < 1 || bounds.Height < 1)
        {
            HideOverlay();
            return;
        }

        if (bounds == _lastBounds && IsVisible)
        {
            return;
        }

        _lastBounds = bounds;

        if (!IsVisible)
        {
            Show();
        }

        nint handle = new WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return;
        }

        // Positioned through SetWindowPos in physical pixels rather than
        // Left/Top, which are DIPs against the primary monitor's scaling and so
        // land in the wrong place on a second monitor at a different DPI.
        _ = SetWindowPos(
            handle,
            nint.Zero,
            (int)Math.Round(bounds.X),
            (int)Math.Round(bounds.Y),
            (int)Math.Round(bounds.Width),
            (int)Math.Round(bounds.Height),
            SwpNoActivate | SwpNoZOrder | SwpNoOwnerZOrder);
    }

    private void HideOverlay()
    {
        _lastBounds = Rect.Empty;
        if (IsVisible)
        {
            Hide();
        }
    }

    private static nint GetWindowLongPtr(nint window, int index) =>
        nint.Size == 8
            ? GetWindowLongPtr64(window, index)
            : new nint(GetWindowLong32(window, index));

    private static nint SetWindowLongPtr(nint window, int index, nint value) =>
        nint.Size == 8
            ? SetWindowLongPtr64(window, index, value)
            : new nint(SetWindowLong32(window, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(nint window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern nint SetWindowLongPtr64(nint window, int index, nint value);

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
}
