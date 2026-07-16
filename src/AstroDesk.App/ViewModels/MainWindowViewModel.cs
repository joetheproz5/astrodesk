using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AstroDesk.App.Configuration;
using AstroDesk.App.Services;
using AstroDesk.Capture.Geometry;
using AstroDesk.Capture.Histogram;
using AstroDesk.Core.Entities;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using AstroDesk.Device;
using AstroDesk.Device.Adb;
using AstroDesk.Device.Scrcpy;
using AstroDesk.Device.Toolbar;
using AstroDesk.Infrastructure.Providers;
using AstroDesk.Infrastructure.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AstroDesk.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPhonePreviewCoordinator _preview;
    private readonly IDeviceMonitor _deviceMonitor;
    private readonly IWeatherProvider _weatherProvider;
    private readonly IAstronomyProvider _astronomyProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly IExposureTimerService _exposureTimer;
    private readonly IDeviceToolbarController _toolbar;
    private readonly IAdbInputService _adbInput;
    private readonly DeviceToolOptions _deviceToolOptions;
    private readonly DeviceMonitorOptions _deviceMonitorOptions;
    private readonly AstroDeskAppOptions _appOptions;
    private readonly AppPaths _paths;
    private readonly ISessionAssetService _sessionAssets;
    private readonly INightModeService _nightMode;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherTimer _elapsedTimer;
    private ShootingSession? _activeSession;
    private WeatherConditions? _latestWeather;
    private AstronomyConditions? _latestAstronomy;
    private string? _lastScreenshotPath;
    private int _lastTimerCompletedFrames;
    private int _lastExperimentalTimerFrame;
    private bool _initialized;
    private bool _initializing;
    private bool _selectingLocation;
    private long _conditionsRequestVersion;
    private PreviewFrameSnapshot? _pendingPreviewFrame;
    private int _previewDispatchScheduled;

    public MainWindowViewModel(
        IServiceScopeFactory scopeFactory,
        IPhonePreviewCoordinator preview,
        IDeviceMonitor deviceMonitor,
        IWeatherProvider weatherProvider,
        IAstronomyProvider astronomyProvider,
        ILocationProvider locationProvider,
        IExposureTimerService exposureTimer,
        IDeviceToolbarController toolbar,
        IAdbInputService adbInput,
        DeviceToolOptions deviceToolOptions,
        DeviceMonitorOptions deviceMonitorOptions,
        AstroDeskAppOptions appOptions,
        AppPaths paths,
        ISessionAssetService sessionAssets,
        INightModeService nightMode,
        ILogger<MainWindowViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _preview = preview;
        _deviceMonitor = deviceMonitor;
        _weatherProvider = weatherProvider;
        _astronomyProvider = astronomyProvider;
        _locationProvider = locationProvider;
        _exposureTimer = exposureTimer;
        _toolbar = toolbar;
        _adbInput = adbInput;
        _deviceToolOptions = deviceToolOptions;
        _deviceMonitorOptions = deviceMonitorOptions;
        _appOptions = appOptions;
        _paths = paths;
        _sessionAssets = sessionAssets;
        _nightMode = nightMode;
        _logger = logger;

        ExposureSeconds = _appOptions.DefaultExposureSeconds;
        PlannedFrameCount = _appOptions.DefaultFrameCount;
        TimerFrameCount = _appOptions.DefaultFrameCount;
        TimerExposureSeconds = _appOptions.DefaultExposureSeconds;
        ScrcpyBitrateMbps = _appOptions.ScrcpyBitrateMbps;
        ScrcpyMaxResolution = _appOptions.ScrcpyMaxResolution;
        ScrcpyMaxFps = _appOptions.ScrcpyMaxFps;
        StatusRefreshSeconds = _appOptions.StatusRefreshSeconds;
        WeatherRefreshMinutes = _appOptions.WeatherRefreshMinutes;
        AutomaticallyLaunchScrcpy = _appOptions.AutomaticallyLaunchScrcpy;
        AutomaticallyReconnect = _appOptions.AutomaticallyReconnect;
        EnableExperimentalCaptureControls = _appOptions.EnableExperimentalCaptureControls;
        EnableNetworkConditions = _appOptions.EnableNetworkConditions;
        SelectedNightMode = _appOptions.NightMode;
        AdbPath = _deviceToolOptions.AdbExecutablePath ?? string.Empty;
        ScrcpyPath = _deviceToolOptions.ScrcpyExecutablePath ?? string.Empty;
        ScreenshotFolder = _paths.ScreenshotRoot;
        SessionDataFolder = _paths.SessionRoot;

        _elapsedTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _elapsedTimer.Tick += (_, _) => UpdateElapsedDisplay();
    }

    public event EventHandler<VisualScreenshotRequestEventArgs>? VisualScreenshotRequested;

    public event EventHandler<ConfirmationRequestEventArgs>? ConfirmationRequested;

    public ObservableCollection<LocationOption> Locations { get; } = [];

    public ObservableCollection<SessionHistoryItemViewModel> History { get; } = [];

    public ObservableCollection<AdbDevice> AvailableAdbDevices { get; } = [];

    public IReadOnlyList<SessionType> SessionTypes { get; } = Enum.GetValues<SessionType>();

    public IReadOnlyList<NightDisplayMode> NightModes { get; } = Enum.GetValues<NightDisplayMode>();

    public IReadOnlyList<string> PhoneLensOptions { get; } =
    [
        "Ultra-wide 0.6×",
        "Main 1×",
        "Telephoto 3×",
        "Telephoto 10×",
    ];

    public IReadOnlyList<int> RatingOptions { get; } = [1, 2, 3, 4, 5];

    public IReadOnlyList<int> HistoryRatingOptions { get; } = [0, 1, 2, 3, 4, 5];

    public IReadOnlyList<string> OverlayColorOptions { get; } =
    [
        "Orange red",
        "Red",
        "Amber",
        "White",
        "Green",
        "Cyan",
    ];

    public IReadOnlyList<string> HistorySortOptions { get; } =
    [
        "Newest first",
        "Oldest first",
        "Longest duration",
        "Most frames",
    ];

    public IReadOnlyList<string> HistoryTypeFilters { get; } =
        ["All types", .. Enum.GetValues<SessionType>().Select(type => SplitPascalCase(type.ToString()))];

    [ObservableProperty]
    private string _currentPage = "Dashboard";

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private string _connectionStatus = "Checking ADB…";

    [ObservableProperty]
    private bool _deviceConnected;

    [ObservableProperty]
    private AdbDevice? _selectedAdbDevice;

    [ObservableProperty]
    private string _deviceModel = "No phone connected";

    [ObservableProperty]
    private string _androidVersionText = "Unavailable";

    [ObservableProperty]
    private string _batteryText = "Unavailable";

    [ObservableProperty]
    private string _phoneTemperatureText = "Unavailable";

    [ObservableProperty]
    private string _storageText = "Unavailable";

    [ObservableProperty]
    private string _screenResolutionText = "Unavailable";

    [ObservableProperty]
    private string _orientationText = "Unavailable";

    [ObservableProperty]
    private string _usbStateText = "Unavailable";

    [ObservableProperty]
    private ImageSource? _previewImage;

    [ObservableProperty]
    private string _previewStatus = "Start scrcpy to begin the embedded phone preview";

    [ObservableProperty]
    private string? _previewError;

    [ObservableProperty]
    private double _previewFps;

    [ObservableProperty]
    private string _previewFrameSizeText = "No frames";

    [ObservableProperty]
    private Size _scrcpyClientSize;

    [ObservableProperty]
    private FrameRotation _previewRotation = FrameRotation.Rotate0;

    [ObservableProperty]
    private bool _pixelPerfect;

    [ObservableProperty]
    private bool _ruleOfThirds = true;

    [ObservableProperty]
    private bool _crosshair = true;

    [ObservableProperty]
    private bool _diagonalGuides;

    [ObservableProperty]
    private bool _safeArea;

    [ObservableProperty]
    private bool _horizonLine;

    [ObservableProperty]
    private bool _customRectangle;

    [ObservableProperty]
    private bool _circleOverlay;

    [ObservableProperty]
    private double _overlayOpacity = 0.72;

    [ObservableProperty]
    private double _overlayThickness = 1;

    [ObservableProperty]
    private Brush _overlayBrush = Brushes.OrangeRed;

    [ObservableProperty]
    private string _overlayColorName = "Orange red";

    [ObservableProperty]
    private double _customRectangleWidthPercent = 70;

    [ObservableProperty]
    private double _customRectangleHeightPercent = 70;

    [ObservableProperty]
    private double _previewZoom = 1;

    [ObservableProperty]
    private Point _zoomCenter = new(0.5, 0.5);

    [ObservableProperty]
    private bool _focusMagnifier;

    [ObservableProperty]
    private bool _isPreviewFrozen;

    [ObservableProperty]
    private bool _debugOverlay;

    [ObservableProperty]
    private string _debugOverlayText = string.Empty;

    [ObservableProperty]
    private bool _showHistogram;

    [ObservableProperty]
    private bool _histogramOverlay = true;

    [ObservableProperty]
    private bool _histogramFrozen;

    [ObservableProperty]
    private int _histogramUpdateMilliseconds = 500;

    [ObservableProperty]
    private HistogramResult? _histogram;

    [ObservableProperty]
    private string _highlightClippingText = "Highlights: Unavailable";

    [ObservableProperty]
    private string _shadowClippingText = "Shadows: Unavailable";

    [ObservableProperty]
    private bool _isPreviewFullscreen;

    [ObservableProperty]
    private string _targetName = "Milky Way";

    [ObservableProperty]
    private SessionType _selectedSessionType = SessionType.MilkyWay;

    [ObservableProperty]
    private LocationOption? _selectedLocation;

    [ObservableProperty]
    private double _latitude = 34.0106;

    [ObservableProperty]
    private double _longitude = 35.8258;

    [ObservableProperty]
    private string _cameraName = "Samsung Galaxy S23 Ultra";

    [ObservableProperty]
    private string _selectedPhoneLens = "Main 1×";

    [ObservableProperty]
    private int? _iso = 800;

    [ObservableProperty]
    private double _exposureSeconds = 20;

    [ObservableProperty]
    private double _delayBetweenFramesSeconds = 1;

    [ObservableProperty]
    private double _initialDelaySeconds = 2;

    [ObservableProperty]
    private int _plannedFrameCount = 120;

    [ObservableProperty]
    private string _whiteBalance = "4000 K";

    [ObservableProperty]
    private string _focusSetting = "Manual / infinity";

    [ObservableProperty]
    private bool _rawEnabled = true;

    [ObservableProperty]
    private string _sessionNotes = string.Empty;

    [ObservableProperty]
    private string _sessionStateText = "No active session";

    [ObservableProperty]
    private string _sessionElapsedText = "00:00:00";

    [ObservableProperty]
    private string _frameCountText = "0 / 120";

    [ObservableProperty]
    private string _frameProgressText = "0%";

    [ObservableProperty]
    private string _remainingFramesText = "120 remaining";

    [ObservableProperty]
    private string _estimatedRemainingText = "Estimated remaining: 42 min";

    [ObservableProperty]
    private string _integrationText = "Integration: 0 min";

    [ObservableProperty]
    private double _frameProgress;

    [ObservableProperty]
    private double _timerExposureSeconds = 20;

    [ObservableProperty]
    private double _timerDelaySeconds = 1;

    [ObservableProperty]
    private double _timerInitialDelaySeconds = 2;

    [ObservableProperty]
    private int _timerFrameCount = 120;

    [ObservableProperty]
    private bool _timerSound = true;

    [ObservableProperty]
    private string _timerStateText = "Idle";

    [ObservableProperty]
    private string _timerPhaseText = "Ready";

    [ObservableProperty]
    private string _timerCountdownText = "00:20.0";

    [ObservableProperty]
    private string _timerFrameText = "Frame 0 / 120";

    [ObservableProperty]
    private bool _isTimerFullscreen;

    [ObservableProperty]
    private bool _experimentalTimerCapture;

    [ObservableProperty]
    private int _experimentalShutterX;

    [ObservableProperty]
    private int _experimentalShutterY;

    [ObservableProperty]
    private string _temperatureText = "Unavailable";

    [ObservableProperty]
    private string _humidityText = "Unavailable";

    [ObservableProperty]
    private string _windText = "Unavailable";

    [ObservableProperty]
    private string _cloudCoverText = "Unavailable";

    [ObservableProperty]
    private string _visibilityText = "Unavailable";

    [ObservableProperty]
    private string _dewPointText = "Unavailable";

    [ObservableProperty]
    private string _dewRiskText = "Unavailable";

    [ObservableProperty]
    private string _sunsetText = "Unavailable";

    [ObservableProperty]
    private string _astronomicalDuskText = "Unavailable";

    [ObservableProperty]
    private string _sunriseText = "Unavailable";

    [ObservableProperty]
    private string _astronomicalDawnText = "Unavailable";

    [ObservableProperty]
    private string _moonPhaseText = "Unavailable";

    [ObservableProperty]
    private string _moonIlluminationText = "Unavailable";

    [ObservableProperty]
    private string _moonriseText = "Unavailable";

    [ObservableProperty]
    private string _moonsetText = "Unavailable";

    [ObservableProperty]
    private string _moonAltitudeText = "Unavailable";

    [ObservableProperty]
    private string _darkSkyWindowText = "Unavailable";

    [ObservableProperty]
    private string _conditionsUpdatedText = "Not updated";

    [ObservableProperty]
    private string _locationSearchText = string.Empty;

    [ObservableProperty]
    private string _historySearchText = string.Empty;

    [ObservableProperty]
    private string _historyTargetFilter = string.Empty;

    [ObservableProperty]
    private string _historyLocationFilter = string.Empty;

    [ObservableProperty]
    private string _selectedHistoryTypeFilter = "All types";

    [ObservableProperty]
    private int _historyMinimumRating;

    [ObservableProperty]
    private DateTime? _historyFromDate;

    [ObservableProperty]
    private DateTime? _historyToDate;

    [ObservableProperty]
    private string _selectedHistorySort = "Newest first";

    [ObservableProperty]
    private SessionHistoryItemViewModel? _selectedHistorySession;

    [ObservableProperty]
    private int? _historyEditRating;

    [ObservableProperty]
    private string _historyEditProblems = string.Empty;

    [ObservableProperty]
    private string _historyStatusText = "No sessions loaded";

    [ObservableProperty]
    private string _adbPath = string.Empty;

    [ObservableProperty]
    private string _scrcpyPath = string.Empty;

    [ObservableProperty]
    private int _scrcpyBitrateMbps = 12;

    [ObservableProperty]
    private int _scrcpyMaxResolution = 1920;

    [ObservableProperty]
    private int _scrcpyMaxFps = 60;

    [ObservableProperty]
    private int _statusRefreshSeconds = 20;

    [ObservableProperty]
    private int _weatherRefreshMinutes = 15;

    [ObservableProperty]
    private string _defaultLocationName = "Faraya";

    [ObservableProperty]
    private double _defaultExposureSeconds = 20;

    [ObservableProperty]
    private int _defaultFrameCount = 120;

    [ObservableProperty]
    private string _screenshotFolder = string.Empty;

    [ObservableProperty]
    private string _sessionDataFolder = string.Empty;

    [ObservableProperty]
    private bool _automaticallyLaunchScrcpy;

    [ObservableProperty]
    private bool _automaticallyReconnect = true;

    [ObservableProperty]
    private bool _enableExperimentalCaptureControls;

    [ObservableProperty]
    private bool _enableNetworkConditions;

    [ObservableProperty]
    private bool _includeOverlaysInScreenshot;

    [ObservableProperty]
    private NightDisplayMode _selectedNightMode = NightDisplayMode.NormalDark;

    [ObservableProperty]
    private string _settingsStatusText = "Settings are stored locally.";

    public bool IsDashboard => CurrentPage == "Dashboard";

    public bool IsHistory => CurrentPage == "History";

    public bool IsSettings => CurrentPage == "Settings";

    public bool HasActiveSession => _activeSession is not null;

    public bool IsSessionPaused => _activeSession?.Status == SessionStatus.Paused;

    public bool IsSessionActive => _activeSession?.Status == SessionStatus.Active;

    public bool HasSelectedHistorySession => SelectedHistorySession is not null;

    public bool HasLastScreenshot => _lastScreenshotPath is not null;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || _initializing)
        {
            return;
        }

        _initializing = true;
        bool subscribed = false;
        try
        {
            _paths.EnsureCreated();
            _nightMode.Apply(SelectedNightMode);
            _preview.DisplayMode = SelectedNightMode;

            _preview.FrameReady += HandlePreviewFrame;
            _preview.HistogramReady += HandleHistogram;
            _preview.StatusChanged += HandlePreviewStatus;
            _deviceMonitor.SnapshotChanged += HandleDeviceSnapshot;
            _exposureTimer.Tick += HandleTimerTick;
            subscribed = true;

            await LoadSettingsAsync(cancellationToken).ConfigureAwait(true);
            await LoadLocationsAsync(cancellationToken).ConfigureAwait(true);
            await RecoverSessionAsync(cancellationToken).ConfigureAwait(true);
            await RefreshHistoryAsync(cancellationToken).ConfigureAwait(true);
            await RefreshConditionsAsync(cancellationToken).ConfigureAwait(true);
            await _deviceMonitor.PollOnceAsync(cancellationToken).ConfigureAwait(true);
            await _deviceMonitor.StartAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            _elapsedTimer.Start();
            _ = RunConditionsRefreshLoopAsync(_lifetimeCancellation.Token);

            if (AutomaticallyLaunchScrcpy)
            {
                await StartPreviewAsync().ConfigureAwait(true);
            }

            _initialized = true;
        }
        catch
        {
            if (subscribed)
            {
                _preview.FrameReady -= HandlePreviewFrame;
                _preview.HistogramReady -= HandleHistogram;
                _preview.StatusChanged -= HandlePreviewStatus;
                _deviceMonitor.SnapshotChanged -= HandleDeviceSnapshot;
                _exposureTimer.Tick -= HandleTimerTick;
            }

            _elapsedTimer.Stop();
            throw;
        }
        finally
        {
            _initializing = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetimeCancellation.Cancel();
        _elapsedTimer.Stop();
        _preview.FrameReady -= HandlePreviewFrame;
        _preview.HistogramReady -= HandleHistogram;
        _preview.StatusChanged -= HandlePreviewStatus;
        _deviceMonitor.SnapshotChanged -= HandleDeviceSnapshot;
        _exposureTimer.Tick -= HandleTimerTick;

        await _exposureTimer.StopAsync().ConfigureAwait(false);
        await _deviceMonitor.StopAsync().ConfigureAwait(false);
        await _preview.StopAsync().ConfigureAwait(false);
        _lifetimeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    public void CenterZoomAt(Point normalizedPoint)
    {
        ZoomCenter = new Point(
            Math.Clamp(normalizedPoint.X, 0, 1),
            Math.Clamp(normalizedPoint.Y, 0, 1));
    }

    public void PanZoom(double deltaX, double deltaY)
    {
        if (PreviewZoom <= 1)
        {
            return;
        }

        double half = 0.5 / PreviewZoom;
        ZoomCenter = new Point(
            Math.Clamp(ZoomCenter.X + deltaX, half, 1 - half),
            Math.Clamp(ZoomCenter.Y + deltaY, half, 1 - half));
    }

    public void SetAdbPath(string path) => AdbPath = path;

    public void SetScrcpyPath(string path) => ScrcpyPath = path;

    public void SetScreenshotFolder(string path) => ScreenshotFolder = path;

    public void SetSessionDataFolder(string path) => SessionDataFolder = path;

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = "Dashboard";

    [RelayCommand]
    private void ShowHistory() => CurrentPage = "History";

    [RelayCommand]
    private void ShowSettings() => CurrentPage = "Settings";

    [RelayCommand]
    private async Task StartPreviewAsync()
    {
        try
        {
            PreviewError = null;
            DeviceMonitorSnapshot? monitor = _deviceMonitor.LastSnapshot;
            if (monitor?.Connection.State == AdbConnectionState.MultipleDevices &&
                SelectedAdbDevice is null)
            {
                StatusMessage = "Select the intended Android device before starting scrcpy.";
                return;
            }

            string? serial = monitor?.Connection.SelectedDevice?.Serial
                             ?? SelectedAdbDevice?.Serial;
            ScrcpySession session = await _preview.StartAsync(
                new ScrcpyLaunchOptions
                {
                    ExecutablePath = string.IsNullOrWhiteSpace(ScrcpyPath) ? null : ScrcpyPath,
                    DeviceSerial = serial,
                    VideoBitRateMbps = Math.Clamp(ScrcpyBitrateMbps, 1, 100),
                    MaxSize = ScrcpyMaxResolution > 0 ? ScrcpyMaxResolution : null,
                    MaxFps = ScrcpyMaxFps > 0 ? ScrcpyMaxFps : null,
                    KeepAwake = true,
                },
                _lifetimeCancellation.Token).ConfigureAwait(true);
            StatusMessage = $"scrcpy started ({session.WindowTitle}).";
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not start scrcpy.");
            PreviewError = FriendlyToolError(exception, "scrcpy");
            PreviewStatus = "Embedded preview unavailable";
            StatusMessage = PreviewError;
        }
    }

    [RelayCommand]
    private async Task StopPreviewAsync()
    {
        try
        {
            await _preview.StopAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            PreviewImage = null;
            PreviewError = null;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not stop scrcpy cleanly.");
            StatusMessage = exception.Message;
        }
    }

    [RelayCommand]
    private async Task ReconnectPreviewAsync()
    {
        try
        {
            PreviewError = null;
            _ = await _preview.ReconnectAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not reconnect scrcpy.");
            PreviewError = FriendlyToolError(exception, "scrcpy");
        }
    }

    [RelayCommand]
    private Task BackAsync() => ExecuteToolbarAsync(DeviceToolbarAction.Back);

    [RelayCommand]
    private Task HomeAsync() => ExecuteToolbarAsync(DeviceToolbarAction.Home);

    [RelayCommand]
    private Task RecentAppsAsync() => ExecuteToolbarAsync(DeviceToolbarAction.RecentApps);

    [RelayCommand]
    private Task VolumeUpAsync() => ExecuteToolbarAsync(DeviceToolbarAction.VolumeUp);

    [RelayCommand]
    private Task VolumeDownAsync() => ExecuteToolbarAsync(DeviceToolbarAction.VolumeDown);

    [RelayCommand]
    private Task PowerAsync() => ExecuteToolbarAsync(DeviceToolbarAction.Power);

    [RelayCommand]
    private Task RotatePhoneAsync() => ExecuteToolbarAsync(DeviceToolbarAction.RotatePhone);

    [RelayCommand]
    private Task KeepPhoneAwakeAsync() => ExecuteToolbarAsync(DeviceToolbarAction.KeepAwake);

    [RelayCommand]
    private Task TurnPhoneScreenOffAsync() =>
        ExecuteToolbarAsync(DeviceToolbarAction.ScreenOffWhileMirroring);

    [RelayCommand]
    private async Task PasteClipboardAsync()
    {
        string text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        if (text.Length == 0)
        {
            StatusMessage = "The Windows clipboard does not contain text.";
            return;
        }

        await ExecuteToolbarAsync(DeviceToolbarAction.ClipboardPaste, text).ConfigureAwait(true);
    }

    [RelayCommand]
    private void TogglePreviewFullscreen() => IsPreviewFullscreen = !IsPreviewFullscreen;

    [RelayCommand]
    private void ToggleFreeze()
    {
        IsPreviewFrozen = !IsPreviewFrozen;
        _preview.IsFrozen = IsPreviewFrozen;
    }

    [RelayCommand]
    private void ToggleHistogramFreeze()
    {
        HistogramFrozen = !HistogramFrozen;
    }

    [RelayCommand]
    private void HideAllOverlays()
    {
        RuleOfThirds = false;
        Crosshair = false;
        DiagonalGuides = false;
        SafeArea = false;
        HorizonLine = false;
        CustomRectangle = false;
        CircleOverlay = false;
    }

    [RelayCommand]
    private void Zoom1()
    {
        PreviewZoom = 1;
        ZoomCenter = new Point(0.5, 0.5);
    }

    [RelayCommand]
    private void Zoom2() => PreviewZoom = 2;

    [RelayCommand]
    private void Zoom4() => PreviewZoom = 4;

    [RelayCommand]
    private void Zoom8() => PreviewZoom = 8;

    [RelayCommand]
    private void CycleNightMode()
    {
        SelectedNightMode = SelectedNightMode switch
        {
            NightDisplayMode.NormalDark => NightDisplayMode.Dim,
            NightDisplayMode.Dim => NightDisplayMode.FullRed,
            _ => NightDisplayMode.NormalDark,
        };
    }

    [RelayCommand]
    private async Task TakeScreenshotAsync()
    {
        try
        {
            string directory = _activeSession is null
                ? ScreenshotFolder
                : _sessionAssets.GetOrCreateScreenshotFolder(_activeSession);
            string fileName = $"preview-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.png";
            string path;

            EventHandler<VisualScreenshotRequestEventArgs>? screenshotHandler =
                VisualScreenshotRequested;
            bool canRenderVisibleOverlaySurface =
                IncludeOverlaysInScreenshot &&
                IsDashboard &&
                screenshotHandler is not null;
            if (canRenderVisibleOverlaySurface)
            {
                VisualScreenshotRequestEventArgs request = new(directory, fileName);
                screenshotHandler!(this, request);
                path = await request.Completion.ConfigureAwait(true);
            }
            else
            {
                path = await _preview
                    .SaveCurrentFrameAsync(
                        directory,
                        fileName,
                        _lifetimeCancellation.Token)
                    .ConfigureAwait(true);
            }

            _lastScreenshotPath = path;
            OnPropertyChanged(nameof(HasLastScreenshot));
            await RegisterScreenshotAsync(path, canRenderVisibleOverlaySurface).ConfigureAwait(true);
            StatusMessage =
                $"Preview screenshot saved: {path}. It is not a full-resolution phone photograph.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not save a preview screenshot.");
            StatusMessage = $"Screenshot failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private void CopyPreviewToClipboard()
    {
        if (PreviewImage is BitmapSource bitmap)
        {
            Clipboard.SetImage(bitmap);
            StatusMessage = "Current embedded preview copied to the Windows clipboard.";
        }
        else
        {
            StatusMessage = "No embedded preview frame is available.";
        }
    }

    [RelayCommand]
    private void OpenScreenshotFolder()
    {
        string folder = _lastScreenshotPath is null
            ? ScreenshotFolder
            : Path.GetDirectoryName(_lastScreenshotPath) ?? ScreenshotFolder;
        Directory.CreateDirectory(folder);
        _ = Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (_activeSession is not null)
        {
            StatusMessage = "End the current session before starting another.";
            return;
        }

        try
        {
            ValidateSessionInputs();
            DeviceMonitorSnapshot? device = _deviceMonitor.LastSnapshot;
            CreateSessionRequest request = new(
                TargetName,
                SelectedSessionType,
                SelectedLocation?.Name ?? "Manual location",
                Latitude,
                Longitude,
                TimeSpan.FromSeconds(ExposureSeconds),
                PlannedFrameCount,
                TimeSpan.FromSeconds(DelayBetweenFramesSeconds),
                TimeSpan.FromSeconds(InitialDelaySeconds),
                CameraName,
                SelectedPhoneLens,
                Iso,
                WhiteBalance,
                FocusSetting,
                RawEnabled,
                device?.PhoneStatus?.BatteryPercentage,
                device?.PhoneStatus?.InternalStorage?.FreeBytes);

            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISessionService service = scope.ServiceProvider.GetRequiredService<ISessionService>();
            IShootingSessionRepository repository =
                scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
            ShootingSession created = await service
                .CreateAsync(request, startImmediately: true, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            _activeSession = created;
            UpdateSessionDisplay();
            ShootingSession session = await repository
                .GetByIdAsync(created.Id, _lifetimeCancellation.Token)
                .ConfigureAwait(true)
                ?? created;
            _activeSession = session;
            UpdateSessionDisplay();

            DateTimeOffset snapshotTime = DateTimeOffset.UtcNow;
            if (_latestWeather is not null)
            {
                session.SetWeatherSnapshot(
                    new SessionWeatherSnapshot(_latestWeather, snapshotTime),
                    snapshotTime);
            }

            if (_latestAstronomy is not null)
            {
                session.SetAstronomySnapshot(
                    new SessionAstronomySnapshot(_latestAstronomy, snapshotTime),
                    snapshotTime);
            }

            if (!string.IsNullOrWhiteSpace(SessionNotes))
            {
                session.AddNote(SessionNotes, SessionNoteKind.General, snapshotTime);
            }

            await repository.UpdateAsync(session, _lifetimeCancellation.Token).ConfigureAwait(true);
            SessionNotes = string.Empty;
            UpdateSessionDisplay();
            await RefreshHistoryAsync().ConfigureAwait(true);
            StatusMessage = $"Session started for {session.TargetName}.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not start a shooting session.");
            StatusMessage = _activeSession is null
                ? $"Session could not start: {exception.Message}"
                : $"Session started, but its initial conditions or note could not be saved: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PauseSessionAsync()
    {
        await MutateActiveSessionAsync(
            static (service, id, token) => service.PauseAsync(id, token),
            "Session paused.").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResumeSessionAsync()
    {
        await MutateActiveSessionAsync(
            static (service, id, token) => service.ResumeAsync(id, token),
            "Session resumed.").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task EndSessionAsync()
    {
        if (_activeSession is null)
        {
            StatusMessage = "There is no active session to end.";
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(SessionNotes))
            {
                await PersistSessionNoteAsync().ConfigureAwait(true);
            }

            DeviceMonitorSnapshot? device = _deviceMonitor.LastSnapshot;
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISessionService service = scope.ServiceProvider.GetRequiredService<ISessionService>();
            ShootingSession completed = await service.EndAsync(
                    _activeSession.Id,
                    device?.PhoneStatus?.BatteryPercentage,
                    device?.PhoneStatus?.InternalStorage?.FreeBytes,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            IShootingSessionRepository repository =
                scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
            ShootingSession exportSession = await repository
                .GetByIdAsync(completed.Id, _lifetimeCancellation.Token)
                .ConfigureAwait(true)
                ?? completed;
            _activeSession = null;
            UpdateSessionDisplay();
            await RefreshHistoryAsync().ConfigureAwait(true);

            try
            {
                await _sessionAssets
                    .WritePortableFilesAsync(exportSession, _lifetimeCancellation.Token)
                    .ConfigureAwait(true);
                StatusMessage =
                    $"Session ended. Portable files written to {_sessionAssets.GetOrCreateSessionFolder(exportSession)}.";
            }
            catch (Exception exportException)
            {
                _logger.LogWarning(
                    exportException,
                    "Session ended in SQLite, but portable files could not be written.");
                StatusMessage =
                    $"Session ended and is safe in SQLite, but portable export failed: {exportException.Message}";
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not end the shooting session.");
            StatusMessage = $"Session could not end: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task AddFrameAsync()
    {
        await MutateActiveSessionAsync(
            static (service, id, token) => service.AddFrameAsync(id, 1, token),
            "Frame recorded.").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveFrameAsync()
    {
        await MutateActiveSessionAsync(
            static (service, id, token) => service.RemoveFrameAsync(id, 1, token),
            "Frame correction applied.").ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task SaveSessionNoteAsync()
    {
        if (_activeSession is null || string.IsNullOrWhiteSpace(SessionNotes))
        {
            StatusMessage = _activeSession is null
                ? "Start a session before saving notes."
                : "Enter a note first.";
            return;
        }

        try
        {
            await PersistSessionNoteAsync().ConfigureAwait(true);
            StatusMessage = "Session note saved.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not save the session note.");
            StatusMessage = $"Note could not be saved: {exception.Message}";
        }
    }

    private async Task PersistSessionNoteAsync()
    {
        ShootingSession active = _activeSession
                                 ?? throw new InvalidOperationException("There is no active session.");
        string note = string.IsNullOrWhiteSpace(SessionNotes)
            ? throw new InvalidOperationException("Enter a note first.")
            : SessionNotes;
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IShootingSessionRepository repository =
            scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
        ShootingSession session = await repository
            .GetByIdAsync(active.Id, _lifetimeCancellation.Token)
            .ConfigureAwait(true)
            ?? throw new InvalidOperationException("The active session could not be reloaded.");
        session.AddNote(note, SessionNoteKind.General, DateTimeOffset.UtcNow);
        await repository.UpdateAsync(session, _lifetimeCancellation.Token).ConfigureAwait(true);
        _activeSession = session;
        SessionNotes = string.Empty;
        UpdateSessionDisplay();
    }

    [RelayCommand]
    private async Task StartTimerAsync()
    {
        try
        {
            _lastTimerCompletedFrames = 0;
            _lastExperimentalTimerFrame = 0;
            await _exposureTimer.StartAsync(
                new ExposureTimerOptions(
                    TimeSpan.FromSeconds(TimerExposureSeconds),
                    TimeSpan.FromSeconds(TimerDelaySeconds),
                    TimerFrameCount,
                    TimeSpan.FromSeconds(TimerInitialDelaySeconds)),
                _lifetimeCancellation.Token).ConfigureAwait(true);
            TimerStateText = "Running";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Timer could not start: {exception.Message}";
        }
    }

    [RelayCommand]
    private void PauseTimer()
    {
        _exposureTimer.Pause();
        TimerStateText = "Paused";
    }

    [RelayCommand]
    private void ResumeTimer()
    {
        _exposureTimer.Resume();
        TimerStateText = "Running";
    }

    [RelayCommand]
    private async Task StopTimerAsync()
    {
        await _exposureTimer.StopAsync().ConfigureAwait(true);
        TimerStateText = "Idle";
        TimerPhaseText = "Ready";
    }

    [RelayCommand]
    private void ToggleTimerFullscreen() => IsTimerFullscreen = !IsTimerFullscreen;

    [RelayCommand]
    private async Task ExperimentalShutterTapAsync()
    {
        if (!EnableExperimentalCaptureControls)
        {
            StatusMessage = "Experimental capture controls are disabled in Settings.";
            return;
        }

        string? serial = _deviceMonitor.LastSnapshot?.Connection.SelectedDevice?.Serial;
        if (string.IsNullOrWhiteSpace(serial))
        {
            StatusMessage = "Select an authorized ADB device first.";
            return;
        }

        if (ExperimentalShutterX < 0 || ExperimentalShutterY < 0)
        {
            StatusMessage = "Experimental shutter coordinates must be non-negative.";
            return;
        }

        try
        {
            await _adbInput
                .TapAsync(
                    serial,
                    ExperimentalShutterX,
                    ExperimentalShutterY,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            StatusMessage = "Experimental ADB shutter tap sent.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Experimental shutter tap failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshConditionsAsync(CancellationToken cancellationToken = default)
    {
        long requestVersion = Interlocked.Increment(ref _conditionsRequestVersion);
        try
        {
            GeoCoordinate coordinate = new(Latitude, Longitude);
            Task<WeatherConditions?> weatherTask = EnableNetworkConditions
                ? _weatherProvider.GetCurrentAsync(coordinate, cancellationToken)
                : Task.FromResult<WeatherConditions?>(null);
            Task<AstronomyConditions?> astronomyTask = _astronomyProvider.GetConditionsAsync(
                coordinate,
                DateOnly.FromDateTime(DateTime.Now),
                cancellationToken);
            await Task.WhenAll(weatherTask, astronomyTask).ConfigureAwait(true);

            if (requestVersion != Volatile.Read(ref _conditionsRequestVersion))
            {
                return;
            }

            _latestWeather = await weatherTask.ConfigureAwait(true);
            _latestAstronomy = await astronomyTask.ConfigureAwait(true);
            UpdateConditionsDisplay();
            ConditionsUpdatedText = $"Updated {DateTime.Now:t}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Conditions refresh failed.");
            StatusMessage = $"Conditions unavailable: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SearchLocationsAsync()
    {
        if (!EnableNetworkConditions)
        {
            StatusMessage =
                "Online location search is disabled for privacy. Enable network weather/location services in Settings first.";
            return;
        }

        IReadOnlyList<LocationSearchResult> results = await _locationProvider
            .SearchAsync(LocationSearchText, _lifetimeCancellation.Token)
            .ConfigureAwait(true);
        if (results.Count == 0)
        {
            StatusMessage = "No matching locations were found, or location search is unavailable.";
            return;
        }

        foreach (LocationSearchResult result in results)
        {
            AddLocationIfMissing(LocationOption.FromSearchResult(result));
        }

        SelectedLocation = LocationOption.FromSearchResult(results[0]);
        StatusMessage = $"{results.Count} location result(s) added.";
    }

    [RelayCommand]
    private async Task RefreshHistoryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IShootingSessionRepository repository =
                scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
            SessionType? type = ParseHistoryType(SelectedHistoryTypeFilter);
            DateTimeOffset? from = HistoryFromDate is { } fromDate
                ? new DateTimeOffset(fromDate.Date, TimeZoneInfo.Local.GetUtcOffset(fromDate.Date))
                : null;
            DateTimeOffset? to = HistoryToDate is { } toDate
                ? new DateTimeOffset(toDate.Date.AddDays(1).AddTicks(-1), TimeZoneInfo.Local.GetUtcOffset(toDate.Date))
                : null;
            IReadOnlyList<ShootingSession> sessions = await repository.SearchAsync(
                    new SessionSearchCriteria(
                        string.IsNullOrWhiteSpace(HistorySearchText) ? null : HistorySearchText,
                        from,
                        to,
                        string.IsNullOrWhiteSpace(HistoryTargetFilter) ? null : HistoryTargetFilter,
                        string.IsNullOrWhiteSpace(HistoryLocationFilter) ? null : HistoryLocationFilter,
                        type,
                        HistoryMinimumRating > 0 ? HistoryMinimumRating : null,
                        0,
                        500),
                    cancellationToken)
                .ConfigureAwait(true);

            IEnumerable<ShootingSession> sorted = SelectedHistorySort switch
            {
                "Oldest first" => sessions.OrderBy(session => session.StartTime ?? session.CreatedAt),
                "Longest duration" => sessions.OrderByDescending(session => session.ActualSessionDuration),
                "Most frames" => sessions.OrderByDescending(session => session.FrameCount),
                _ => sessions.OrderByDescending(session => session.StartTime ?? session.CreatedAt),
            };

            History.Clear();
            foreach (ShootingSession session in sorted)
            {
                History.Add(new SessionHistoryItemViewModel(session));
            }

            SelectedHistorySession = History.FirstOrDefault();
            HistoryStatusText = History.Count == 0
                ? "No sessions match the current filters."
                : $"{History.Count} session(s)";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not load session history.");
            HistoryStatusText = $"History unavailable: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task DuplicateSelectedSessionAsync()
    {
        if (SelectedHistorySession is null)
        {
            return;
        }

        ShootingSession source = SelectedHistorySession.Session;
        try
        {
            CreateSessionRequest request = new(
                source.TargetName,
                source.SessionType,
                source.LocationName,
                source.Latitude,
                source.Longitude,
                source.ExposureTime,
                source.PlannedFrameCount,
                source.DelayBetweenFrames,
                source.InitialDelay,
                source.Camera,
                source.SelectedPhoneLens,
                source.Iso,
                source.WhiteBalance,
                source.FocusSetting,
                source.RawEnabled);
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISessionService service = scope.ServiceProvider.GetRequiredService<ISessionService>();
            _ = await service.CreateAsync(request, false, _lifetimeCancellation.Token).ConfigureAwait(true);
            await RefreshHistoryAsync().ConfigureAwait(true);
            StatusMessage = "Session duplicated as a new plan.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Session could not be duplicated: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task StartSelectedPlanAsync()
    {
        if (SelectedHistorySession is null)
        {
            return;
        }

        if (_activeSession is not null)
        {
            StatusMessage = "End the current session before starting a saved plan.";
            return;
        }

        if (SelectedHistorySession.Session.Status != SessionStatus.Planned)
        {
            StatusMessage = "Only a planned session can be started from history.";
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISessionService service = scope.ServiceProvider.GetRequiredService<ISessionService>();
            _activeSession = await service
                .StartAsync(SelectedHistorySession.Id, _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            ApplySessionToEditor(_activeSession);
            UpdateSessionDisplay();
            CurrentPage = "Dashboard";
            StatusMessage = $"Started saved plan for {_activeSession.TargetName}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Saved plan could not start: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveHistoryEditsAsync()
    {
        if (SelectedHistorySession is null)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IShootingSessionRepository repository =
                scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
            ShootingSession session = await repository
                .GetByIdAsync(SelectedHistorySession.Id, _lifetimeCancellation.Token)
                .ConfigureAwait(true)
                ?? throw new KeyNotFoundException("Session not found.");
            session.SetOutcome(
                string.IsNullOrWhiteSpace(HistoryEditProblems) ? null : HistoryEditProblems,
                HistoryEditRating,
                DateTimeOffset.UtcNow);
            await repository.UpdateAsync(session, _lifetimeCancellation.Token).ConfigureAwait(true);
            await RefreshHistoryAsync().ConfigureAwait(true);
            StatusMessage = "Session rating and problem notes updated.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Session edits could not be saved: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedSessionAsync()
    {
        if (SelectedHistorySession is null)
        {
            return;
        }

        if (_activeSession?.Id == SelectedHistorySession.Id)
        {
            StatusMessage = "The active session cannot be deleted. End it first.";
            return;
        }

        ConfirmationRequestEventArgs request = new(
            "Delete shooting session",
            $"Delete the session “{SelectedHistorySession.Target}”? The database record will be removed; exported files are left in place for recovery.");
        if (ConfirmationRequested is null)
        {
            return;
        }

        ConfirmationRequested.Invoke(this, request);
        if (!await request.Completion.ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IShootingSessionRepository repository =
                scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
            ShootingSession session = await repository
                .GetByIdAsync(SelectedHistorySession.Id, _lifetimeCancellation.Token)
                .ConfigureAwait(true)
                ?? throw new KeyNotFoundException("Session not found.");
            await repository.DeleteAsync(session, _lifetimeCancellation.Token).ConfigureAwait(true);
            await RefreshHistoryAsync().ConfigureAwait(true);
            StatusMessage = "Session deleted from the local database.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Session could not be deleted: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ExportSelectedJsonAsync()
    {
        if (SelectedHistorySession is null)
        {
            return;
        }

        string path = await _sessionAssets
            .ExportJsonAsync(SelectedHistorySession.Session, _lifetimeCancellation.Token)
            .ConfigureAwait(true);
        StatusMessage = $"Session JSON exported to {path}.";
    }

    [RelayCommand]
    private async Task ExportSelectedMarkdownAsync()
    {
        if (SelectedHistorySession is null)
        {
            return;
        }

        string path = await _sessionAssets
            .ExportMarkdownAsync(SelectedHistorySession.Session, _lifetimeCancellation.Token)
            .ConfigureAwait(true);
        StatusMessage = $"Session Markdown exported to {path}.";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            ValidateSettings();
            _paths.SetScreenshotRoot(ScreenshotFolder);
            _paths.SetSessionRoot(SessionDataFolder);
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISettingsService settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            await settings.SetAsync("Device.AdbPath", AdbPath, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Device.ScrcpyPath", ScrcpyPath, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Scrcpy.BitrateMbps", ScrcpyBitrateMbps, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Scrcpy.MaxResolution", ScrcpyMaxResolution, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Scrcpy.MaxFps", ScrcpyMaxFps, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Status.RefreshSeconds", StatusRefreshSeconds, "Status", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Weather.RefreshMinutes", WeatherRefreshMinutes, "Conditions", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Session.DefaultLocation", DefaultLocationName, "Session", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Session.DefaultExposureSeconds", DefaultExposureSeconds, "Session", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Session.DefaultFrameCount", DefaultFrameCount, "Session", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Files.ScreenshotFolder", ScreenshotFolder, "Files", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Files.SessionDataFolder", SessionDataFolder, "Files", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Startup.AutoLaunchScrcpy", AutomaticallyLaunchScrcpy, "Startup", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Startup.AutoReconnect", AutomaticallyReconnect, "Startup", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Experimental.CaptureControls", EnableExperimentalCaptureControls, "Experimental", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Experimental.ShutterX", ExperimentalShutterX, "Experimental", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Experimental.ShutterY", ExperimentalShutterY, "Experimental", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Privacy.EnableNetworkConditions", EnableNetworkConditions, "Privacy", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.RuleOfThirds", RuleOfThirds, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.Crosshair", Crosshair, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.DiagonalGuides", DiagonalGuides, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.SafeArea", SafeArea, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.HorizonLine", HorizonLine, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.CustomRectangle", CustomRectangle, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.Circle", CircleOverlay, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.Opacity", OverlayOpacity, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.Thickness", OverlayThickness, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.Color", OverlayColorName, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.RectangleWidthPercent", CustomRectangleWidthPercent, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.RectangleHeightPercent", CustomRectangleHeightPercent, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Histogram.UpdateMilliseconds", HistogramUpdateMilliseconds, "Histogram", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Display.NightMode", SelectedNightMode, "Display", _lifetimeCancellation.Token).ConfigureAwait(true);

            _deviceToolOptions.AdbExecutablePath = string.IsNullOrWhiteSpace(AdbPath) ? null : AdbPath;
            _deviceToolOptions.ScrcpyExecutablePath = string.IsNullOrWhiteSpace(ScrcpyPath) ? null : ScrcpyPath;
            _deviceMonitorOptions.RefreshInterval = TimeSpan.FromSeconds(StatusRefreshSeconds);
            _deviceMonitorOptions.AutoReconnect = AutomaticallyReconnect;
            SettingsStatusText =
                "Settings saved locally. Device polling and asset folders are active immediately.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not save settings.");
            SettingsStatusText = $"Settings could not be saved: {exception.Message}";
        }
    }

    partial void OnCurrentPageChanged(string value)
    {
        OnPropertyChanged(nameof(IsDashboard));
        OnPropertyChanged(nameof(IsHistory));
        OnPropertyChanged(nameof(IsSettings));
    }

    partial void OnSelectedLocationChanged(LocationOption? value)
    {
        if (value is null)
        {
            return;
        }

        _selectingLocation = true;
        try
        {
            Latitude = value.Latitude;
            Longitude = value.Longitude;
        }
        finally
        {
            _selectingLocation = false;
        }

        if (_initialized)
        {
            _ = RefreshConditionsAsync(_lifetimeCancellation.Token);
        }
    }

    partial void OnSelectedAdbDeviceChanged(AdbDevice? value)
    {
        _deviceMonitorOptions.PreferredSerial = value?.Serial;
        if (_initialized)
        {
            _ = _deviceMonitor.PollOnceAsync(_lifetimeCancellation.Token);
        }
    }

    partial void OnLatitudeChanged(double value)
    {
        if (!_selectingLocation)
        {
            SelectedLocation = null;
            if (_initialized)
            {
                _ = RefreshConditionsAsync(_lifetimeCancellation.Token);
            }
        }
    }

    partial void OnLongitudeChanged(double value)
    {
        if (!_selectingLocation)
        {
            SelectedLocation = null;
            if (_initialized)
            {
                _ = RefreshConditionsAsync(_lifetimeCancellation.Token);
            }
        }
    }

    partial void OnPlannedFrameCountChanged(int value) => UpdateSessionDisplay();

    partial void OnExposureSecondsChanged(double value) => UpdateSessionDisplay();

    partial void OnDelayBetweenFramesSecondsChanged(double value) => UpdateSessionDisplay();

    partial void OnIsPreviewFrozenChanged(bool value) => _preview.IsFrozen = value;

    partial void OnHistogramFrozenChanged(bool value)
    {
        // The displayed histogram freezes here; capture processing remains throttled and bounded.
    }

    partial void OnSelectedNightModeChanged(NightDisplayMode value)
    {
        _nightMode.Apply(value);
        _preview.DisplayMode = value;
    }

    partial void OnOverlayColorNameChanged(string value)
    {
        OverlayBrush = value switch
        {
            "Red" => Brushes.Red,
            "Amber" => Brushes.Goldenrod,
            "White" => Brushes.White,
            "Green" => Brushes.LimeGreen,
            "Cyan" => Brushes.Cyan,
            _ => Brushes.OrangeRed,
        };
    }

    partial void OnHistogramUpdateMillisecondsChanged(int value)
    {
        if (value > 0)
        {
            _preview.HistogramUpdateInterval = TimeSpan.FromMilliseconds(
                Math.Clamp(value, 100, 5000));
        }
    }

    partial void OnEnableNetworkConditionsChanged(bool value)
    {
        if (_initialized)
        {
            _ = RefreshConditionsAsync(_lifetimeCancellation.Token);
        }
    }

    partial void OnSelectedHistorySessionChanged(SessionHistoryItemViewModel? value)
    {
        HistoryEditRating = value?.Session.Rating;
        HistoryEditProblems = value?.Session.Problems ?? string.Empty;
        OnPropertyChanged(nameof(HasSelectedHistorySession));
    }

    private async Task LoadSettingsAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ISettingsService settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
        AdbPath = await settings.GetAsync("Device.AdbPath", AdbPath, cancellationToken) ?? AdbPath;
        ScrcpyPath = await settings.GetAsync("Device.ScrcpyPath", ScrcpyPath, cancellationToken) ?? ScrcpyPath;
        ScrcpyBitrateMbps = await settings.GetAsync("Scrcpy.BitrateMbps", ScrcpyBitrateMbps, cancellationToken);
        ScrcpyMaxResolution = await settings.GetAsync("Scrcpy.MaxResolution", ScrcpyMaxResolution, cancellationToken);
        ScrcpyMaxFps = await settings.GetAsync("Scrcpy.MaxFps", ScrcpyMaxFps, cancellationToken);
        StatusRefreshSeconds = await settings.GetAsync("Status.RefreshSeconds", StatusRefreshSeconds, cancellationToken);
        WeatherRefreshMinutes = await settings.GetAsync("Weather.RefreshMinutes", WeatherRefreshMinutes, cancellationToken);
        DefaultLocationName = await settings.GetAsync("Session.DefaultLocation", _appOptions.DefaultLocation, cancellationToken)
                              ?? _appOptions.DefaultLocation;
        DefaultExposureSeconds = await settings.GetAsync("Session.DefaultExposureSeconds", ExposureSeconds, cancellationToken);
        DefaultFrameCount = await settings.GetAsync("Session.DefaultFrameCount", PlannedFrameCount, cancellationToken);
        ScreenshotFolder = await settings.GetAsync("Files.ScreenshotFolder", ScreenshotFolder, cancellationToken)
                           ?? ScreenshotFolder;
        SessionDataFolder = await settings.GetAsync("Files.SessionDataFolder", SessionDataFolder, cancellationToken)
                            ?? SessionDataFolder;
        AutomaticallyLaunchScrcpy = await settings.GetAsync("Startup.AutoLaunchScrcpy", AutomaticallyLaunchScrcpy, cancellationToken);
        AutomaticallyReconnect = await settings.GetAsync("Startup.AutoReconnect", AutomaticallyReconnect, cancellationToken);
        EnableExperimentalCaptureControls = await settings.GetAsync("Experimental.CaptureControls", EnableExperimentalCaptureControls, cancellationToken);
        ExperimentalShutterX = await settings.GetAsync("Experimental.ShutterX", 0, cancellationToken);
        ExperimentalShutterY = await settings.GetAsync("Experimental.ShutterY", 0, cancellationToken);
        EnableNetworkConditions = await settings.GetAsync(
            "Privacy.EnableNetworkConditions",
            EnableNetworkConditions,
            cancellationToken);
        RuleOfThirds = await settings.GetAsync("Overlay.RuleOfThirds", RuleOfThirds, cancellationToken);
        Crosshair = await settings.GetAsync("Overlay.Crosshair", Crosshair, cancellationToken);
        DiagonalGuides = await settings.GetAsync("Overlay.DiagonalGuides", DiagonalGuides, cancellationToken);
        SafeArea = await settings.GetAsync("Overlay.SafeArea", SafeArea, cancellationToken);
        HorizonLine = await settings.GetAsync("Overlay.HorizonLine", HorizonLine, cancellationToken);
        CustomRectangle = await settings.GetAsync("Overlay.CustomRectangle", CustomRectangle, cancellationToken);
        CircleOverlay = await settings.GetAsync("Overlay.Circle", CircleOverlay, cancellationToken);
        OverlayOpacity = await settings.GetAsync("Overlay.Opacity", OverlayOpacity, cancellationToken);
        OverlayThickness = await settings.GetAsync("Overlay.Thickness", OverlayThickness, cancellationToken);
        OverlayColorName = await settings.GetAsync("Overlay.Color", OverlayColorName, cancellationToken)
                           ?? OverlayColorName;
        CustomRectangleWidthPercent = await settings.GetAsync(
            "Overlay.RectangleWidthPercent",
            CustomRectangleWidthPercent,
            cancellationToken);
        CustomRectangleHeightPercent = await settings.GetAsync(
            "Overlay.RectangleHeightPercent",
            CustomRectangleHeightPercent,
            cancellationToken);
        HistogramUpdateMilliseconds = await settings.GetAsync(
            "Histogram.UpdateMilliseconds",
            HistogramUpdateMilliseconds,
            cancellationToken);
        SelectedNightMode = await settings.GetAsync("Display.NightMode", SelectedNightMode, cancellationToken);

        ExposureSeconds = DefaultExposureSeconds;
        PlannedFrameCount = DefaultFrameCount;
        TimerExposureSeconds = DefaultExposureSeconds;
        TimerFrameCount = DefaultFrameCount;
        _deviceToolOptions.AdbExecutablePath = string.IsNullOrWhiteSpace(AdbPath) ? null : AdbPath;
        _deviceToolOptions.ScrcpyExecutablePath = string.IsNullOrWhiteSpace(ScrcpyPath) ? null : ScrcpyPath;
        _deviceMonitorOptions.RefreshInterval = TimeSpan.FromSeconds(
            Math.Clamp(StatusRefreshSeconds, 5, 300));
        _deviceMonitorOptions.AutoReconnect = AutomaticallyReconnect;
        try
        {
            _paths.SetScreenshotRoot(ScreenshotFolder);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(exception, "Persisted screenshot folder is unavailable; using the default.");
            ScreenshotFolder = _paths.ScreenshotRoot;
        }

        try
        {
            _paths.SetSessionRoot(SessionDataFolder);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.LogWarning(exception, "Persisted session folder is unavailable; using the default.");
            SessionDataFolder = _paths.SessionRoot;
        }
    }

    private async Task LoadLocationsAsync(CancellationToken cancellationToken)
    {
        Locations.Clear();
        foreach (LocationSearchResult example in LocationSeeds.LebanonExamples)
        {
            AddLocationIfMissing(LocationOption.FromSearchResult(example));
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IRepository<SavedLocation> repository =
            scope.ServiceProvider.GetRequiredService<IRepository<SavedLocation>>();
        IReadOnlyList<SavedLocation> saved = await repository.ListAsync(cancellationToken).ConfigureAwait(true);
        foreach (SavedLocation location in saved)
        {
            AddLocationIfMissing(
                new LocationOption(
                    location.Name,
                    location.Latitude,
                    location.Longitude,
                    location.TimeZoneId,
                    location.ElevationMeters));
        }

        SelectedLocation = Locations.FirstOrDefault(
                               location => location.Name.Contains(
                                   DefaultLocationName,
                                   StringComparison.CurrentCultureIgnoreCase))
                           ?? Locations.FirstOrDefault();
    }

    private async Task RecoverSessionAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IShootingSessionRepository repository =
            scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
        _activeSession = await repository.GetCurrentAsync(cancellationToken).ConfigureAwait(true);
        if (_activeSession is not null)
        {
            ApplySessionToEditor(_activeSession);
        }

        UpdateSessionDisplay();
        if (_activeSession is not null)
        {
            StatusMessage = $"Recovered {_activeSession.Status.ToString().ToLowerInvariant()} session for {_activeSession.TargetName}.";
        }
    }

    private async Task MutateActiveSessionAsync(
        Func<ISessionService, Guid, CancellationToken, Task<ShootingSession>> mutation,
        string successMessage)
    {
        if (_activeSession is null)
        {
            StatusMessage = "There is no active session.";
            return;
        }

        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISessionService service = scope.ServiceProvider.GetRequiredService<ISessionService>();
            _activeSession = await mutation(
                    service,
                    _activeSession.Id,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            UpdateSessionDisplay();
            StatusMessage = successMessage;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Session mutation failed.");
            StatusMessage = exception.Message;
        }
    }

    private async Task RegisterScreenshotAsync(string path, bool includesOverlays)
    {
        if (_activeSession is null)
        {
            return;
        }

        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        IShootingSessionRepository repository =
            scope.ServiceProvider.GetRequiredService<IShootingSessionRepository>();
        ShootingSession session = await repository
            .GetByIdAsync(_activeSession.Id, _lifetimeCancellation.Token)
            .ConfigureAwait(true)
            ?? _activeSession;
        session.AddScreenshot(path, includesOverlays, DateTimeOffset.UtcNow);
        await repository.UpdateAsync(session, _lifetimeCancellation.Token).ConfigureAwait(true);
        _activeSession = session;
        UpdateSessionDisplay();
    }

    private async Task ExecuteToolbarAsync(DeviceToolbarAction action, string? clipboardText = null)
    {
        try
        {
            DeviceMonitorSnapshot? snapshot = _deviceMonitor.LastSnapshot;
            ToolbarActionResult result = await _toolbar.ExecuteAsync(
                    action,
                    new ToolbarActionContext(
                        Serial: snapshot?.Connection.SelectedDevice?.Serial,
                        ClipboardText: clipboardText,
                        WindowHandle: _preview.CurrentSession?.WindowHandle),
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);
            StatusMessage = result.Succeeded
                ? $"{SplitPascalCase(action.ToString())} sent."
                : result.Message ?? "The device action is unavailable.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Device toolbar action {Action} failed.", action);
            StatusMessage = $"{SplitPascalCase(action.ToString())} failed: {exception.Message}";
        }
    }

    private void HandleDeviceSnapshot(object? sender, DeviceMonitorSnapshotEventArgs args) =>
        Dispatch(
            () =>
            {
                DeviceMonitorSnapshot snapshot = args.Snapshot;
                string? selectedSerial = SelectedAdbDevice?.Serial
                                         ?? snapshot.Connection.SelectedDevice?.Serial;
                AvailableAdbDevices.Clear();
                foreach (AdbDevice device in snapshot.Connection.Devices)
                {
                    AvailableAdbDevices.Add(device);
                }

                SelectedAdbDevice = AvailableAdbDevices.FirstOrDefault(
                                        device => device.Serial == selectedSerial)
                                    ?? snapshot.Connection.SelectedDevice
                                    ?? (snapshot.Connection.State == AdbConnectionState.MultipleDevices
                                        ? null
                                        : AvailableAdbDevices.FirstOrDefault());
                ConnectionStatus = FriendlyConnectionStatus(snapshot);
                DeviceConnected = snapshot.Connection.IsConnected;
                PhoneStatus? status = snapshot.PhoneStatus;
                DeviceModel = status?.Model
                              ?? snapshot.Connection.SelectedDevice?.DisplayName
                              ?? "No phone connected";
                AndroidVersionText = status?.AndroidVersion is { Length: > 0 } version
                    ? $"Android {version}"
                    : "Unavailable";
                BatteryText = status?.BatteryPercentage is { } battery
                    ? $"{battery}% · {status.ChargingState?.ToString() ?? "state unavailable"}"
                    : "Unavailable";
                decimal? temperature = status?.EstimatedPhoneTemperatureCelsius
                                       ?? status?.BatteryTemperatureCelsius;
                PhoneTemperatureText = temperature is null
                    ? "Unavailable"
                    : $"{temperature.Value:0.0} °C";
                StorageText = status?.InternalStorage is { } storage
                    ? $"{FormatBytes(storage.FreeBytes)} free / {FormatBytes(storage.TotalBytes)}"
                    : "Unavailable";
                ScreenResolutionText = status?.ScreenResolution is { } resolution
                    ? $"{resolution.Width} × {resolution.Height}"
                    : "Unavailable";
                OrientationText = status?.Orientation?.ToString() ?? "Unavailable";
                UsbStateText = status?.UsbConnection?.ToString() ?? "Unavailable";
            });

    private void HandlePreviewFrame(object? sender, PreviewFrameSnapshot frame)
    {
        Interlocked.Exchange(ref _pendingPreviewFrame, frame);
        if (Interlocked.Exchange(ref _previewDispatchScheduled, 1) != 0)
        {
            return;
        }

        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            ApplyLatestPreviewFrame();
            return;
        }

        _ = dispatcher.BeginInvoke(
            ApplyLatestPreviewFrame,
            DispatcherPriority.Render);
    }

    private void HandleHistogram(object? sender, HistogramResult histogram)
    {
        if (HistogramFrozen)
        {
            return;
        }

        Dispatch(
            () =>
            {
                Histogram = histogram;
                HighlightClippingText = $"Highlights: {histogram.HighlightClippingPercent:0.00}%";
                ShadowClippingText = $"Shadows: {histogram.ShadowClippingPercent:0.00}%";
            });
    }

    private void HandlePreviewStatus(object? sender, PreviewStatus status) =>
        Dispatch(
            () =>
            {
                PreviewStatus = status.Message;
                PreviewFps = status.FramesPerSecond;
                PreviewError = status.Error;
            });

    private void HandleTimerTick(object? sender, ExposureTimerTick tick) =>
        Dispatch(
            () =>
            {
                TimerStateText = tick.State.ToString();
                TimerPhaseText = SplitPascalCase(tick.Phase.ToString());
                TimerCountdownText = tick.Remaining.ToString(
                    tick.Remaining.TotalHours >= 1
                        ? @"hh\:mm\:ss\.f"
                        : tick.Remaining.TotalMinutes >= 1
                            ? @"mm\:ss\.f"
                            : @"ss\.f",
                    CultureInfo.InvariantCulture);
                TimerFrameText = $"Frame {tick.CurrentFrame} / {tick.TotalFrames}";

                if (tick.CompletedFrames > _lastTimerCompletedFrames)
                {
                    _lastTimerCompletedFrames = tick.CompletedFrames;
                    if (TimerSound)
                    {
                        SystemSounds.Beep.Play();
                    }
                }

                if (tick.Phase == ExposureTimerPhase.Exposure &&
                    tick.CurrentFrame > _lastExperimentalTimerFrame &&
                    ExperimentalTimerCapture &&
                    EnableExperimentalCaptureControls)
                {
                    _lastExperimentalTimerFrame = tick.CurrentFrame;
                    _ = ExperimentalShutterTapAsync();
                }
            });

    private void UpdateConditionsDisplay()
    {
        WeatherConditions? weather = _latestWeather;
        TemperatureText = Format(weather?.TemperatureCelsius, "0.0", " °C");
        HumidityText = Format(weather?.HumidityPercent, "0", "%");
        WindText = Format(weather?.WindSpeedKilometersPerHour, "0.0", " km/h");
        CloudCoverText = Format(weather?.CloudCoverPercent, "0", "%");
        VisibilityText = Format(weather?.VisibilityKilometers, "0.0", " km");
        DewPointText = Format(weather?.DewPointCelsius, "0.0", " °C");
        DewRiskText = weather?.DewRisk.ToString() ?? "Unavailable";

        AstronomyConditions? astronomy = _latestAstronomy;
        SunsetText = FormatTime(astronomy?.Sunset);
        AstronomicalDuskText = FormatTime(astronomy?.EndOfAstronomicalTwilight);
        SunriseText = FormatTime(astronomy?.Sunrise);
        AstronomicalDawnText = FormatTime(astronomy?.StartOfAstronomicalTwilight);
        MoonPhaseText = astronomy?.MoonPhase ?? "Unavailable";
        MoonIlluminationText = Format(astronomy?.MoonIlluminationPercent, "0", "%");
        MoonriseText = FormatTime(astronomy?.Moonrise);
        MoonsetText = FormatTime(astronomy?.Moonset);
        MoonAltitudeText = Format(astronomy?.MoonAltitudeDegrees, "0.0", "°");
        DarkSkyWindowText =
            astronomy?.DarkSkyWindowStart is { } darkStart &&
            astronomy.DarkSkyWindowEnd is { } darkEnd
                ? $"{darkStart.ToLocalTime():HH:mm} – {darkEnd.ToLocalTime():HH:mm}"
                : "Unavailable";
    }

    private void ApplyLatestPreviewFrame()
    {
        PreviewFrameSnapshot? frame = Interlocked.Exchange(ref _pendingPreviewFrame, null);
        if (frame is not null)
        {
            PreviewImage = frame.Image;
            PreviewFrameSizeText = $"{frame.Width} × {frame.Height}";
            ScrcpyClientSize = new Size(frame.Width, frame.Height);
            PreviewError = null;
        }

        Interlocked.Exchange(ref _previewDispatchScheduled, 0);
        if (Volatile.Read(ref _pendingPreviewFrame) is not null)
        {
            HandlePreviewFrame(this, Volatile.Read(ref _pendingPreviewFrame)!);
        }
    }

    private void UpdateSessionDisplay()
    {
        ShootingSession? session = _activeSession;
        OnPropertyChanged(nameof(HasActiveSession));
        OnPropertyChanged(nameof(IsSessionPaused));
        OnPropertyChanged(nameof(IsSessionActive));

        if (session is null)
        {
            SessionStateText = "No active session";
            FrameCountText = $"0 / {Math.Max(0, PlannedFrameCount)}";
            FrameProgress = 0;
            FrameProgressText = "0%";
            RemainingFramesText = $"{Math.Max(0, PlannedFrameCount)} remaining";
            TimeSpan estimated = EstimateCaptureTime(
                ExposureSeconds,
                DelayBetweenFramesSeconds,
                Math.Max(0, PlannedFrameCount));
            EstimatedRemainingText = $"Estimated remaining: {FormatDuration(estimated)}";
            IntegrationText = "Integration: 0 min";
            SessionElapsedText = "00:00:00";
            return;
        }

        SessionStateText = $"{session.Status} · {session.TargetName}";
        FrameCountText = $"{session.FrameCount} / {session.PlannedFrameCount}";
        FrameProgress = session.ProgressPercentage;
        FrameProgressText = $"{session.ProgressPercentage:0}%";
        RemainingFramesText = $"{session.RemainingFrames} remaining";
        EstimatedRemainingText =
            $"Estimated remaining: {FormatDuration(session.EstimatedRemainingCaptureTime)}";
        IntegrationText = $"Integration: {FormatDuration(session.TotalIntegrationTime)}";
        UpdateElapsedDisplay();
    }

    private void ApplySessionToEditor(ShootingSession session)
    {
        TargetName = session.TargetName;
        SelectedSessionType = session.SessionType;
        Latitude = session.Latitude;
        Longitude = session.Longitude;
        CameraName = session.Camera;
        SelectedPhoneLens = session.SelectedPhoneLens;
        Iso = session.Iso;
        ExposureSeconds = session.ExposureTime.TotalSeconds;
        DelayBetweenFramesSeconds = session.DelayBetweenFrames.TotalSeconds;
        InitialDelaySeconds = session.InitialDelay.TotalSeconds;
        PlannedFrameCount = session.PlannedFrameCount;
        WhiteBalance = session.WhiteBalance;
        FocusSetting = session.FocusSetting;
        RawEnabled = session.RawEnabled;
        SelectedLocation = Locations.FirstOrDefault(
            location =>
                Math.Abs(location.Latitude - session.Latitude) < 0.00001 &&
                Math.Abs(location.Longitude - session.Longitude) < 0.00001);
    }

    private void UpdateElapsedDisplay()
    {
        SessionElapsedText = _activeSession is null
            ? "00:00:00"
            : _activeSession.GetActualSessionDuration(DateTimeOffset.UtcNow).ToString(@"hh\:mm\:ss");
    }

    private async Task RunConditionsRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(
                        TimeSpan.FromMinutes(Math.Clamp(WeatherRefreshMinutes, 1, 180)),
                        cancellationToken)
                    .ConfigureAwait(false);
                await DispatchAsync(() => RefreshConditionsAsync(cancellationToken)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void AddLocationIfMissing(LocationOption location)
    {
        if (Locations.Any(
                existing =>
                    Math.Abs(existing.Latitude - location.Latitude) < 0.00001 &&
                    Math.Abs(existing.Longitude - location.Longitude) < 0.00001))
        {
            return;
        }

        Locations.Add(location);
    }

    private void ValidateSessionInputs()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TargetName);
        _ = new GeoCoordinate(Latitude, Longitude);
        if (ExposureSeconds <= 0 || DelayBetweenFramesSeconds < 0 || InitialDelaySeconds < 0)
        {
            throw new InvalidOperationException("Exposure and delay values are invalid.");
        }

        if (PlannedFrameCount < 0)
        {
            throw new InvalidOperationException("Planned frame count cannot be negative.");
        }
    }

    private void ValidateSettings()
    {
        if (ScrcpyBitrateMbps is < 1 or > 100)
        {
            throw new InvalidOperationException("scrcpy bitrate must be between 1 and 100 Mbps.");
        }

        if (ScrcpyMaxFps is < 1 or > 120)
        {
            throw new InvalidOperationException("scrcpy FPS must be between 1 and 120.");
        }

        if (StatusRefreshSeconds is < 5 or > 300)
        {
            throw new InvalidOperationException("Status refresh must be between 5 and 300 seconds.");
        }

        if (WeatherRefreshMinutes is < 1 or > 180)
        {
            throw new InvalidOperationException("Weather refresh must be between 1 and 180 minutes.");
        }

        if (CustomRectangleWidthPercent is < 5 or > 100 ||
            CustomRectangleHeightPercent is < 5 or > 100)
        {
            throw new InvalidOperationException("Custom framing rectangle dimensions must be between 5% and 100%.");
        }

        if (HistogramUpdateMilliseconds is < 100 or > 5000)
        {
            throw new InvalidOperationException("Histogram update interval must be between 100 and 5000 milliseconds.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(ScreenshotFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(SessionDataFolder);
    }

    private static SessionType? ParseHistoryType(string value)
    {
        if (value == "All types")
        {
            return null;
        }

        return Enum.GetValues<SessionType>()
            .Cast<SessionType?>()
            .FirstOrDefault(type => type is not null && SplitPascalCase(type.Value.ToString()) == value);
    }

    private static TimeSpan EstimateCaptureTime(double exposureSeconds, double delaySeconds, int frames)
    {
        if (frames <= 0 || exposureSeconds <= 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds((exposureSeconds * frames) + (Math.Max(0, frames - 1) * Math.Max(0, delaySeconds)));
    }

    private static string Format(double? value, string format, string suffix) =>
        value is null
            ? "Unavailable"
            : value.Value.ToString(format, CultureInfo.CurrentCulture) + suffix;

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture) ?? "Unavailable";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return "0 min";
        }

        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} h {duration.Minutes} min"
            : $"{Math.Ceiling(duration.TotalMinutes):0} min";
    }

    private static string FormatBytes(long bytes)
    {
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }

    private static string SplitPascalCase(string value) =>
        string.Concat(
            value.Select(
                (character, index) =>
                    index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1])
                        ? $" {character}"
                        : character.ToString()));

    private static string FriendlyToolError(Exception exception, string toolName) =>
        exception is AstroDesk.Device.Processes.ExecutableNotFoundException
            ? $"{toolName} was not found. Install it or select its executable in Settings."
            : exception.Message;

    private static string FriendlyConnectionStatus(DeviceMonitorSnapshot snapshot)
    {
        string message = snapshot.ErrorMessage ?? snapshot.Connection.Message;
        return message.Contains("Could not find adb", StringComparison.OrdinalIgnoreCase)
            ? "ADB was not found. Install Android platform-tools or select adb.exe in Settings."
            : message;
    }

    private static void Dispatch(Action action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _ = dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }

    private static Task DispatchAsync(Func<Task> action)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Background).Task.Unwrap();
    }
}
