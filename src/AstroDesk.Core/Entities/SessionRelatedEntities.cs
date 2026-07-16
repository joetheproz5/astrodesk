using AstroDesk.Core.Enums;
using AstroDesk.Core.Models;

namespace AstroDesk.Core.Entities;

public sealed class SessionWeatherSnapshot : EntityBase
{
    private SessionWeatherSnapshot()
    {
        ShootingSession = null!;
    }

    public SessionWeatherSnapshot(WeatherConditions conditions, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ValidateFinite(conditions.TemperatureCelsius, nameof(conditions.TemperatureCelsius));
        ValidatePercentage(conditions.HumidityPercent, nameof(conditions.HumidityPercent));
        ValidatePercentage(conditions.CloudCoverPercent, nameof(conditions.CloudCoverPercent));
        ValidateNonNegative(
            conditions.WindSpeedKilometersPerHour,
            nameof(conditions.WindSpeedKilometersPerHour));
        ValidateNonNegative(
            conditions.VisibilityKilometers,
            nameof(conditions.VisibilityKilometers));
        ValidateFinite(conditions.DewPointCelsius, nameof(conditions.DewPointCelsius));
        ShootingSession = null!;
        CapturedAt = capturedAt;
        TemperatureCelsius = conditions.TemperatureCelsius;
        HumidityPercent = conditions.HumidityPercent;
        WindSpeedKilometersPerHour = conditions.WindSpeedKilometersPerHour;
        CloudCoverPercent = conditions.CloudCoverPercent;
        VisibilityKilometers = conditions.VisibilityKilometers;
        DewPointCelsius = conditions.DewPointCelsius;
        DewRisk = conditions.DewRisk;
        ProviderName = OptionalText(conditions.ProviderName, 200);
    }

    public Guid ShootingSessionId { get; private set; }

    public ShootingSession ShootingSession { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public double? TemperatureCelsius { get; private set; }

    public double? HumidityPercent { get; private set; }

    public double? WindSpeedKilometersPerHour { get; private set; }

    public double? CloudCoverPercent { get; private set; }

    public double? VisibilityKilometers { get; private set; }

    public double? DewPointCelsius { get; private set; }

    public DewRisk DewRisk { get; private set; }

    public string? ProviderName { get; private set; }

    internal void AttachTo(ShootingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ShootingSessionId != Guid.Empty && ShootingSessionId != session.Id)
        {
            throw new InvalidOperationException("Weather snapshot already belongs to another session.");
        }

        ShootingSessionId = session.Id;
        ShootingSession = session;
    }

    private static void ValidatePercentage(double? value, string paramName)
    {
        if (value is { } number && (!double.IsFinite(number) || number is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(paramName, "Percentage must be between 0 and 100.");
        }
    }

    private static void ValidateNonNegative(double? value, string paramName)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
        {
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite and non-negative.");
        }
    }

    private static void ValidateFinite(double? value, string paramName)
    {
        if (value is { } number && !double.IsFinite(number))
        {
            throw new ArgumentOutOfRangeException(paramName, "Value must be finite.");
        }
    }

    private static string? OptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maxLength} characters.");
        }

        return trimmed;
    }
}

public sealed class SessionAstronomySnapshot : EntityBase
{
    private SessionAstronomySnapshot()
    {
        ShootingSession = null!;
    }

    public SessionAstronomySnapshot(AstronomyConditions conditions, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (conditions.MoonIlluminationPercent is { } illumination &&
            (!double.IsFinite(illumination) || illumination is < 0 or > 100))
        {
            throw new ArgumentOutOfRangeException(
                nameof(conditions.MoonIlluminationPercent),
                "Moon illumination must be between 0 and 100.");
        }

        if (conditions.MoonAltitudeDegrees is { } altitude &&
            (!double.IsFinite(altitude) || altitude is < -90 or > 90))
        {
            throw new ArgumentOutOfRangeException(
                nameof(conditions.MoonAltitudeDegrees),
                "Moon altitude must be between -90 and 90.");
        }

        if (conditions.DarkSkyWindowStart is { } darkSkyStart &&
            conditions.DarkSkyWindowEnd is { } darkSkyEnd &&
            darkSkyEnd < darkSkyStart)
        {
            throw new ArgumentException("Dark-sky window end cannot precede its start.", nameof(conditions));
        }

        ShootingSession = null!;
        CapturedAt = capturedAt;
        Sunset = conditions.Sunset;
        EndOfAstronomicalTwilight = conditions.EndOfAstronomicalTwilight;
        Sunrise = conditions.Sunrise;
        StartOfAstronomicalTwilight = conditions.StartOfAstronomicalTwilight;
        MoonPhase = OptionalText(conditions.MoonPhase, 100);
        MoonIlluminationPercent = conditions.MoonIlluminationPercent;
        Moonrise = conditions.Moonrise;
        Moonset = conditions.Moonset;
        MoonAltitudeDegrees = conditions.MoonAltitudeDegrees;
        DarkSkyWindowStart = conditions.DarkSkyWindowStart;
        DarkSkyWindowEnd = conditions.DarkSkyWindowEnd;
        ProviderName = OptionalText(conditions.ProviderName, 200);
    }

