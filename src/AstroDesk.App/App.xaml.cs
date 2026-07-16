using System.Windows;
using System.Windows.Threading;
using AstroDesk.App.Configuration;
using AstroDesk.App.Services;
using AstroDesk.App.ViewModels;
using AstroDesk.Capture.Frames;
using AstroDesk.Capture.Geometry;
using AstroDesk.Capture.Histogram;
using AstroDesk.Capture.Screenshots;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Services;
using AstroDesk.Data;
using AstroDesk.Data.Initialization;
using AstroDesk.Device;
using AstroDesk.Device.Adb;
using AstroDesk.Device.Input;
using AstroDesk.Device.Processes;
using AstroDesk.Device.Scrcpy;
using AstroDesk.Device.Toolbar;
using AstroDesk.Infrastructure.Logging;
using AstroDesk.Infrastructure.Providers;
using AstroDesk.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AstroDesk.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        try
        {
            _host = BuildHost();
            await _host.StartAsync();

            await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
            {
                IDatabaseInitializer initializer =
                    scope.ServiceProvider.GetRequiredService<IDatabaseInitializer>();
                await initializer.InitializeAsync();
            }

            RegisterExceptionHandlers();
            MainWindow window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"AstroDesk could not start.{Environment.NewLine}{Environment.NewLine}" +
                $"{exception.Message}{Environment.NewLine}{Environment.NewLine}" +
                "Review the local Logs folder for details.",
                "AstroDesk startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try
            {
                await _host.StopAsync(TimeSpan.FromSeconds(8));
                if (_host is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else
                {
                    _host.Dispose();
                }
            }
            catch
            {
                // Shutdown must continue even if a child process or provider is already gone.
            }
        }

        base.OnExit(e);
    }

    private static IHost BuildHost()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration.Sources.Clear();
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(prefix: "ASTRODESK_");

        AstroDeskAppOptions appOptions = new();
        builder.Configuration.GetSection("AstroDesk").Bind(appOptions);
        AppPaths paths = new();
        paths.EnsureCreated();

        builder.Logging.ClearProviders();
        builder.Services.Configure<RollingFileLoggerOptions>(
            options =>
            {
                options.DirectoryPath = paths.LogRoot;
                options.RetainedFileCount = 14;
                options.MaximumFileSizeBytes = 5 * 1024 * 1024;
            });
        builder.Services.AddSingleton<ILoggerProvider, RollingFileLoggerProvider>();

        builder.Services.AddSingleton(appOptions);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<ISessionAssetService, SessionAssetService>();

        builder.Services.AddAstroDeskData(
            options =>
            {
                options.DatabasePath = paths.DatabasePath;
                options.CommandTimeoutSeconds = 30;
                options.EnableDetailedErrors = true;
                options.EnableSensitiveDataLogging = false;
            });
        builder.Services.AddScoped<ISessionService, SessionService>();
        builder.Services.AddScoped<ISettingsService, SettingsService>();
        builder.Services.AddSingleton(TimeProvider.System);

        builder.Services.AddHttpClient<OpenMeteoWeatherProvider>(
            client =>
            {
                client.BaseAddress = new Uri("https://api.open-meteo.com/");
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AstroDesk/0.1.4");
            });
        builder.Services.AddHttpClient<OpenMeteoLocationProvider>(
            client =>
            {
                client.BaseAddress = new Uri("https://geocoding-api.open-meteo.com/");
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AstroDesk/0.1.4");
            });
        builder.Services.AddHttpClient<BigDataCloudIpLocationProvider>(
            client =>
            {
                client.BaseAddress = new Uri("https://api.bigdatacloud.net/");
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AstroDesk/0.1.4");
            });
        builder.Services.AddHttpClient<DavidLorenzLightPollutionProvider>(
            client =>
            {
                client.BaseAddress = new Uri("https://djlorenz.github.io/");
                client.Timeout = TimeSpan.FromSeconds(20);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AstroDesk/0.1.4");
            });
        builder.Services.AddTransient<IWeatherProvider>(
            provider => provider.GetRequiredService<OpenMeteoWeatherProvider>());
        builder.Services.AddTransient<ILightPollutionProvider>(
            provider => provider.GetRequiredService<DavidLorenzLightPollutionProvider>());
        builder.Services.AddTransient<ILocationProvider, WindowsLocationProvider>();
        builder.Services.AddSingleton<IAstronomyProvider, AstronomyEngineProvider>();

        builder.Services.AddSingleton(
            new DeviceToolOptions
            {
                AdbExecutablePath = Environment.GetEnvironmentVariable("ASTRODESK_ADB_PATH"),
                ScrcpyExecutablePath = Environment.GetEnvironmentVariable("ASTRODESK_SCRCPY_PATH"),
            });
        builder.Services.AddSingleton(
            new DeviceMonitorOptions
            {
                RefreshInterval = TimeSpan.FromSeconds(
                    Math.Clamp(appOptions.StatusRefreshSeconds, 5, 300)),
                AutoReconnect = appOptions.AutomaticallyReconnect,
                ReconnectCooldown = TimeSpan.FromSeconds(30),
            });
        builder.Services.AddSingleton<IExecutableLocator, ExecutableLocator>();
        builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
        builder.Services.AddSingleton<AdbService>();
        builder.Services.AddSingleton<IAdbCommandExecutor>(
            provider => provider.GetRequiredService<AdbService>());
        builder.Services.AddSingleton<IAdbDeviceClient>(
            provider => provider.GetRequiredService<AdbService>());
        builder.Services.AddSingleton<IAdbInputService, AdbInputService>();
        builder.Services.AddSingleton<IScrcpyWindowManager, Win32ScrcpyWindowManager>();
        builder.Services.AddSingleton<IScrcpyService, ScrcpyService>();
        builder.Services.AddSingleton<IWindowMessageSink, NativeWindowMessageSink>();
        builder.Services.AddSingleton<IClipboardBridge, Win32ClipboardBridge>();
        builder.Services.AddSingleton<IInputForwarder, Win32InputForwarder>();
        builder.Services.AddSingleton<IInputRouter, InputRouter>();
        builder.Services.AddSingleton(
            new DeviceToolbarCapabilities
            {
                SupportsClipboardPaste = true,
                SupportsKeepAwake = true,
                SupportsScreenOffWhileMirroring = false,
            });
        builder.Services.AddSingleton<IDeviceToolbarController, DeviceToolbarController>();
        builder.Services.AddSingleton<IDeviceMonitor, DeviceMonitor>();

        builder.Services.AddSingleton<CoordinateMapper>();
        builder.Services.AddSingleton<IWindowCaptureService, Win32WindowCaptureService>();
        builder.Services.AddSingleton<HistogramAnalyzer>();
        builder.Services.AddSingleton<ThrottledHistogramProcessor>();
        builder.Services.AddSingleton<PreviewScreenshotWriter>();

        builder.Services.AddSingleton<INightModeService, NightModeService>();
        builder.Services.AddSingleton<IExposureTimerService, ExposureTimerService>();
        builder.Services.AddSingleton<IPhonePreviewCoordinator, PhonePreviewCoordinator>();
        builder.Services.AddSingleton<IPreviewInputCoordinator, PreviewInputCoordinator>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private void RegisterExceptionHandlers()
    {
        DispatcherUnhandledException += HandleDispatcherException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += HandleUnhandledException;
    }

    private void HandleDispatcherException(
        object sender,
        DispatcherUnhandledExceptionEventArgs args)
    {
        ILogger<App>? logger = _host?.Services.GetService<ILogger<App>>();
        logger?.LogError(args.Exception, "Unhandled UI exception.");
        MessageBox.Show(
            MainWindow,
            $"AstroDesk recovered from an unexpected UI error.{Environment.NewLine}{Environment.NewLine}{args.Exception.Message}",
            "AstroDesk error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        args.Handled = true;
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        ILogger<App>? logger = _host?.Services.GetService<ILogger<App>>();
        logger?.LogError(args.Exception, "Unobserved background task exception.");
        args.SetObserved();
    }

    private void HandleUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
        {
            ILogger<App>? logger = _host?.Services.GetService<ILogger<App>>();
            logger?.LogCritical(exception, "Unhandled process exception.");
        }
    }
}
