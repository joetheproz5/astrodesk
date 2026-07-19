using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroDesk.App.Controls;
using AstroDesk.App.Services;
using AstroDesk.App.ViewModels;
using AstroDesk.Capture.Geometry;
using AstroDesk.Core.Enums;
using Microsoft.Win32;

namespace AstroDesk.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private readonly IPreviewInputCoordinator _inputCoordinator;
    private Point? _lastZoomPanPoint;
    private bool _zoomGestureActive;
    private bool _shutdownInProgress;
    private bool _shutdownComplete;
    private HwndSource? _windowSource;
    private PreviewOverlayWindow? _guideOverlay;
    private bool _previewFullscreenApplied;
    private WindowStyle _savedWindowStyle;
    private ResizeMode _savedResizeMode;
    private WindowState _savedWindowState;
    private bool _savedTopmost;
    private Rect _savedRestoreBounds;

    private const int PreviewFullscreenHotKeyId = 0xAD11;
    private const int WmHotKey = 0x0312;
    private const uint VkF11 = 0x7A;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);

    public MainWindow(
        MainWindowViewModel viewModel,
        IPreviewInputCoordinator inputCoordinator)
    {
        _viewModel = viewModel;
        _inputCoordinator = inputCoordinator;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += HandleLoaded;
        Closing += HandleClosing;
        Closed += HandleClosed;
        SourceInitialized += HandleSourceInitialized;
        _viewModel.PropertyChanged += HandleViewModelPropertyChanged;
        _viewModel.VisualScreenshotRequested += HandleVisualScreenshotRequested;
        _viewModel.ConfirmationRequested += HandleConfirmationRequested;
    }

    private async void HandleLoaded(object sender, RoutedEventArgs args)
    {
        CreateGuideOverlay();

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"AstroDesk could not finish startup.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "AstroDesk startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Builds the transparent window that carries the framing guides above the
    /// native scrcpy window.
    /// </summary>
    /// <remarks>
    /// Its content is a second <see cref="PhonePreviewControl"/> in guides-only
    /// mode rather than a purpose-built element, so the guides on the two
    /// surfaces are drawn by exactly the same code and cannot drift apart.
    /// </remarks>
    private void CreateGuideOverlay()
    {
        if (_guideOverlay is not null)
        {
            return;
        }

        PhonePreviewControl guides = new()
        {
            GuidesOnly = true,
            IsHitTestVisible = false,
            DataContext = _viewModel,
        };

        BindGuide(guides, PhonePreviewControl.RuleOfThirdsProperty, nameof(MainWindowViewModel.RuleOfThirds));
        BindGuide(guides, PhonePreviewControl.CrosshairProperty, nameof(MainWindowViewModel.Crosshair));
        BindGuide(guides, PhonePreviewControl.DiagonalGuidesProperty, nameof(MainWindowViewModel.DiagonalGuides));
        BindGuide(guides, PhonePreviewControl.SafeAreaProperty, nameof(MainWindowViewModel.SafeArea));
        BindGuide(guides, PhonePreviewControl.HorizonLineProperty, nameof(MainWindowViewModel.HorizonLine));
        BindGuide(guides, PhonePreviewControl.CustomRectangleProperty, nameof(MainWindowViewModel.CustomRectangle));
        BindGuide(guides, PhonePreviewControl.CircleOverlayProperty, nameof(MainWindowViewModel.CircleOverlay));
        BindGuide(
            guides,
            PhonePreviewControl.CustomRectangleWidthPercentProperty,
            nameof(MainWindowViewModel.CustomRectangleWidthPercent));
        BindGuide(
            guides,
            PhonePreviewControl.CustomRectangleHeightPercentProperty,
            nameof(MainWindowViewModel.CustomRectangleHeightPercent));
        BindGuide(guides, PhonePreviewControl.OverlayBrushProperty, nameof(MainWindowViewModel.OverlayBrush));
        BindGuide(guides, PhonePreviewControl.OverlayOpacityProperty, nameof(MainWindowViewModel.OverlayOpacity));
        BindGuide(guides, PhonePreviewControl.OverlayThicknessProperty, nameof(MainWindowViewModel.OverlayThickness));
        BindGuide(guides, PhonePreviewControl.ShootingZoneProperty, nameof(MainWindowViewModel.ShootingZone));

        _guideOverlay = new PreviewOverlayWindow { Content = guides };
        _guideOverlay.Attach(this);
        UpdateGuideOverlayTarget();
    }

    private static void BindGuide(
        PhonePreviewControl target,
        DependencyProperty property,
        string path) =>
        target.SetBinding(property, new Binding(path) { Mode = BindingMode.OneWay });

    /// <summary>
    /// Points the overlay at whichever preview is currently showing scrcpy's own
    /// window, or hides it when nothing is.
    /// </summary>
    /// <remarks>
    /// The guides are only needed where WPF cannot draw them. When scrcpy is not
    /// running, the preview control underneath draws its own guides over the
    /// captured frame, and floating a second copy on top would double every line.
    /// The two handle properties are mutually exclusive by construction, so at
    /// most one target is ever live.
    /// </remarks>
    private void UpdateGuideOverlayTarget()
    {
        if (_guideOverlay is null)
        {
            return;
        }

        FrameworkElement? target = null;
        if (_viewModel.MainScrcpyWindowHandle != nint.Zero)
        {
            target = PhonePreview;
        }
        else if (_viewModel.FullscreenScrcpyWindowHandle != nint.Zero)
        {
            target = FullscreenPhonePreview;
        }

        _guideOverlay.SetTarget(target);
    }

    private void HandleClosed(object? sender, EventArgs args)
    {
        _guideOverlay?.Detach();
        _guideOverlay?.Close();
        _guideOverlay = null;
        UnregisterPreviewFullscreenHotKey();
        _windowSource?.RemoveHook(WindowMessageHook);
        _windowSource = null;
        Closing -= HandleClosing;
        _viewModel.VisualScreenshotRequested -= HandleVisualScreenshotRequested;
        _viewModel.ConfirmationRequested -= HandleConfirmationRequested;
        _viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
    }

    private async void HandleClosing(object? sender, CancelEventArgs args)
    {
        if (_shutdownComplete)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownInProgress)
        {
            return;
        }

        _shutdownInProgress = true;
        try
        {
            await _viewModel.DisposeAsync();
        }
        finally
        {
            _shutdownComplete = true;
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    private void HandleSourceInitialized(object? sender, EventArgs args)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
        ApplyTitleBarTheme();
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        // Both handles change whenever scrcpy starts, stops, or the preview moves
        // between docked and fullscreen, which is exactly when the guides need to
        // follow it to a different element.
        if (args.PropertyName is nameof(MainWindowViewModel.MainScrcpyWindowHandle)
            or nameof(MainWindowViewModel.FullscreenScrcpyWindowHandle))
        {
            UpdateGuideOverlayTarget();
            return;
        }

        if (args.PropertyName == nameof(MainWindowViewModel.IsPreviewFullscreen))
        {
            ApplyPreviewFullscreenMode(_viewModel.IsPreviewFullscreen);
            UpdateGuideOverlayTarget();
            return;
        }

        if (args.PropertyName == nameof(MainWindowViewModel.SelectedWindowMode))
        {
            ApplyWindowMode(_viewModel.SelectedWindowMode);
            return;
        }

        if (args.PropertyName == nameof(MainWindowViewModel.SelectedNightMode))
        {
            ApplyTitleBarTheme();
            PhonePreview.InvalidateVisual();
            FullscreenPhonePreview.InvalidateVisual();
        }
    }

    private void ApplyPreviewFullscreenMode(bool fullscreen)
    {
        if (fullscreen == _previewFullscreenApplied)
        {
            return;
        }

        if (fullscreen)
        {
            _savedWindowStyle = WindowStyle;
            _savedResizeMode = ResizeMode;
            _savedWindowState = WindowState;
            _savedTopmost = Topmost;
            _savedRestoreBounds = RestoreBounds;

            HeaderRow.Height = new GridLength(0);
            StatusRow.Height = new GridLength(0);
            AppHeader.Visibility = Visibility.Collapsed;
            StatusBar.Visibility = Visibility.Collapsed;

            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            FillCurrentMonitor();
            RegisterPreviewFullscreenHotKey();
            _previewFullscreenApplied = true;
            return;
        }

        UnregisterPreviewFullscreenHotKey();
        Topmost = _savedTopmost;
        WindowState = WindowState.Normal;
        WindowStyle = _savedWindowStyle;
        ResizeMode = _savedResizeMode;
        AppHeader.Visibility = Visibility.Visible;
        StatusBar.Visibility = Visibility.Visible;
        HeaderRow.Height = new GridLength(44);
        StatusRow.Height = new GridLength(24);

        if (_savedWindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
        else
        {
            Left = _savedRestoreBounds.Left;
            Top = _savedRestoreBounds.Top;
            Width = Math.Max(MinWidth, _savedRestoreBounds.Width);
            Height = Math.Max(MinHeight, _savedRestoreBounds.Height);
            WindowState = _savedWindowState;
        }

        _previewFullscreenApplied = false;
        ApplyTitleBarTheme();
    }

    private void FillCurrentMonitor()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        nint monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        MonitorInfo info = new() { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref info))
        {
            WindowState = WindowState.Maximized;
            return;
        }

        int width = info.Monitor.Right - info.Monitor.Left;
        int height = info.Monitor.Bottom - info.Monitor.Top;
        _ = SetWindowPos(
            handle,
            HwndTopmost,
            info.Monitor.Left,
            info.Monitor.Top,
            width,
            height,
            SwpFrameChanged | SwpShowWindow);
    }

    private void RegisterPreviewFullscreenHotKey()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            _ = RegisterHotKey(handle, PreviewFullscreenHotKeyId, 0, VkF11);
        }
    }

    private void UnregisterPreviewFullscreenHotKey()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle != nint.Zero)
        {
            _ = UnregisterHotKey(handle, PreviewFullscreenHotKeyId);
        }
    }

    private nint WindowMessageHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == PreviewFullscreenHotKeyId)
        {
            _viewModel.TogglePreviewFullscreenCommand.Execute(null);
            handled = true;
        }

        return nint.Zero;
    }

    private void ApplyWindowMode(string mode)
    {
        WindowState = WindowState.Normal;
        switch (mode)
        {
            case "Borderless window":
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.CanResize;
                break;
            case "Fullscreen":
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Maximized;
                break;
            case "Windowed fullscreen":
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Maximized;
                break;
            default:
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                break;
        }

        ApplyTitleBarTheme();
    }

    private void ApplyTitleBarTheme()
    {
        IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        int enabled = 1;
        _ = DwmSetWindowAttribute(
            handle,
            20,
            ref enabled,
            sizeof(int));

        (int Caption, int Text, int Border) colors = _viewModel.SelectedNightMode switch
        {
            NightDisplayMode.FullRed => (
                ToColorRef(6, 0, 0),
                ToColorRef(232, 0, 0),
                ToColorRef(62, 0, 0)),
            NightDisplayMode.Dim => (
                ToColorRef(3, 5, 9),
                ToColorRef(184, 190, 201),
                ToColorRef(27, 36, 52)),
            _ => (
                ToColorRef(7, 10, 17),
                ToColorRef(243, 246, 251),
                ToColorRef(38, 51, 75)),
        };
        _ = DwmSetWindowAttribute(
            handle,
            35,
            ref colors.Caption,
            sizeof(int));
        _ = DwmSetWindowAttribute(
            handle,
            36,
            ref colors.Text,
            sizeof(int));
        _ = DwmSetWindowAttribute(
            handle,
            34,
            ref colors.Border,
            sizeof(int));
    }

    private static int ToColorRef(byte red, byte green, byte blue) =>
        red | (green << 8) | (blue << 16);

    private async void PhonePreview_OnPreviewInput(object? sender, PreviewInputEventArgs args)
    {
        if (sender is not PhonePreviewControl preview)
        {
            return;
        }

        CoordinateMappingContext? context = preview.CreateMappingContext();
        if (context is null)
        {
            return;
        }

        CoordinateMappingResult mapping = new CoordinateMapper().MapToScrcpy(
            new PointD(args.Position.X, args.Position.Y),
            context);
        DpiScale dpi = VisualTreeHelper.GetDpi(preview);
        _viewModel.DebugOverlayText =
            $"Embedded DIP: {args.Position.X:0.0}, {args.Position.Y:0.0}{Environment.NewLine}" +
            $"Mapped client: {mapping.ScrcpyClientPointPixels.X:0}, {mapping.ScrcpyClientPointPixels.Y:0}{Environment.NewLine}" +
            $"Frame: {context.CapturedFrameSizePixels.Width:0} × {context.CapturedFrameSizePixels.Height:0}{Environment.NewLine}" +
            $"Client: {context.ScrcpyClientSizePixels.Width:0} × {context.ScrcpyClientSizePixels.Height:0}{Environment.NewLine}" +
            $"DPI: {dpi.DpiScaleX:0.00} × {dpi.DpiScaleY:0.00}{Environment.NewLine}" +
            $"Rotation: {(int)context.Rotation}°";

        if (args.Kind == PreviewInputKind.MouseDown &&
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) &&
            _viewModel.PreviewZoom > 1)
        {
            _zoomGestureActive = true;
        }

        bool zoomGesture = _zoomGestureActive;
        if (zoomGesture)
        {
            if (!mapping.IsInsidePreview)
            {
                if (args.Kind == PreviewInputKind.MouseUp)
                {
                    _lastZoomPanPoint = null;
                    _zoomGestureActive = false;
                }

                return;
            }

            Point normalized = new(
                mapping.CapturedPointPixels.X / Math.Max(1, context.CapturedFrameSizePixels.Width - 1),
                mapping.CapturedPointPixels.Y / Math.Max(1, context.CapturedFrameSizePixels.Height - 1));

            if (args.Kind == PreviewInputKind.MouseDown)
            {
                _lastZoomPanPoint = args.Position;
                _viewModel.CenterZoomAt(normalized);
            }
            else if (args.Kind == PreviewInputKind.MouseMove && _lastZoomPanPoint is { } last)
            {
                double deltaX = -(args.Position.X - last.X) / Math.Max(1, preview.ActualWidth);
                double deltaY = -(args.Position.Y - last.Y) / Math.Max(1, preview.ActualHeight);
                _viewModel.PanZoom(deltaX / _viewModel.PreviewZoom, deltaY / _viewModel.PreviewZoom);
                _lastZoomPanPoint = args.Position;
            }
            else if (args.Kind == PreviewInputKind.MouseUp)
            {
                _lastZoomPanPoint = null;
                _zoomGestureActive = false;
            }

            return;
        }

        try
        {
            await _inputCoordinator.HandleAsync(args, context);
        }
        catch (Exception exception)
        {
            _viewModel.StatusMessage = $"Phone input failed: {exception.Message}";
        }
    }

    private async void HandleVisualScreenshotRequested(
        object? sender,
        VisualScreenshotRequestEventArgs request)
    {
        try
        {
            string path = Path.Combine(request.Directory, request.FileName);
            Directory.CreateDirectory(request.Directory);
            await Dispatcher.InvokeAsync(
                () =>
                {
                    PhonePreviewControl target = _viewModel.IsPreviewFullscreen
                        ? FullscreenPhonePreview
                        : PhonePreview;
                    DpiScale dpi = VisualTreeHelper.GetDpi(target);
                    int width = Math.Max(1, (int)Math.Ceiling(target.ActualWidth * dpi.DpiScaleX));
                    int height = Math.Max(1, (int)Math.Ceiling(target.ActualHeight * dpi.DpiScaleY));
                    RenderTargetBitmap bitmap = new(
                        width,
                        height,
                        96 * dpi.DpiScaleX,
                        96 * dpi.DpiScaleY,
                        PixelFormats.Pbgra32);
                    bitmap.Render(target);
                    bitmap.Freeze();

                    PngBitmapEncoder encoder = new();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                    encoder.Save(stream);
                });
            request.Complete(path);
        }
        catch (Exception exception)
        {
            request.Fail(exception);
        }
    }

    private void HandleConfirmationRequested(
        object? sender,
        ConfirmationRequestEventArgs request)
    {
        MessageBoxResult result = MessageBox.Show(
            this,
            request.Message,
            request.Title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        request.Complete(result == MessageBoxResult.Yes);
    }

    private void BrowseAdb_OnClick(object sender, RoutedEventArgs args)
    {
        string? path = BrowseExecutable("Select adb.exe", "adb.exe");
        if (path is not null)
        {
            _viewModel.SetAdbPath(path);
        }
    }

    private void BrowseScrcpy_OnClick(object sender, RoutedEventArgs args)
    {
        string? path = BrowseExecutable("Select scrcpy.exe", "scrcpy.exe");
        if (path is not null)
        {
            _viewModel.SetScrcpyPath(path);
        }
    }

    private void BrowseSiril_OnClick(object sender, RoutedEventArgs args)
    {
        string? path = BrowseExecutable("Select siril-cli.exe", "siril-cli.exe");
        if (path is not null)
        {
            _viewModel.SirilPath = path;
        }
    }

    private void BrowseScreenshotFolder_OnClick(object sender, RoutedEventArgs args)
    {
        string? path = BrowseFolder("Select preview screenshot folder", _viewModel.ScreenshotFolder);
        if (path is not null)
        {
            _viewModel.SetScreenshotFolder(path);
        }
    }

    private void BrowseSessionFolder_OnClick(object sender, RoutedEventArgs args)
    {
        string? path = BrowseFolder("Select session data folder", _viewModel.SessionDataFolder);
        if (path is not null)
        {
            _viewModel.SetSessionDataFolder(path);
        }
    }

    private void BrowsePhoneCaptureFolder_OnClick(object sender, RoutedEventArgs args)
    {
        string? path = BrowseFolder("Select full-resolution phone capture folder", _viewModel.PhoneCaptureFolder);
        if (path is not null)
        {
            _viewModel.SetPhoneCaptureFolder(path);
        }
    }

    private void ClearHistoryDates_OnClick(object sender, RoutedEventArgs args)
    {
        _viewModel.HistoryFromDate = null;
        _viewModel.HistoryToDate = null;
        _viewModel.HistoryMinimumRating = 0;
    }

    private void OpenButtonContextMenu_OnClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button button || button.ContextMenu is null)
        {
            return;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private void Window_OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.F8)
        {
            _viewModel.AddFrameCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.F9)
        {
            _viewModel.HideAllOverlaysCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.F10)
        {
            _viewModel.CycleNightModeCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.F11)
        {
            _viewModel.TogglePreviewFullscreenCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.S &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                 Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _viewModel.TakeScreenshotCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && _viewModel.IsTimerFullscreen)
        {
            _viewModel.ToggleTimerFullscreenCommand.Execute(null);
            args.Handled = true;
        }
        else if (args.Key == Key.Escape && _viewModel.IsPreviewFullscreen)
        {
            _viewModel.TogglePreviewFullscreenCommand.Execute(null);
            args.Handled = true;
        }
    }

    private string? BrowseExecutable(string title, string fileName)
    {
        OpenFileDialog dialog = new()
        {
            Title = title,
            Filter = "Windows executable (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            FileName = fileName,
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private string? BrowseFolder(string title, string initialDirectory)
    {
        OpenFolderDialog dialog = new()
        {
            Title = title,
            InitialDirectory = Directory.Exists(initialDirectory)
                ? initialDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false,
        };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
