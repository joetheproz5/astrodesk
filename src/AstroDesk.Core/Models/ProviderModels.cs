using AstroDesk.Core.Enums;

namespace AstroDesk.Core.Models;

public readonly record struct GeoCoordinate
{
    public GeoCoordinate(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
        }

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }
}

public sealed record WeatherConditions(
    double? TemperatureCelsius,
    double? HumidityPercent,
    double? WindSpeedKilometersPerHour,
    double? CloudCoverPercent,
    double? VisibilityKilometers,
    double? DewPointCelsius,
    DewRisk DewRisk,
    string? ProviderName,
    DateTimeOffset ObservedAt,
    string? TimeZoneId = null,
    string? TimeZoneAbbreviation = null);

public sealed record AstronomyConditions(
    DateTimeOffset? Sunset,
    DateTimeOffset? EndOfAstronomicalTwilight,
    DateTimeOffset? Sunrise,
    DateTimeOffset? StartOfAstronomicalTwilight,
    string? MoonPhase,
    double? MoonIlluminationPercent,
    DateTimeOffset? Moonrise,
    DateTimeOffset? Moonset,
    double? MoonAltitudeDegrees,
    DateTimeOffset? DarkSkyWindowStart,
    DateTimeOffset? DarkSkyWindowEnd,
    string? ProviderName,
    DateTimeOffset CalculatedAt);

public sealed record LocationSearchResult(
    string DisplayName,
    GeoCoordinate Coordinate,
    string? CountryCode = null,
    string? TimeZoneId = null,
    double? ElevationMeters = null);
