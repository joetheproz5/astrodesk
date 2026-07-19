using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AstroDesk.App.Configuration;
using AstroDesk.App.Services;
using AstroDesk.Capture.Geometry;
using AstroDesk.Capture.Histogram;
using AstroDesk.Core.Calculations;
using AstroDesk.Core.Entities;
using AstroDesk.Core.Enums;
using AstroDesk.Stacking;
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
using QRCoder;

namespace AstroDesk.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IPhonePreviewCoordinator _preview;
    private readonly IDeviceMonitor _deviceMonitor;
    private readonly IWeatherProvider _weatherProvider;
    private readonly IAstronomyProvider _astronomyProvider;
    private readonly ILightPollutionProvider _lightPollutionProvider;
    private readonly ILocationProvider _locationProvider;
    private readonly IExposureTimerService _exposureTimer;
    private readonly IDeviceToolbarController _toolbar;
    private readonly IAdbInputService _adbInput;
    private readonly IAdbWirelessClient _adbWireless;
    private readonly DeviceToolOptions _deviceToolOptions;
    private readonly DeviceMonitorOptions _deviceMonitorOptions;
    private readonly AstroDeskAppOptions _appOptions;
    private readonly AppPaths _paths;
    private readonly ISessionAssetService _sessionAssets;
    private readonly IPhonePhotoSyncService _photoSync;
    private readonly INightModeService _nightMode;
    private readonly IStackSessionService _stackSession;
    private readonly IStackEngine _stackEngine;
    private readonly IAutoCaptureService _autoCapture;
    private readonly ICameraForegroundService _cameraForeground;
    private readonly ILivePreviewStackCoordinator _livePreview;
    private readonly ILivePreviewRenderer _livePreviewRenderer;
    private readonly ILogger<MainWindowViewModel> _logger;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly DispatcherTimer _elapsedTimer;
    private ShootingSession? _activeSession;
    private WeatherConditions? _latestWeather;
    private AstronomyConditions? _latestAstronomy;
    private LightPollutionConditions? _latestLightPollution;
    private string? _lastScreenshotPath;
    private int _lastTimerCompletedFrames;
    private int _lastExperimentalTimerFrame;
    private bool _initialized;
    private bool _initializing;
    private bool _selectingLocation;
    private bool _suppressLocationRefresh;
    private long _conditionsRequestVersion;
    private PreviewFrameSnapshot? _pendingPreviewFrame;

    /// <summary>
    /// How the phone was attached when it last worked, so a drop can be
    /// explained after the serial that would have told us is already gone.
    /// </summary>
    private DeviceTransport _lastKnownTransport = DeviceTransport.Unknown;
    private int _previewDispatchScheduled;
    private CancellationTokenSource? _wirelessQrCancellation;
    private int _disposed;

    public MainWindowViewModel(
        IServiceScopeFactory scopeFactory,
        IPhonePreviewCoordinator preview,
        IDeviceMonitor deviceMonitor,
        IWeatherProvider weatherProvider,
        IAstronomyProvider astronomyProvider,
        ILightPollutionProvider lightPollutionProvider,
        ILocationProvider locationProvider,
        IExposureTimerService exposureTimer,
        IDeviceToolbarController toolbar,
        IAdbInputService adbInput,
        IAdbWirelessClient adbWireless,
        DeviceToolOptions deviceToolOptions,
        DeviceMonitorOptions deviceMonitorOptions,
        AstroDeskAppOptions appOptions,
        AppPaths paths,
        ISessionAssetService sessionAssets,
        IPhonePhotoSyncService photoSync,
        INightModeService nightMode,
        IStackSessionService stackSession,
        IStackEngine stackEngine,
        IAutoCaptureService autoCapture,
        ICameraForegroundService cameraForeground,
        ILivePreviewStackCoordinator livePreview,
        ILivePreviewRenderer livePreviewRenderer,
        ILogger<MainWindowViewModel> logger)
    {
        _scopeFactory = scopeFactory;
        _preview = preview;
        _deviceMonitor = deviceMonitor;
        _weatherProvider = weatherProvider;
        _astronomyProvider = astronomyProvider;
        _lightPollutionProvider = lightPollutionProvider;
        _locationProvider = locationProvider;
        _exposureTimer = exposureTimer;
        _toolbar = toolbar;
        _adbInput = adbInput;
        _adbWireless = adbWireless;
        _deviceToolOptions = deviceToolOptions;
        _deviceMonitorOptions = deviceMonitorOptions;
        _appOptions = appOptions;
        _paths = paths;
        _sessionAssets = sessionAssets;
        _photoSync = photoSync;
        _nightMode = nightMode;
        _stackSession = stackSession;
        _stackEngine = stackEngine;
        _autoCapture = autoCapture;
        _cameraForeground = cameraForeground;
        _livePreview = livePreview;
        _livePreviewRenderer = livePreviewRenderer;
        _stackSession.FrameAdded += HandleStackFrameAdded;
        _livePreview.Updated += HandleLivePreviewUpdated;
        _autoCapture.Triggered += HandleAutoCaptureTriggered;
        _autoCapture.Finished += HandleAutoCaptureFinished;
        _autoCapture.TelemetryChanged += HandleAutoCaptureTelemetry;

        // Surface whatever the engine resolved (bundled copy or environment
        // override) so the Settings field reflects reality instead of looking
        // unconfigured while stacking actually works.
        SirilPath = stackEngine.ExecutablePath;
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
        PhoneCaptureFolder = _paths.PhoneCaptureRoot;
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
    private string _wirelessEndpoint = string.Empty;

    [ObservableProperty]
    private string _wirelessPairingEndpoint = string.Empty;

    [ObservableProperty]
    private string _wirelessPairingCode = string.Empty;

    [ObservableProperty]
    private string _wirelessStatus = "Choose the one-tap USB switch or enter the address shown under Wireless debugging.";

    [ObservableProperty]
    private bool _isWirelessBusy;

    [ObservableProperty]
    private bool _isWirelessQrActive;

    [ObservableProperty]
    private ImageSource? _wirelessQrCodeImage;

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
    private nint _scrcpyWindowHandle;

    public nint MainScrcpyWindowHandle =>
        IsPreviewFullscreen || ShowWirelessPanel ? nint.Zero : ScrcpyWindowHandle;

    public nint FullscreenScrcpyWindowHandle =>
        IsPreviewFullscreen ? ScrcpyWindowHandle : nint.Zero;

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
    private bool _showNightBriefDrawer;

    [ObservableProperty]
    private bool _showSessionDrawer;

    [ObservableProperty]
    private bool _showWirelessPanel;

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
    private string _locationStatusText = "Locating this laptop…";

    [ObservableProperty]
    private string _locationCoordinateText = "Waiting for location";

    [ObservableProperty]
    private string _timeZoneText = "Detecting time zone…";

    [ObservableProperty]
    private string _altitudeText = "Unavailable";

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
    private string _precipitationText = "Unavailable";

    [ObservableProperty]
    private string _dewPointText = "Unavailable";

    [ObservableProperty]
    private string _dewRiskText = "Unavailable";

    [ObservableProperty]
    private string _shootingRecommendationText = "Forecast unavailable";

    [ObservableProperty]
    private string _shootingScoreText = "Unavailable";

    [ObservableProperty]
    private string _bestShootingWindowText = "Unavailable";

    [ObservableProperty]
    private string _shootingAdviceText = "Waiting for an hourly forecast.";

    [ObservableProperty]
    private string _shootQualityLevel = ObservingQuality.Unavailable.ToString();

    [ObservableProperty]
    private string _lightPollutionText = "Unavailable";

    [ObservableProperty]
    private string _lightPollutionDetailText = "Live atlas data unavailable";

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
    private string _moonPositionText = "Unavailable";

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

    /// <summary>
    /// Raises the stream quality above the saved settings when the phone is on
    /// USB.
    /// </summary>
    /// <remarks>
    /// Off by default. It was on, and on a real S23 Ultra the extra traffic
    /// reset the USB link within seconds of any on-screen movement, taking the
    /// scrcpy server down with it. A preview that dies whenever it is touched is
    /// far worse than one that is slightly softer, so the sharper stream has to
    /// be asked for rather than assumed.
    /// </remarks>
    [ObservableProperty]
    private bool _boostQualityOnCable;

    // ------------------------------------------------------------ shooting zone

    public const string ShootingZoneFullScreen = "Full screen";
    public const string ShootingZoneExpertRaw = "Expert RAW (4:3)";

    public IReadOnlyList<string> ShootingZoneOptions { get; } =
        [ShootingZoneFullScreen, ShootingZoneExpertRaw];

    /// <summary>
    /// Which camera layout the framing guides should be drawn inside.
    /// </summary>
    /// <remarks>
    /// Guides across the whole mirrored screen land inside the camera app's own
    /// control panel rather than on the frame, so composing to them puts the
    /// subject in the wrong place in the photograph.
    /// </remarks>
    [ObservableProperty]
    private string _selectedShootingZone = ShootingZoneFullScreen;

    public ShootingZone ShootingZone => SelectedShootingZone switch
    {
        ShootingZoneExpertRaw => Capture.Geometry.ShootingZone.ExpertRaw43,
        _ => Capture.Geometry.ShootingZone.FullScreen,
    };

    partial void OnSelectedShootingZoneChanged(string value) =>
        OnPropertyChanged(nameof(ShootingZone));

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
    private string _phoneCaptureFolder = string.Empty;

    [ObservableProperty]
    private bool _automaticallyImportPhonePhotos = true;

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
    private string _selectedWindowMode = "Windowed fullscreen";

    [ObservableProperty]
    private string _settingsStatusText = "Settings are stored locally.";

    public IReadOnlyList<string> WindowModeOptions { get; } =
    [
        "Windowed",
        "Borderless window",
        "Windowed fullscreen",
        "Fullscreen",
    ];

    public bool IsDashboard => CurrentPage == "Dashboard";

    public bool IsHistory => CurrentPage == "History";

    public bool IsSettings => CurrentPage == "Settings";

    public bool IsStacking => CurrentPage == "Stacking";

    public bool IsSky => CurrentPage == "Sky";

    /// <summary>
    /// The light-pollution map, opened on the site currently being planned.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the live web map rather than something drawn from the atlas tiles
    /// the app already downloads. Those tiles would give an offline map, but
    /// choosing where to shoot is planning done at a desk rather than at the
    /// tripod, and the site brings panning, search and overlays that would each
    /// have to be rebuilt for no gain.
    /// </para>
    /// <para>
    /// Zoom 9 is close enough to tell one valley from the next while still
    /// showing where the nearest town's glow reaches.
    /// </para>
    /// </remarks>
    public Uri SkyMapUri => new(
        "https://www.lightpollutionmap.app/?" +
        $"lat={Latitude.ToString("0.######", CultureInfo.InvariantCulture)}" +
        $"&lng={Longitude.ToString("0.######", CultureInfo.InvariantCulture)}" +
        "&zoom=9");

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
            _photoSync.PhotoImported += HandlePhonePhotoImported;
            _preview.WindowHandleChanged += HandlePreviewWindowHandleChanged;
            _deviceMonitor.SnapshotChanged += HandleDeviceSnapshot;
            _exposureTimer.Tick += HandleTimerTick;
            subscribed = true;

            await LoadSettingsAsync(cancellationToken).ConfigureAwait(true);
            await LoadLocationsAsync(cancellationToken).ConfigureAwait(true);
            await RecoverSessionAsync(cancellationToken).ConfigureAwait(true);
            if (EnableNetworkConditions && _activeSession is null)
            {
                await LocateCurrentUserAsync(cancellationToken).ConfigureAwait(true);
            }

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _wirelessQrCancellation?.Cancel();
        _wirelessQrCancellation?.Dispose();
        _wirelessQrCancellation = null;
        _lifetimeCancellation.Cancel();
        _elapsedTimer.Stop();
        _preview.FrameReady -= HandlePreviewFrame;
        _preview.HistogramReady -= HandleHistogram;
        _preview.StatusChanged -= HandlePreviewStatus;
        _photoSync.PhotoImported -= HandlePhonePhotoImported;
        _preview.WindowHandleChanged -= HandlePreviewWindowHandleChanged;
        _deviceMonitor.SnapshotChanged -= HandleDeviceSnapshot;
        _exposureTimer.Tick -= HandleTimerTick;

        ScrcpyWindowHandle = nint.Zero;
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

    public void SetPhoneCaptureFolder(string path) => PhoneCaptureFolder = path;

    public void SetSessionDataFolder(string path) => SessionDataFolder = path;

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = "Dashboard";

    [RelayCommand]
    private void ShowHistory() => CurrentPage = "History";

    [RelayCommand]
    private void ShowSettings() => CurrentPage = "Settings";

    [RelayCommand]
    private void ShowStacking() => CurrentPage = "Stacking";

    [RelayCommand]
    private void ShowSky() => CurrentPage = "Sky";

    /// <summary>
    /// Re-centres the map on the site currently being planned.
    /// </summary>
    [RelayCommand]
    private void RecentreSkyMap() => OnPropertyChanged(nameof(SkyMapUri));

    [RelayCommand]
    private async Task StartPreviewAsync()
    {
        try
        {
            PreviewZoom = 1;
            ZoomCenter = new Point(0.5, 0.5);
            PreviewError = null;
            DeviceMonitorSnapshot? monitor = _deviceMonitor.LastSnapshot;
            if (monitor?.Connection.State == AdbConnectionState.MultipleDevices &&
                SelectedAdbDevice is null)
            {
                StatusMessage = "Select the intended Android device before starting scrcpy.";
                return;
            }

            if (monitor?.Connection.IsConnected != true)
            {
                // The cached snapshot can be a poll behind. That is how this
                // managed to report no device while the status bar still showed
                // the phone connected, which reads as the app contradicting
                // itself. Ask once more before refusing.
                await _deviceMonitor.PollOnceAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
                monitor = _deviceMonitor.LastSnapshot;
            }

            if (monitor?.Connection.IsConnected != true)
            {
                ReportNoDeviceForPreview();
                return;
            }

            string? serial = monitor?.Connection.SelectedDevice?.Serial
                             ?? SelectedAdbDevice?.Serial;
            _lastKnownTransport = DeviceTransportDetector.Detect(serial);
            ScrcpySession session = await _preview.StartAsync(
                BuildLaunchOptions(serial),
                _lifetimeCancellation.Token).ConfigureAwait(true);
            ScrcpyWindowHandle = session.WindowHandle;
            StatusMessage = DescribeStartedSession(session, serial);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not start scrcpy.");
            PreviewError = FriendlyToolError(exception, "scrcpy");
            PreviewStatus = "Embedded preview unavailable";
            StatusMessage = PreviewError;
        }
    }

    /// <summary>
    /// Explains a missing device in terms of how it was actually attached.
    /// </summary>
    /// <remarks>
    /// This used to open the wireless panel and say "finish the wireless
    /// connection first" whatever the phone was plugged into. On a cable that is
    /// simply wrong, and it is worst exactly when it is least welcome: a USB link
    /// that resets mid-session sends the user to a pairing screen that cannot
    /// help, while the status bar still shows the phone as connected.
    /// </remarks>
    private void ReportNoDeviceForPreview()
    {
        string? serial = _deviceMonitor.LastSnapshot?.Connection.SelectedDevice?.Serial
                         ?? SelectedAdbDevice?.Serial;

        // A dropped device leaves no serial to read, so fall back to how it was
        // attached when it was last working.
        DeviceTransport transport = DeviceTransportDetector.Detect(serial);
        if (transport == DeviceTransport.Unknown)
        {
            transport = _lastKnownTransport;
        }

        switch (transport)
        {
            case DeviceTransport.Cable:
                StatusMessage =
                    "The phone stopped responding over USB. Check the cable and port, " +
                    "then press Start preview again.";
                return;

            case DeviceTransport.Wireless:
                ShowWirelessPanel = true;
                WirelessStatus =
                    "The wireless connection has dropped. On the phone's Wireless debugging " +
                    "screen, copy its IP address and port into Connect directly, then press Connect.";
                StatusMessage = "The wireless ADB connection has dropped. Reconnect it, then start the preview.";
                return;

            default:
                // Nothing has ever connected in this session, so neither route is
                // more likely than the other; offering both beats guessing.
                StatusMessage =
                    "No phone is connected. Plug it in with a USB cable, " +
                    "or use Wireless to pair over Wi-Fi.";
                return;
        }
    }

    /// <summary>
    /// Builds the scrcpy options, lifting the stream quality when the phone is on
    /// a cable.
    /// </summary>
    /// <remarks>
    /// The saved settings are the floor, never the ceiling: the cable profile can
    /// only raise a value, so a deliberately higher setting is preserved and a
    /// deliberately lower one is still honoured by turning the boost off. Without
    /// that, opening Settings once would silently pin the cable back down to
    /// whatever Wi-Fi could survive.
    /// </remarks>
    internal ScrcpyLaunchOptions BuildLaunchOptions(string? serial)
    {
        int maxSize = ScrcpyMaxResolution;
        int maxFps = ScrcpyMaxFps;
        int bitrate = ScrcpyBitrateMbps;

        if (BoostQualityOnCable &&
            DeviceTransportDetector.Detect(serial) == DeviceTransport.Cable)
        {
            ScrcpyQualityProfile profile = ScrcpyQualityProfile.Cable;
            maxSize = Math.Max(maxSize, profile.MaxSize);
            maxFps = Math.Max(maxFps, profile.MaxFps);
            bitrate = Math.Max(bitrate, profile.VideoBitRateMbps);
        }

        return new ScrcpyLaunchOptions
        {
            ExecutablePath = string.IsNullOrWhiteSpace(ScrcpyPath) ? null : ScrcpyPath,
            DeviceSerial = serial,
            VideoBitRateMbps = Math.Clamp(bitrate, 1, 100),
            MaxSize = maxSize > 0 ? maxSize : null,
            MaxFps = maxFps > 0 ? maxFps : null,
            KeepAwake = true,
        };
    }

    /// <summary>
    /// Reports the quality actually in use, so a boosted cable session is not a
    /// silent difference from what Settings shows.
    /// </summary>
    private string DescribeStartedSession(ScrcpySession session, string? serial)
    {
        string link = DeviceTransportDetector.Detect(serial) switch
        {
            DeviceTransport.Cable => "cable",
            DeviceTransport.Wireless => "wireless",
            _ => "unknown link",
        };

        return $"scrcpy started over {link} at {session.Options.MaxSize?.ToString() ?? "native"}p, " +
               $"{session.Options.MaxFps?.ToString() ?? "unlimited"} fps, " +
               $"{session.Options.VideoBitRateMbps} Mbps.";
    }

    [RelayCommand]
    private async Task StopPreviewAsync()
    {
        try
        {
            ScrcpyWindowHandle = nint.Zero;
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
            ScrcpyWindowHandle = nint.Zero;
            ScrcpySession session = await _preview
                .ReconnectAsync(_lifetimeCancellation.Token)
                .ConfigureAwait(true);
            ScrcpyWindowHandle = session.WindowHandle;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Could not reconnect scrcpy.");
            PreviewError = FriendlyToolError(exception, "scrcpy");
        }
    }

    [RelayCommand]
    private async Task SwitchUsbToWirelessAsync()
    {
        if (IsWirelessBusy)
        {
            return;
        }

        string? serial = SelectedAdbDevice?.Serial
                         ?? _deviceMonitor.LastSnapshot?.Connection.SelectedDevice?.Serial;
        if (string.IsNullOrWhiteSpace(serial) || serial.Contains(':', StringComparison.Ordinal))
        {
            WirelessStatus = "Connect and authorize the phone with USB first, then tap this button again.";
            return;
        }

        IsWirelessBusy = true;
        try
        {
            WirelessStatus = "Finding the phone on Wi-Fi…";
            string address = await _adbWireless.GetWifiAddressAsync(
                serial,
                _lifetimeCancellation.Token).ConfigureAwait(true);
            string endpoint = $"{address}:5555";

            WirelessStatus = "Restarting ADB in wireless mode…";
            await _adbWireless.EnableTcpIpAsync(
                serial,
                5555,
                _lifetimeCancellation.Token).ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(1), _lifetimeCancellation.Token).ConfigureAwait(true);
            await _adbWireless.ConnectWirelessAsync(
                endpoint,
                _lifetimeCancellation.Token).ConfigureAwait(true);

            WirelessEndpoint = endpoint;
            _deviceMonitorOptions.PreferredSerial = endpoint;
            SelectedAdbDevice = null;
            _deviceMonitorOptions.PreferredSerial = endpoint;
            await _deviceMonitor.PollOnceAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            WirelessStatus = $"Connected wirelessly to {endpoint}. You can unplug USB now.";
            StatusMessage = "Wireless ADB connected.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not switch ADB from USB to wireless.");
            WirelessStatus = $"Wireless switch failed: {exception.Message}";
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectWirelessAsync()
    {
        if (IsWirelessBusy)
        {
            return;
        }

        IsWirelessBusy = true;
        try
        {
            string endpoint = NormalizeWirelessEndpoint(WirelessEndpoint, addDefaultPort: true);
            WirelessStatus = $"Connecting to {endpoint}…";
            await _adbWireless.ConnectWirelessAsync(
                endpoint,
                _lifetimeCancellation.Token).ConfigureAwait(true);
            WirelessEndpoint = endpoint;
            _deviceMonitorOptions.PreferredSerial = endpoint;
            SelectedAdbDevice = null;
            _deviceMonitorOptions.PreferredSerial = endpoint;
            await _deviceMonitor.PollOnceAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            WirelessStatus = $"Connected wirelessly to {endpoint}.";
            StatusMessage = "Wireless ADB connected.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not connect wireless ADB.");
            WirelessStatus = $"Wireless connection failed: {exception.Message}";
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    [RelayCommand]
    private async Task PairWirelessAsync()
    {
        if (IsWirelessBusy)
        {
            return;
        }

        IsWirelessBusy = true;
        try
        {
            string endpoint = NormalizeWirelessEndpoint(WirelessPairingEndpoint, addDefaultPort: false);
            string code = WirelessPairingCode.Trim();
            if (code.Length != 6 || !code.All(char.IsDigit))
            {
                throw new InvalidOperationException("Enter the six-digit pairing code shown on the phone.");
            }

            WirelessPairingEndpoint = endpoint;
            WirelessStatus = $"Pairing with {endpoint}…";

            await _adbWireless.PairWirelessAsync(
                endpoint,
                code,
                _lifetimeCancellation.Token).ConfigureAwait(true);
            WirelessPairingCode = string.Empty;
            WirelessStatus = "Paired. Looking for the phone's secure connection port…";
            string? connectedEndpoint = await TryAutoConnectPairedPhoneAsync(
                endpoint,
                _lifetimeCancellation.Token).ConfigureAwait(true);
            WirelessStatus = connectedEndpoint is null
                ? "Pairing succeeded, but the phone is NOT connected yet. Return to the main Wireless debugging screen on the phone, copy its different IP address and port into Connect directly above, then press Connect."
                : $"Paired and connected wirelessly to {connectedEndpoint}.";
            StatusMessage = connectedEndpoint is null
                ? "Wireless ADB pairing completed."
                : "Wireless ADB paired and connected.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not pair wireless ADB.");
            WirelessStatus = $"Pairing failed: {exception.Message}";
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    [RelayCommand]
    private async Task FindPairingAddressAsync()
    {
        if (IsWirelessBusy)
        {
            return;
        }

        IsWirelessBusy = true;
        try
        {
            WirelessStatus = "Restarting ADB wireless discovery…";
            await _adbWireless.RestartWirelessDiscoveryAsync(
                _lifetimeCancellation.Token).ConfigureAwait(true);
            WirelessStatus = "Looking for the open pairing-code screen on this Wi-Fi…";
            IReadOnlyList<AdbMdnsService> services = await _adbWireless.GetMdnsServicesAsync(
                _lifetimeCancellation.Token).ConfigureAwait(true);
            AdbMdnsService[] pairingServices = services
                .Where(static service => service.ServiceType == "_adb-tls-pairing._tcp")
                .Where(static service => service.InstanceName.StartsWith("adb-", StringComparison.Ordinal))
                .ToArray();
            if (pairingServices.Length == 0)
            {
                WirelessStatus = "No pairing-code screen was found. Keep that screen open on the S23 and make sure both devices are on the same Wi-Fi.";
                return;
            }

            if (pairingServices.Length > 1)
            {
                WirelessStatus = "More than one phone is offering ADB pairing. Enter the address shown on the intended S23.";
                return;
            }

            WirelessPairingEndpoint = pairingServices[0].Endpoint;
            WirelessStatus = $"Found {pairingServices[0].Endpoint}. Enter the six-digit code, then tap Pair phone.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not discover a wireless ADB pairing service.");
            WirelessStatus = $"Pairing discovery failed: {exception.Message}";
        }
        finally
        {
            IsWirelessBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartQrPairingAsync()
    {
        if (IsWirelessBusy)
        {
            return;
        }

        _wirelessQrCancellation?.Cancel();
        _wirelessQrCancellation?.Dispose();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
        _wirelessQrCancellation = cancellation;

        string serviceName = $"studio-{CreateRandomQrToken(10)}";
        string password = CreateRandomQrToken(12);
        string payload = $"WIFI:T:ADB;S:{serviceName};P:{password};;";
        WirelessQrCodeImage = CreateQrCodeImage(payload);
        IsWirelessQrActive = true;
        IsWirelessBusy = true;
        WirelessStatus = "Restarting ADB wireless discovery…";

        try
        {
            await _adbWireless.RestartWirelessDiscoveryAsync(cancellation.Token).ConfigureAwait(true);
            WirelessStatus = "On the S23, open Wireless debugging → Pair device with QR code, then scan this code.";
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(2);
            DateTimeOffset discoveryHintAt = DateTimeOffset.UtcNow.AddSeconds(12);
            bool discoveryHintShown = false;
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                IReadOnlyList<AdbMdnsService> services = await _adbWireless.GetMdnsServicesAsync(
                    cancellation.Token).ConfigureAwait(true);
                AdbMdnsService? pairingService = services.FirstOrDefault(
                    service => service.InstanceName == serviceName &&
                               service.ServiceType == "_adb-tls-pairing._tcp");
                if (pairingService is not null)
                {
                    WirelessStatus = $"S23 found at {pairingService.Endpoint}. Finishing secure pairing…";
                    await _adbWireless.PairWirelessAsync(
                        pairingService.Endpoint,
                        password,
                        cancellation.Token).ConfigureAwait(true);
                    WirelessStatus = "QR pairing complete. Looking for the secure connection port…";
                    string? connectedEndpoint = await TryAutoConnectPairedPhoneAsync(
                        pairingService.Endpoint,
                        cancellation.Token).ConfigureAwait(true);
                    WirelessStatus = connectedEndpoint is null
                        ? "QR pairing succeeded, but the phone is NOT connected yet. Return to the main Wireless debugging screen on the phone, copy its different IP address and port into Connect directly above, then press Connect."
                        : $"QR pairing complete. Connected wirelessly to {connectedEndpoint}.";
                    StatusMessage = connectedEndpoint is null
                        ? "Wireless ADB QR pairing completed."
                        : "Wireless ADB QR pairing completed and connected.";
                    return;
                }

                if (!discoveryHintShown && DateTimeOffset.UtcNow >= discoveryHintAt)
                {
                    discoveryHintShown = true;
                    WirelessStatus = "No QR pairing service has reached this laptop yet. If the phone is spinning, cancel QR and use the six-digit code; some Windows hotspots block QR discovery.";
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token).ConfigureAwait(true);
            }

            WirelessStatus = "QR pairing timed out. Keep the QR scanner open, stay on the same Wi-Fi, and try again.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            WirelessStatus = "QR pairing cancelled.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not pair wireless ADB with a QR code.");
            WirelessStatus = $"QR pairing failed: {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_wirelessQrCancellation, cancellation))
            {
                _wirelessQrCancellation = null;
            }

            cancellation.Dispose();
            WirelessQrCodeImage = null;
            IsWirelessQrActive = false;
            IsWirelessBusy = false;
        }
    }

    [RelayCommand]
    private void CancelQrPairing()
    {
        _wirelessQrCancellation?.Cancel();
    }

    private async Task<string?> TryAutoConnectPairedPhoneAsync(
        string pairingEndpoint,
        CancellationToken cancellationToken)
    {
        int separator = pairingEndpoint.LastIndexOf(':');
        string address = separator > 0 ? pairingEndpoint[..separator] : pairingEndpoint;
        for (int attempt = 0; attempt < 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                IReadOnlyList<AdbMdnsService> services = await _adbWireless.GetMdnsServicesAsync(
                    cancellationToken).ConfigureAwait(true);
                AdbMdnsService? connectService = services.FirstOrDefault(
                    service => service.ServiceType == "_adb-tls-connect._tcp" &&
                               service.Endpoint.StartsWith(
                                   address + ":",
                                   StringComparison.OrdinalIgnoreCase));
                if (connectService is not null)
                {
                    await _adbWireless.ConnectWirelessAsync(
                        connectService.Endpoint,
                        cancellationToken).ConfigureAwait(true);
                    WirelessEndpoint = connectService.Endpoint;
                    await _deviceMonitor.PollOnceAsync(cancellationToken).ConfigureAwait(true);
                    return connectService.Endpoint;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogDebug(exception, "Could not auto-connect immediately after wireless pairing.");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(true);
        }

        await _deviceMonitor.PollOnceAsync(cancellationToken).ConfigureAwait(true);
        return null;
    }

    [RelayCommand]
    private async Task DisconnectWirelessAsync()
    {
        if (IsWirelessBusy)
        {
            return;
        }

        IsWirelessBusy = true;
        try
        {
            string candidate = string.IsNullOrWhiteSpace(WirelessEndpoint)
                ? SelectedAdbDevice?.Serial ?? string.Empty
                : WirelessEndpoint;
            string endpoint = NormalizeWirelessEndpoint(candidate, addDefaultPort: true);
            await _adbWireless.DisconnectWirelessAsync(
                endpoint,
                _lifetimeCancellation.Token).ConfigureAwait(true);
            _deviceMonitorOptions.PreferredSerial = null;
            await _deviceMonitor.PollOnceAsync(_lifetimeCancellation.Token).ConfigureAwait(true);
            WirelessStatus = $"Disconnected {endpoint}.";
            StatusMessage = "Wireless ADB disconnected.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not disconnect wireless ADB.");
            WirelessStatus = $"Disconnect failed: {exception.Message}";
        }
        finally
        {
            IsWirelessBusy = false;
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
            WeatherConditions? weather = EnableNetworkConditions
                ? await _weatherProvider
                    .GetCurrentAsync(coordinate, cancellationToken)
                    .ConfigureAwait(true)
                : null;
            Task<AstronomyConditions?> astronomyTask = _astronomyProvider.GetConditionsAsync(
                coordinate,
                GetLocationDate(weather?.TimeZoneId ?? SelectedLocation?.TimeZoneId),
                cancellationToken);
            Task<LightPollutionConditions?> lightPollutionTask = EnableNetworkConditions
                ? _lightPollutionProvider.GetConditionsAsync(coordinate, cancellationToken)
                : Task.FromResult<LightPollutionConditions?>(null);
            await Task.WhenAll(astronomyTask, lightPollutionTask).ConfigureAwait(true);
            AstronomyConditions? astronomy = await astronomyTask.ConfigureAwait(true);
            LightPollutionConditions? lightPollution =
                await lightPollutionTask.ConfigureAwait(true);

            if (requestVersion != Volatile.Read(ref _conditionsRequestVersion))
            {
                return;
            }

            _latestWeather = weather;
            _latestAstronomy = astronomy;
            _latestLightPollution = lightPollution;
            UpdateConditionsDisplay();
            ConditionsUpdatedText = BuildConditionsUpdatedText(
                _latestWeather,
                _latestAstronomy,
                EnableNetworkConditions);
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
    private async Task LocateCurrentUserAsync(CancellationToken cancellationToken)
    {
        if (!EnableNetworkConditions)
        {
            LocationStatusText = "Automatic location is off";
            StatusMessage = "Enable live weather and automatic location in Settings first.";
            return;
        }

        LocationStatusText = "Locating this laptop…";
        LocationCoordinateText = "Waiting for Windows location";
        TimeZoneText = "Detecting time zone…";

        LocationSearchResult? result = await _locationProvider
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(true);
        if (result is null)
        {
            LocationStatusText = SelectedLocation is null
                ? "Automatic location unavailable"
                : $"Automatic location unavailable · using {SelectedLocation.Name}";
            LocationCoordinateText = SelectedLocation?.CoordinateText
                                     ?? $"{Latitude:0.####}, {Longitude:0.####}";
            TimeZoneText = FormatTimeZoneLabel(SelectedLocation?.TimeZoneId);
            StatusMessage =
                "Windows could not determine this laptop's location. Turn on Location services or choose a fallback location.";
            if (_initialized)
            {
                await RefreshConditionsAsync(cancellationToken).ConfigureAwait(true);
            }

            return;
        }

        LocationOption automaticLocation = AddLocationIfMissing(
            LocationOption.FromSearchResult(result));
        _suppressLocationRefresh = true;
        try
        {
            SelectedLocation = automaticLocation;
        }
        finally
        {
            _suppressLocationRefresh = false;
        }

        bool isIpEstimate = automaticLocation.Name.Contains(
            "(IP estimate)",
            StringComparison.OrdinalIgnoreCase);
        LocationStatusText = isIpEstimate
            ? "Using approximate network location"
            : "Using this laptop's current location";
        LocationCoordinateText = automaticLocation.CoordinateText;
        TimeZoneText = FormatTimeZoneLabel(automaticLocation.TimeZoneId);
        StatusMessage = isIpEstimate
            ? "Windows location was unavailable. Loading live conditions from an approximate public-IP location."
            : "Current location detected. Loading live conditions for this area.";

        if (_initialized)
        {
            await RefreshConditionsAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void OpenLocationSettings()
    {
        try
        {
            Process.Start(
                new ProcessStartInfo("ms-settings:privacy-location")
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            StatusMessage = $"Windows Location settings could not be opened: {exception.Message}";
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
            _paths.SetPhoneCaptureRoot(PhoneCaptureFolder);
            _paths.SetSessionRoot(SessionDataFolder);
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            ISettingsService settings = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            await settings.SetAsync("Device.AdbPath", AdbPath, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Device.ScrcpyPath", ScrcpyPath, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Scrcpy.BitrateMbps", ScrcpyBitrateMbps, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Scrcpy.MaxResolution", ScrcpyMaxResolution, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Scrcpy.MaxFps", ScrcpyMaxFps, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Scrcpy.BoostOnCable", BoostQualityOnCable, "Device", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Overlay.ShootingZone", SelectedShootingZone, "Overlay", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Status.RefreshSeconds", StatusRefreshSeconds, "Status", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Weather.RefreshMinutes", WeatherRefreshMinutes, "Conditions", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Session.DefaultLocation", DefaultLocationName, "Session", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Session.DefaultExposureSeconds", DefaultExposureSeconds, "Session", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Session.DefaultFrameCount", DefaultFrameCount, "Session", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Files.ScreenshotFolder", ScreenshotFolder, "Files", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Files.PhoneCaptureFolder", PhoneCaptureFolder, "Files", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Files.AutoImportPhonePhotos", AutomaticallyImportPhonePhotos, "Files", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Files.SessionDataFolder", SessionDataFolder, "Files", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Startup.AutoLaunchScrcpy", AutomaticallyLaunchScrcpy, "Startup", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Startup.AutoReconnect", AutomaticallyReconnect, "Startup", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Experimental.CaptureControls", EnableExperimentalCaptureControls, "Experimental", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Experimental.ShutterX", ExperimentalShutterX, "Experimental", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Experimental.ShutterY", ExperimentalShutterY, "Experimental", _lifetimeCancellation.Token).ConfigureAwait(true);
            await settings.SetAsync("Privacy.LiveConditionsEnabled", EnableNetworkConditions, "Privacy", _lifetimeCancellation.Token).ConfigureAwait(true);
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
            await settings.SetAsync("Display.WindowMode", SelectedWindowMode, "Display", _lifetimeCancellation.Token).ConfigureAwait(true);

            _deviceToolOptions.AdbExecutablePath = string.IsNullOrWhiteSpace(AdbPath) ? null : AdbPath;
            _deviceToolOptions.ScrcpyExecutablePath = string.IsNullOrWhiteSpace(ScrcpyPath) ? null : ScrcpyPath;
            _deviceMonitorOptions.RefreshInterval = TimeSpan.FromSeconds(StatusRefreshSeconds);
            _deviceMonitorOptions.AutoReconnect = AutomaticallyReconnect;
            _photoSync.DestinationFolder = PhoneCaptureFolder;
            _photoSync.Enabled = AutomaticallyImportPhonePhotos;
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
        OnPropertyChanged(nameof(IsStacking));
        OnPropertyChanged(nameof(IsSky));
    }

    partial void OnShowNightBriefDrawerChanged(bool value)
    {
        if (value)
        {
            ShowSessionDrawer = false;
        }
    }

    partial void OnShowSessionDrawerChanged(bool value)
    {
        if (value)
        {
            ShowNightBriefDrawer = false;
        }
    }

    partial void OnShowWirelessPanelChanged(bool value)
    {
        OnPropertyChanged(nameof(MainScrcpyWindowHandle));

        if (!value)
        {
            _wirelessQrCancellation?.Cancel();
        }
    }

    partial void OnIsPreviewFullscreenChanged(bool value)
    {
        OnPropertyChanged(nameof(MainScrcpyWindowHandle));
        OnPropertyChanged(nameof(FullscreenScrcpyWindowHandle));

        if (value)
        {
            ShowNightBriefDrawer = false;
            ShowSessionDrawer = false;
            ShowWirelessPanel = false;
        }
    }

    partial void OnScrcpyWindowHandleChanged(nint value)
    {
        OnPropertyChanged(nameof(MainScrcpyWindowHandle));
        OnPropertyChanged(nameof(FullscreenScrcpyWindowHandle));
    }

    partial void OnSelectedSessionTypeChanged(SessionType value) =>
        UpdateObservingPlanDisplay();

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

        LocationStatusText = value.Name.Equals(
                "Current device location",
                StringComparison.OrdinalIgnoreCase)
            ? "Using this laptop's current location"
            : value.Name.Contains("(IP estimate)", StringComparison.OrdinalIgnoreCase)
                ? "Using approximate network location"
                : $"Using selected location: {value.Name}";
        LocationCoordinateText = value.CoordinateText;
        TimeZoneText = FormatTimeZoneLabel(value.TimeZoneId);

        if (_initialized && !_suppressLocationRefresh)
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
            UpdateManualLocationDisplay();
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
            UpdateManualLocationDisplay();
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
            _ = value
                ? LocateCurrentUserAsync(_lifetimeCancellation.Token)
                : RefreshConditionsAsync(_lifetimeCancellation.Token);
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
        BoostQualityOnCable = await settings.GetAsync("Scrcpy.BoostOnCable", BoostQualityOnCable, cancellationToken);

        // Guard the stored value: a preset that no longer exists must fall back to
        // the whole screen rather than leaving the guides drawn nowhere.
        string storedZone = await settings.GetAsync("Overlay.ShootingZone", SelectedShootingZone, cancellationToken)
                            ?? ShootingZoneFullScreen;
        SelectedShootingZone = ShootingZoneOptions.Contains(storedZone)
            ? storedZone
            : ShootingZoneFullScreen;
        StatusRefreshSeconds = await settings.GetAsync("Status.RefreshSeconds", StatusRefreshSeconds, cancellationToken);
        WeatherRefreshMinutes = await settings.GetAsync("Weather.RefreshMinutes", WeatherRefreshMinutes, cancellationToken);
        DefaultLocationName = await settings.GetAsync("Session.DefaultLocation", _appOptions.DefaultLocation, cancellationToken)
                              ?? _appOptions.DefaultLocation;
        DefaultExposureSeconds = await settings.GetAsync("Session.DefaultExposureSeconds", ExposureSeconds, cancellationToken);
        DefaultFrameCount = await settings.GetAsync("Session.DefaultFrameCount", PlannedFrameCount, cancellationToken);
        ScreenshotFolder = await settings.GetAsync("Files.ScreenshotFolder", ScreenshotFolder, cancellationToken)
                           ?? ScreenshotFolder;
        PhoneCaptureFolder = await settings.GetAsync("Files.PhoneCaptureFolder", PhoneCaptureFolder, cancellationToken)
                             ?? PhoneCaptureFolder;
        AutomaticallyImportPhonePhotos = await settings.GetAsync(
            "Files.AutoImportPhonePhotos",
            AutomaticallyImportPhonePhotos,
            cancellationToken);
        SessionDataFolder = await settings.GetAsync("Files.SessionDataFolder", SessionDataFolder, cancellationToken)
                            ?? SessionDataFolder;
        AutomaticallyLaunchScrcpy = await settings.GetAsync("Startup.AutoLaunchScrcpy", AutomaticallyLaunchScrcpy, cancellationToken);
        AutomaticallyReconnect = await settings.GetAsync("Startup.AutoReconnect", AutomaticallyReconnect, cancellationToken);
        EnableExperimentalCaptureControls = await settings.GetAsync("Experimental.CaptureControls", EnableExperimentalCaptureControls, cancellationToken);
        ExperimentalShutterX = await settings.GetAsync("Experimental.ShutterX", 0, cancellationToken);
        ExperimentalShutterY = await settings.GetAsync("Experimental.ShutterY", 0, cancellationToken);
        // The 0.1.1 key intentionally replaces the disabled-by-default 0.1.0 setting.
        EnableNetworkConditions = await settings.GetAsync(
            "Privacy.LiveConditionsEnabled",
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
        string? savedWindowMode = await settings.GetAsync(
            "Display.WindowMode",
            SelectedWindowMode,
            cancellationToken);
        SelectedWindowMode = WindowModeOptions.Contains(savedWindowMode, StringComparer.Ordinal)
            ? savedWindowMode!
            : "Windowed fullscreen";

        ExposureSeconds = DefaultExposureSeconds;
        PlannedFrameCount = DefaultFrameCount;
        TimerExposureSeconds = DefaultExposureSeconds;
        TimerFrameCount = DefaultFrameCount;
        _deviceToolOptions.AdbExecutablePath = string.IsNullOrWhiteSpace(AdbPath) ? null : AdbPath;
        _deviceToolOptions.ScrcpyExecutablePath = string.IsNullOrWhiteSpace(ScrcpyPath) ? null : ScrcpyPath;
        _deviceMonitorOptions.RefreshInterval = TimeSpan.FromSeconds(
            Math.Clamp(StatusRefreshSeconds, 5, 300));
        _deviceMonitorOptions.AutoReconnect = AutomaticallyReconnect;
        _photoSync.DestinationFolder = PhoneCaptureFolder;
        _photoSync.RemoteFolder = SelectedCameraApp.RemoteFolder;
        _photoSync.Enabled = AutomaticallyImportPhonePhotos;
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

    private void HandlePreviewWindowHandleChanged(object? sender, nint handle) =>
        Dispatch(
            () =>
            {
                // The embedded host must follow the new window; leaving it bound
                // to the destroyed one shows a permanently black preview.
                // MainScrcpyWindowHandle is computed from this, and its own
                // change notification is raised by the generated setter.
                ScrcpyWindowHandle = handle;
                StatusMessage = "Phone preview reattached.";
            });

    private void HandlePhonePhotoImported(object? sender, PhonePhotoImportedEventArgs args)
    {
        // Offer every imported capture to the stack run. The service ignores it
        // unless stack mode is armed and the target has not been met, so this is
        // safe to call unconditionally and keeps the arming decision in one place.
        _ = _stackSession.Offer(args.LocalPath, DateTimeOffset.Now);

        // Advance the unattended run only if this file genuinely completes the
        // outstanding shutter press. The file's own write time is used rather
        // than "now", because the sync poll can lag several seconds behind.
        DateTimeOffset writtenAt;
        try
        {
            writtenAt = File.Exists(args.LocalPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(args.LocalPath), TimeSpan.Zero)
                : DateTimeOffset.Now;
        }
        catch (IOException)
        {
            writtenAt = DateTimeOffset.Now;
        }

        _ = _autoCapture.NotifyFileImported(args.LocalPath, writtenAt);

        Dispatch(() => StatusMessage = $"Phone photo saved to {args.LocalPath}");
    }

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
        HourlyWeatherConditions? nearestForecast = weather?.HourlyForecast?
            .OrderBy(hour => Math.Abs((hour.Time - weather.ObservedAt).TotalMinutes))
            .FirstOrDefault();
        PrecipitationText = Format(
            nearestForecast?.PrecipitationProbabilityPercent,
            "0",
            "%");
        DewRiskText = weather?.DewRisk.ToString() ?? "Unavailable";
        AltitudeText = Format(
            SelectedLocation?.ElevationMeters ?? weather?.ElevationMeters,
            "0",
            " m above sea level");
        TimeZoneText = FormatTimeZoneLabel(
            weather?.TimeZoneId ?? SelectedLocation?.TimeZoneId,
            weather?.TimeZoneAbbreviation);

        AstronomyConditions? astronomy = _latestAstronomy;
        SunsetText = FormatLocationTime(astronomy?.Sunset);
        AstronomicalDuskText = FormatLocationTime(astronomy?.EndOfAstronomicalTwilight);
        SunriseText = FormatLocationTime(astronomy?.Sunrise);
        AstronomicalDawnText = FormatLocationTime(astronomy?.StartOfAstronomicalTwilight);
        MoonPhaseText = astronomy?.MoonPhase ?? "Unavailable";
        MoonIlluminationText = Format(astronomy?.MoonIlluminationPercent, "0", "%");
        MoonriseText = FormatLocationTime(astronomy?.Moonrise);
        MoonsetText = FormatLocationTime(astronomy?.Moonset);
        MoonAltitudeText = Format(astronomy?.MoonAltitudeDegrees, "0.0", "°");
        MoonPositionText =
            astronomy?.MoonAltitudeDegrees is { } moonAltitude &&
            astronomy.MoonAzimuthDegrees is { } moonAzimuth
                ? $"{moonAltitude:0.0}° high · {astronomy.MoonDirection ?? "?"} ({moonAzimuth:0}°)"
                : "Unavailable";
        DarkSkyWindowText =
            astronomy?.DarkSkyWindowStart is { } darkStart &&
            astronomy.DarkSkyWindowEnd is { } darkEnd
                ? $"{FormatLocationTime(darkStart)} – {FormatLocationTime(darkEnd)}"
                : "Unavailable";

        LightPollutionConditions? lightPollution = _latestLightPollution;
        LightPollutionText = lightPollution is null
            ? "Unavailable"
            : $"Zone {lightPollution.Zone} · {lightPollution.MagnitudesPerSquareArcSecond:0.0} mag/arcsec²";
        LightPollutionDetailText = lightPollution is null
            ? "Live atlas data unavailable"
            : $"{lightPollution.Description} · artificial glow {lightPollution.ArtificialBrightnessRatio:0.#}× natural · {lightPollution.DataYear} atlas";

        UpdateObservingPlanDisplay();
    }

    private void UpdateObservingPlanDisplay()
    {
        ObservingPlan plan = AstrophotographyPlanner.CreatePlan(
            SelectedSessionType,
            _latestWeather,
            _latestAstronomy,
            _latestLightPollution);
        ShootQualityLevel = plan.Quality.ToString();
        ShootingRecommendationText = plan.Headline;
        ShootingScoreText = plan.Quality == ObservingQuality.Unavailable
            ? "Unavailable"
            : $"{plan.Score}/100";
        BestShootingWindowText =
            plan.BestWindowStart is { } start &&
            plan.BestWindowEnd is { } end
                ? $"{FormatLocationTime(start)} – {FormatLocationTime(end)}"
                : "Unavailable";
        ShootingAdviceText = plan.Details;

        // Shown beside the score so a night held back by sky brightness rather
        // than weather is legible at a glance: bad weather means wait, a bright
        // sky means drive somewhere darker.
        WeatherScoreText = plan.Quality == ObservingQuality.Unavailable
            ? string.Empty
            : $"WEATHER {plan.WeatherScore}";
        IsSkyLimited = plan.Quality != ObservingQuality.Unavailable &&
                       plan.WeatherScore - plan.Score >= 15;

        // 100% is a pristine sky. This is the figure the score is multiplied by,
        // so showing it makes the rating traceable rather than mysterious.
        SkyDarknessText = plan.Quality == ObservingQuality.Unavailable
            ? "—"
            : $"{plan.SkyDarknessPercent}%";
        SkyLimitText = IsSkyLimited
            ? "LIMITING THIS TARGET"
            : string.Empty;
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

    private LocationOption AddLocationIfMissing(LocationOption location)
    {
        LocationOption? existingLocation = Locations.FirstOrDefault(
            existing =>
                Math.Abs(existing.Latitude - location.Latitude) < 0.00001 &&
                Math.Abs(existing.Longitude - location.Longitude) < 0.00001);
        if (existingLocation is not null)
        {
            return existingLocation;
        }

        Locations.Add(location);
        return location;
    }

    private void UpdateManualLocationDisplay()
    {
        LocationStatusText = "Using manual coordinates";
        LocationCoordinateText = $"{Latitude:0.####}, {Longitude:0.####}";
        TimeZoneText = EnableNetworkConditions
            ? "Resolving from live weather…"
            : FormatTimeZoneLabel(TimeZoneInfo.Local.Id);
    }

    private static DateOnly GetLocationDate(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                DateTimeOffset localNow = TimeZoneInfo.ConvertTime(
                    DateTimeOffset.UtcNow,
                    timeZone);
                return DateOnly.FromDateTime(localNow.DateTime);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return DateOnly.FromDateTime(DateTime.Now);
    }

    private static string BuildConditionsUpdatedText(
        WeatherConditions? weather,
        AstronomyConditions? astronomy,
        bool networkEnabled)
    {
        if (weather is not null)
        {
            string provider = weather.ProviderName ?? "Live weather";
            return $"{provider} · observed {weather.ObservedAt:HH:mm}";
        }

        if (astronomy is not null)
        {
            return networkEnabled
                ? $"Weather unavailable · astronomy updated {DateTime.Now:t}"
                : $"Live weather off · astronomy updated {DateTime.Now:t}";
        }

        return "Conditions unavailable";
    }

    private static string FormatTimeZoneLabel(
        string? timeZoneId,
        string? abbreviation = null)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return "Time zone unavailable";
        }

        return string.IsNullOrWhiteSpace(abbreviation) ||
               abbreviation.Equals(timeZoneId, StringComparison.OrdinalIgnoreCase)
            ? timeZoneId
            : $"{timeZoneId} ({abbreviation})";
    }

    private static string CreateRandomQrToken(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var value = new char[length];
        for (int index = 0; index < value.Length; index++)
        {
            value[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        }

        return new string(value);
    }

    private static ImageSource CreateQrCodeImage(string payload)
    {
        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        byte[] png = qrCode.GetGraphic(14);
        using var stream = new MemoryStream(png, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string NormalizeWirelessEndpoint(string endpoint, bool addDefaultPort)
    {
        string value = endpoint.Trim();
        if (value.Length == 0)
        {
            throw new InvalidOperationException("Enter the phone's Wi-Fi IP address and port.");
        }

        Match endpointMatch = Regex.Match(
            value,
            @"(?<!\d)(?<address>(?:\d{1,3}\.){3}\d{1,3}):(?<port>\d{1,5})(?!\d)",
            RegexOptions.CultureInvariant);
        if (endpointMatch.Success &&
            IPAddress.TryParse(endpointMatch.Groups["address"].Value, out IPAddress? address) &&
            int.TryParse(
                endpointMatch.Groups["port"].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int port) &&
            port is >= 1 and <= 65535)
        {
            return $"{address}:{port}";
        }

        if (!value.Contains(':', StringComparison.Ordinal) &&
            addDefaultPort &&
            IPAddress.TryParse(value, out IPAddress? defaultAddress))
        {
            return $"{defaultAddress}:5555";
        }

        throw new InvalidOperationException(
            addDefaultPort
                ? "Enter a valid phone IP address and port, for example 192.168.137.132:37123."
                : "Enter the temporary pairing IP address and port exactly as shown on the phone.");
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
        ArgumentException.ThrowIfNullOrWhiteSpace(PhoneCaptureFolder);
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

    private string FormatLocationTime(DateTimeOffset? value)
    {
        if (value is null)
        {
            return "Unavailable";
        }

        string? timeZoneId = _latestWeather?.TimeZoneId ?? SelectedLocation?.TimeZoneId;
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                return TimeZoneInfo.ConvertTime(value.Value, timeZone)
                    .ToString("HH:mm", CultureInfo.CurrentCulture);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        return _latestWeather is not null
            ? value.Value.ToOffset(_latestWeather.ObservedAt.Offset)
                .ToString("HH:mm", CultureInfo.CurrentCulture)
            : value.Value.ToLocalTime().ToString("HH:mm", CultureInfo.CurrentCulture);
    }

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
        exception switch
        {
            AstroDesk.Device.Processes.ExecutableNotFoundException =>
                $"{toolName} was not found. Install it or select its executable in Settings.",

            // The common cause is a device that is listed but no longer
            // answering, usually a wireless one that has gone off the network.
            // Naming that is more use than the raw timeout, because the fix is
            // to pick a different device rather than to try again.
            AstroDesk.Device.Processes.ToolProcessTimeoutException timeout =>
                $"The phone stopped responding, so {toolName} was given up on after " +
                $"{timeout.Timeout.TotalSeconds:0} seconds. Check the device is still " +
                "reachable, pick another under DEVICE, then try again.",

            _ => exception.Message,
        };

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

    // ---------------------------------------------------------------- stacking

    /// <summary>
    /// How many frames the run should collect before disarming itself. Zero means
    /// keep collecting until stopped by hand.
    /// </summary>
    [ObservableProperty]
    private int _stackTargetFrameCount = 30;

    [ObservableProperty]
    private bool _isStackArmed;

    [ObservableProperty]
    private int _stackCollectedCount;

    [ObservableProperty]
    private string _stackStatusText = "Stack mode is off.";

    [ObservableProperty]
    private string _stackFolderText = string.Empty;

    [ObservableProperty]
    private string _stackResultText = string.Empty;

    [ObservableProperty]
    private string _stackResultPath = string.Empty;

    [ObservableProperty]
    private bool _isStackRunning;

    /// <summary>Stretched rendition of the finished stack, for display.</summary>
    [ObservableProperty]
    private ImageSource? _stackResultImage;

    public bool HasStackResult => StackResultImage is not null;

    [ObservableProperty]
    private string _sirilPath = string.Empty;

    public ObservableCollection<StackFrameTile> StackFrameNames { get; } = [];

    public double StackProgress => StackTargetFrameCount > 0
        ? Math.Clamp((double)StackCollectedCount / StackTargetFrameCount, 0, 1)
        : 0;

    public string StackProgressText => StackTargetFrameCount > 0
        ? $"{StackCollectedCount} / {StackTargetFrameCount} frames"
        : $"{StackCollectedCount} frames";

    public bool IsSirilAvailable => _stackEngine.IsAvailable;

    [RelayCommand]
    private void ToggleStackMode()
    {
        if (_stackSession.IsArmed)
        {
            _stackSession.Disarm();
            IsStackArmed = false;
            StackStatusText = $"Stack mode stopped with {StackCollectedCount} frames collected.";
            return;
        }

        _ = TryArmStackRun();
    }

    /// <summary>
    /// Starts a fresh collection run. Returns false if the folder could not be
    /// created.
    /// </summary>
    private bool TryArmStackRun()
    {
        string root = PhoneCaptureFolder.Length > 0
            ? PhoneCaptureFolder
            : _paths.PhoneCaptureRoot;

        try
        {
            string folder = _stackSession.Arm(root, StackTargetFrameCount);
            _livePreview.Reset();
            LivePreviewImage = null;
            LivePreviewFramesText = string.Empty;
            StackFrameNames.Clear();
            StackCollectedCount = 0;
            StackResultText = string.Empty;
            StackResultPath = string.Empty;
            StackResultImage = null;
            OnPropertyChanged(nameof(HasStackResult));
            StackFolderText = folder;
            IsStackArmed = true;
            StackStatusText = StackTargetFrameCount > 0
                ? $"Armed. Every photo you take now is collected, up to {StackTargetFrameCount} frames."
                : "Armed. Every photo you take now is collected until you stop.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            StackStatusText = $"Could not start stack mode: {exception.Message}";
            return false;
        }
    }

    [RelayCommand]
    private async Task RunStackAsync()
    {
        if (IsStackRunning)
        {
            return;
        }

        _stackEngine.ExecutablePath = SirilPath;
        OnPropertyChanged(nameof(IsSirilAvailable));

        if (!_stackEngine.IsAvailable)
        {
            StackResultText =
                "Siril was not found. Install it and set the siril-cli path in Settings.";
            return;
        }

        string? extension = _stackSession.ResolveFrameExtension();
        if (extension is null || _stackSession.SessionFolder.Length == 0)
        {
            StackResultText = "There are no collected frames to stack yet.";
            return;
        }

        IsStackRunning = true;
        StackResultText = "Stacking…";
        try
        {
            var progress = new Progress<string>(line => StackResultText = line);
            StackResult result = await _stackEngine
                .StackAsync(
                    new StackRequest(_stackSession.SessionFolder, extension),
                    progress,
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);

            StackResultText = result.Message;
            StackResultPath = result.OutputPath ?? string.Empty;

            // Show the stretched rendition: the master is linear FITS, which
            // holds the real data but which nothing here can display.
            StackResultImage = result.PreviewPath is { } preview
                ? _livePreviewRenderer.LoadThumbnail(preview, 900)
                : null;
            OnPropertyChanged(nameof(HasStackResult));
            if (!result.Succeeded)
            {
                _logger.LogWarning("Stacking failed: {Message}", result.Message);
            }
        }
        catch (OperationCanceledException)
        {
            StackResultText = "Stacking was cancelled.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Stacking failed.");
            StackResultText = $"Stacking failed: {exception.Message}";
        }
        finally
        {
            IsStackRunning = false;
        }
    }

    [RelayCommand]
    private void OpenStackFolder()
    {
        if (StackFolderText.Length == 0 || !Directory.Exists(StackFolderText))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(StackFolderText) { UseShellExecute = true });
    }

    private void HandleStackFrameAdded(object? sender, StackFrameAddedEventArgs args) =>
        Dispatch(
            () =>
            {
                StackCollectedCount = args.Collected;
                StackFrameNames.Insert(
                    0,
                    new StackFrameTile(
                        Path.GetFileName(args.Frame.Path),
                        _livePreviewRenderer.LoadThumbnail(args.Frame.Path)));

                // Queued rather than decoded here: decoding and aligning a frame
                // is slow enough to stutter the UI if done on the dispatcher.
                if (IsLivePreviewEnabled)
                {
                    _livePreview.Offer(args.Frame.Path);
                }
                IsStackArmed = _stackSession.IsArmed;
                OnPropertyChanged(nameof(StackProgress));
                OnPropertyChanged(nameof(StackProgressText));
                StackStatusText = _stackSession.IsArmed
                    ? $"Collecting… {StackProgressText}"
                    : $"Target reached: {StackProgressText}. Ready to stack.";
            });

    partial void OnStackTargetFrameCountChanged(int value)
    {
        OnPropertyChanged(nameof(StackProgress));
        OnPropertyChanged(nameof(StackProgressText));
        NotifyAutoCaptureDerived();
    }

    partial void OnStackCollectedCountChanged(int value)
    {
        OnPropertyChanged(nameof(StackProgress));
        OnPropertyChanged(nameof(StackProgressText));
    }

    partial void OnSirilPathChanged(string value)
    {
        _stackEngine.ExecutablePath = value;
        OnPropertyChanged(nameof(IsSirilAvailable));
    }


    // ------------------------------------------------------------ auto capture

    /// <summary>
    /// Seconds the phone needs after the exposure ends before it can take the
    /// next frame: writing the file, and for long exposures Samsung's own noise
    /// reduction pass.
    /// </summary>
    [ObservableProperty]
    private double _autoCaptureWriteDelaySeconds = 4;

    [ObservableProperty]
    private bool _isAutoCaptureRunning;

    [ObservableProperty]
    private int _autoCaptureTriggeredCount;

    [ObservableProperty]
    private string _autoCaptureStatusText = "Auto capture is off.";

    [ObservableProperty]
    private string _weatherScoreText = string.Empty;

    /// <summary>Sky darkness as a percentage, where 100% is pristine.</summary>
    [ObservableProperty]
    private string _skyDarknessText = "—";

    [ObservableProperty]
    private string _skyLimitText = string.Empty;

    /// <summary>
    /// True when the weather would allow a good night but sky brightness will not.
    /// </summary>
    [ObservableProperty]
    private bool _isSkyLimited;

    [ObservableProperty]
    private string _autoCaptureTelemetryText = string.Empty;

    [ObservableProperty]
    private string _autoCaptureUnaccountedText = string.Empty;

    /// <summary>
    /// Seconds between shutter presses, derived from the exposure so the phone
    /// is never asked for a frame it is still busy taking.
    /// </summary>
    /// <summary>
    /// Typical seconds per frame, shown only to estimate how long a run takes.
    /// The actual cadence follows frame arrival, not this figure.
    /// </summary>
    public double AutoCaptureIntervalSeconds =>
        Math.Max(0, ExposureSeconds) + Math.Max(0, AutoCaptureWriteDelaySeconds);

    public string AutoCaptureIntervalText =>
        $"Waits for each photo to land  (~{AutoCaptureIntervalSeconds:F0}s per frame)";

    public string AutoCaptureEstimatedRunText
    {
        get
        {
            if (StackTargetFrameCount <= 0)
            {
                return "Runs until stopped.";
            }

            TimeSpan total = TimeSpan.FromSeconds(AutoCaptureIntervalSeconds * StackTargetFrameCount);
            return total.TotalHours >= 1
                ? $"About {total.TotalHours:F1} h for {StackTargetFrameCount} frames."
                : $"About {total.TotalMinutes:F0} min for {StackTargetFrameCount} frames.";
        }
    }

    [RelayCommand]
    private async Task ToggleAutoCaptureAsync()
    {
        if (_autoCapture.IsRunning)
        {
            await _autoCapture.StopAsync().ConfigureAwait(true);
            IsAutoCaptureRunning = false;
            AutoCaptureStatusText = $"Auto capture stopped after {AutoCaptureTriggeredCount} shots.";
            return;
        }

        string? serial = SelectedAdbDevice?.Serial
                         ?? _deviceMonitor.LastSnapshot?.Connection.SelectedDevice?.Serial;
        if (string.IsNullOrWhiteSpace(serial))
        {
            AutoCaptureStatusText = "Select a device before starting auto capture.";
            return;
        }

        AutoCaptureTriggeredCount = 0;

        // Arm collection first. Firing an unattended sequence while nothing is
        // collecting produces a folder of captures on the phone and an empty
        // stack run, and the failure is silent: the shutter works, the files
        // import, and the frame list simply stays at zero.
        if (!_stackSession.IsArmed && !TryArmStackRun())
        {
            AutoCaptureStatusText = StackStatusText;
            return;
        }

        try
        {
            // A volume-key shutter goes to whichever app holds the foreground, so
            // firing before the camera is up sends the whole run into nothing and
            // only surfaces at the watchdog a minute later.
            AutoCaptureStatusText = $"Opening {SelectedCameraApp.Name} on the phone…";
            CameraReadyResult ready = await _cameraForeground
                .EnsureForegroundAsync(serial, SelectedCameraApp, _lifetimeCancellation.Token)
                .ConfigureAwait(true);

            if (!ready.Ready)
            {
                AutoCaptureStatusText = ready.Message;
                return;
            }

            await _autoCapture
                .StartAsync(
                    new AutoCaptureOptions(
                        serial,
                        TimeSpan.FromSeconds(Math.Max(0, ExposureSeconds)),
                        TimeSpan.FromSeconds(Math.Max(0, AutoCaptureWriteDelaySeconds)),
                        StackTargetFrameCount),
                    _lifetimeCancellation.Token)
                .ConfigureAwait(true);

            IsAutoCaptureRunning = true;
            AutoCaptureStatusText =
                "Running. Each shot fires once the previous photo lands. " +
                "The camera app must be open and its volume key set to take pictures.";
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Auto capture could not start.");
            AutoCaptureStatusText = $"Auto capture could not start: {exception.Message}";
        }
    }

    private void HandleAutoCaptureTriggered(object? sender, AutoCaptureTick tick) =>
        Dispatch(
            () =>
            {
                AutoCaptureTriggeredCount = tick.Frame;
                AutoCaptureStatusText = tick.Total > 0
                    ? $"Shutter {tick.Frame} / {tick.Total} — waiting for the photo to land"
                    : $"Shutter {tick.Frame} — waiting for the photo to land";
            });

    private void HandleAutoCaptureFinished(object? sender, AutoCaptureFinished args) =>
        Dispatch(
            () =>
            {
                IsAutoCaptureRunning = false;
                AutoCaptureStatusText = args.Message;
                AutoCaptureTelemetryText = args.Telemetry.Summary;
            });

    private void HandleAutoCaptureTelemetry(object? sender, AutoCaptureTelemetry telemetry) =>
        Dispatch(
            () =>
            {
                AutoCaptureTelemetryText = telemetry.Summary;
                AutoCaptureUnaccountedText = telemetry.UnaccountedShutters > 0
                    ? $"{telemetry.UnaccountedShutters} shutter command(s) produced no photo"
                    : string.Empty;
            });

    partial void OnAutoCaptureWriteDelaySecondsChanged(double value) =>
        NotifyAutoCaptureDerived();

    private void NotifyAutoCaptureDerived()
    {
        OnPropertyChanged(nameof(AutoCaptureIntervalSeconds));
        OnPropertyChanged(nameof(AutoCaptureIntervalText));
        OnPropertyChanged(nameof(AutoCaptureEstimatedRunText));
    }


    // ------------------------------------------------------- live stack preview

    /// <summary>
    /// Rough running stack of the frames captured so far.
    /// </summary>
    /// <remarks>
    /// Deliberately lower quality than the final Siril stack. Its job is to show,
    /// while the session is still running, whether the frames are usable at all.
    /// </remarks>
    [ObservableProperty]
    private ImageSource? _livePreviewImage;

    [ObservableProperty]
    private string _livePreviewFramesText = string.Empty;

    [ObservableProperty]
    private string _livePreviewDriftText = string.Empty;

    [ObservableProperty]
    private bool _isLivePreviewEnabled = true;

    public bool HasLivePreview => LivePreviewImage is not null;

    private void HandleLivePreviewUpdated(object? sender, LivePreviewUpdatedEventArgs args) =>
        Dispatch(
            () =>
            {
                LivePreviewImage = args.Image;
                LivePreviewFramesText = args.FramesStacked == 1
                    ? "1 frame"
                    : $"{args.FramesStacked} frames";

                // Showing the measured drift makes it obvious when the mount has
                // been knocked, which otherwise only surfaces at the final stack.
                LivePreviewDriftText = args.Offset == FrameOffset.Zero
                    ? "aligned"
                    : $"drift {args.Offset.Dx:+0;-0;0}, {args.Offset.Dy:+0;-0;0} px";

                OnPropertyChanged(nameof(HasLivePreview));
            });

    partial void OnLivePreviewImageChanged(ImageSource? value) =>
        OnPropertyChanged(nameof(HasLivePreview));


    // --------------------------------------------------------------- camera app

    /// <summary>
    /// Camera app used for shooting. The package and its output folder move
    /// together, so selecting one both launches that app and points the photo
    /// sync at the folder it writes to.
    /// </summary>
    public IReadOnlyList<CameraApp> CameraApps { get; } = CameraApp.All;

    [ObservableProperty]
    private CameraApp _selectedCameraApp = CameraApp.Default;

    public string CameraAppFolderText => SelectedCameraApp.RemoteFolder;

    partial void OnSelectedCameraAppChanged(CameraApp value)
    {
        // Watching the wrong folder produces a run that fires the shutter,
        // captures successfully, and imports nothing.
        _photoSync.RemoteFolder = value.RemoteFolder;
        OnPropertyChanged(nameof(CameraAppFolderText));
        StatusMessage = $"Shooting with {value.Name}; watching {value.RemoteFolder}.";
    }

}

/// <summary>
/// One collected frame in the stack list, with a thumbnail.
/// </summary>
/// <remarks>
/// The thumbnail is null for a file that cannot be rendered, so the list falls
/// back to showing the name rather than an empty box.
/// </remarks>
public sealed record StackFrameTile(string FileName, ImageSource? Thumbnail)
{
    public bool HasThumbnail => Thumbnail is not null;
}
