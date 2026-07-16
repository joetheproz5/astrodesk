using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
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

    public MainWindow(
        MainWindowViewModel viewModel,
        IPreviewInputCoordinator inputCoordinator)
    {
        _viewModel = viewModel;
        _inputCoordinator = inputCoordinator;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += HandleLoaded;
        Closed += HandleClosed;
        SourceInitialized += HandleSourceInitialized;
        _viewModel.PropertyChanged += HandleViewModelPropertyChanged;
        _viewModel.VisualScreenshotRequested += HandleVisualScreenshotRequested;
        _viewModel.ConfirmationRequested += HandleConfirmationRequested;
    }

    private async void HandleLoaded(object sender, RoutedEventArgs args)
    {
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

    private void HandleClosed(object? sender, EventArgs args)
    {
        _viewModel.VisualScreenshotRequested -= HandleVisualScreenshotRequested;
        _viewModel.ConfirmationRequested -= HandleConfirmationRequested;
        _viewModel.PropertyChanged -= HandleViewModelPropertyChanged;
    }

    private void HandleSourceInitialized(object? sender, EventArgs args)
    {
        ApplyTitleBarTheme();
    }

    private void HandleViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainWindowViewModel.SelectedNightMode))
        {
            return;
        }

        ApplyTitleBarTheme();
        PhonePreview.InvalidateVisual();
        FullscreenPhonePreview.InvalidateVisual();
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
                ToColorRef(4, 5, 6),
                ToColorRef(181, 186, 191),
                ToColorRef(28, 32, 36)),
            _ => (
                ToColorRef(0, 0, 0),
                ToColorRef(232, 237, 242),
                ToColorRef(40, 49, 61)),
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

    private void ClearHistoryDates_OnClick(object sender, RoutedEventArgs args)
    {
        _viewModel.HistoryFromDate = null;
        _viewModel.HistoryToDate = null;
        _viewModel.HistoryMinimumRating = 0;
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
}