    public Guid ShootingSessionId { get; private set; }

    public ShootingSession ShootingSession { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public DateTimeOffset? Sunset { get; private set; }

    public DateTimeOffset? EndOfAstronomicalTwilight { get; private set; }

    public DateTimeOffset? Sunrise { get; private set; }

    public DateTimeOffset? StartOfAstronomicalTwilight { get; private set; }

    public string? MoonPhase { get; private set; }

    public double? MoonIlluminationPercent { get; private set; }

    public DateTimeOffset? Moonrise { get; private set; }

    public DateTimeOffset? Moonset { get; private set; }

    public double? MoonAltitudeDegrees { get; private set; }

    public DateTimeOffset? DarkSkyWindowStart { get; private set; }

    public DateTimeOffset? DarkSkyWindowEnd { get; private set; }

    public string? ProviderName { get; private set; }

    internal void AttachTo(ShootingSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (ShootingSessionId != Guid.Empty && ShootingSessionId != session.Id)
        {
            throw new InvalidOperationException("Astronomy snapshot already belongs to another session.");
        }

        ShootingSessionId = session.Id;
        ShootingSession = session;
    }

    private static string? OptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maxLength} characters.");
        }

        return trimmed;
    }
}

public sealed class SessionScreenshot : EntityBase
{
    private SessionScreenshot()
    {
        FilePath = string.Empty;
        ImageFormat = "PNG";
        ShootingSession = null!;
    }

    public SessionScreenshot(
        Guid shootingSessionId,
        string filePath,
        bool includesOverlays,
        DateTimeOffset capturedAt,
        string imageFormat = "PNG")
    {
        if (shootingSessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID is required.", nameof(shootingSessionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageFormat);
        var normalizedPath = filePath.Trim();
        var normalizedFormat = imageFormat.Trim().ToUpperInvariant();
        if (normalizedPath.Length > 2048)
        {
            throw new ArgumentOutOfRangeException(nameof(filePath), "File path cannot exceed 2048 characters.");
        }

        if (normalizedFormat.Length > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(imageFormat), "Image format cannot exceed 16 characters.");
        }

        ShootingSessionId = shootingSessionId;
        FilePath = normalizedPath;
        IncludesOverlays = includesOverlays;
        CapturedAt = capturedAt;
        ImageFormat = normalizedFormat;
        ShootingSession = null!;
    }

    public Guid ShootingSessionId { get; private set; }

    public ShootingSession ShootingSession { get; private set; }

    public string FilePath { get; private set; }

    public bool IncludesOverlays { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public string ImageFormat { get; private set; }
}

public sealed class SessionNote : EntityBase
{
    private SessionNote()
    {
        Content = string.Empty;
        ShootingSession = null!;
    }

    public SessionNote(
        Guid shootingSessionId,
        string content,
        SessionNoteKind kind,
        DateTimeOffset notedAt)
    {
        if (shootingSessionId == Guid.Empty)
        {
            throw new ArgumentException("Session ID is required.", nameof(shootingSessionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ShootingSessionId = shootingSessionId;
        Content = content.Trim();
        Kind = kind;
        NotedAt = notedAt;
        ShootingSession = null!;
    }

    public Guid ShootingSessionId { get; private set; }

    public ShootingSession ShootingSession { get; private set; }

    public string Content { get; private set; }

    public SessionNoteKind Kind { get; private set; }

    public DateTimeOffset NotedAt { get; private set; }

    public void UpdateContent(string content, DateTimeOffset timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        Content = content.Trim();
        MarkUpdated(timestamp);
    }
}
