using AstroDesk.Core.Enums;

namespace AstroDesk.App.Configuration;

public sealed class AstroDeskAppOptions
{
    public int StatusRefreshSeconds { get; set; } = 20;

    public int WeatherRefreshMinutes { get; set; } = 15;

    public bool AutomaticallyLaunchScrcpy { get; set; }

    public bool AutomaticallyReconnect { get; set; } = true;

    public int ScrcpyBitrateMbps { get; set; } = 12;

    public int ScrcpyMaxResolution { get; set; } = 1920;

    public int ScrcpyMaxFps { get; set; } = 60;

    public double DefaultExposureSeconds { get; set; } = 20;

    public int DefaultFrameCount { get; set; } = 120;

    public string DefaultLocation { get; set; } = "Faraya";

    public NightDisplayMode NightMode { get; set; } = NightDisplayMode.NormalDark;

    public bool EnableExperimentalCaptureControls { get; set; }

    public bool EnableNetworkConditions { get; set; } = true;
}
