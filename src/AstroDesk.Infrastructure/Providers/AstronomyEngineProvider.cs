using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using CosineKitty;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Infrastructure.Providers;

public sealed class AstronomyEngineProvider(
    ILogger<AstronomyEngineProvider> logger) : IAstronomyProvider
{
    private readonly ILogger<AstronomyEngineProvider> _logger = logger;

    public string Name => "Astronomy Engine (local)";

    public Task<AstronomyConditions?> GetConditionsAsync(
        GeoCoordinate coordinate,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            DateTime noonUtc = DateTime.SpecifyKind(
                date.ToDateTime(new TimeOnly(12, 0)),
                DateTimeKind.Utc);
            AstroTime searchStart = new(noonUtc);
            DateTimeOffset calculatedAt = DateTimeOffset.UtcNow;
            AstroTime now = new(calculatedAt.UtcDateTime);
            Observer observer = new(coordinate.Latitude, coordinate.Longitude, 0);

            AstroTime? sunset = Astronomy.SearchRiseSet(
                Body.Sun,
                observer,
                Direction.Set,
                searchStart,
                2,
                0);
            AstroTime? twilightEnd = Astronomy.SearchAltitude(
                Body.Sun,
                observer,
                Direction.Set,
                searchStart,
                2,
                -18);
            AstroTime? sunrise = Astronomy.SearchRiseSet(
                Body.Sun,
                observer,
                Direction.Rise,
                searchStart,
                2,
                0);
            AstroTime? twilightStart = Astronomy.SearchAltitude(
                Body.Sun,
                observer,
                Direction.Rise,
                searchStart,
                2,
                -18);
            AstroTime? moonrise = Astronomy.SearchRiseSet(
                Body.Moon,
                observer,
                Direction.Rise,
                searchStart,
                2,
                0);
            AstroTime? moonset = Astronomy.SearchRiseSet(
                Body.Moon,
                observer,
                Direction.Set,
                searchStart,
                2,
                0);

            double phaseAngle = Astronomy.MoonPhase(now);
            IllumInfo illumination = Astronomy.Illumination(Body.Moon, now);
            Equatorial equatorial = Astronomy.Equator(
                Body.Moon,
                now,
                observer,
                EquatorEpoch.OfDate,
                Aberration.Corrected);
            Topocentric horizontal = Astronomy.Horizon(
                now,
                observer,
                equatorial.ra,
                equatorial.dec,
                Refraction.Normal);

            DateTimeOffset? darkStart = ToDateTimeOffset(twilightEnd);
            DateTimeOffset? darkEnd = ToDateTimeOffset(twilightStart);
            if (darkStart is not null && darkEnd is not null && darkEnd <= darkStart)
            {
                AstroTime nextDawnStart = twilightStart!.AddDays(1);
                darkEnd = ToDateTimeOffset(nextDawnStart);
            }

            AstronomyConditions result = new(
                ToDateTimeOffset(sunset),
                ToDateTimeOffset(twilightEnd),
                ToDateTimeOffset(sunrise),
                ToDateTimeOffset(twilightStart),
                GetMoonPhaseName(phaseAngle),
                illumination.phase_fraction * 100,
                ToDateTimeOffset(moonrise),
                ToDateTimeOffset(moonset),
                horizontal.altitude,
                darkStart,
                darkEnd,
                Name,
                calculatedAt,
                horizontal.azimuth,
                GetCompassDirection(horizontal.azimuth),
                CalculateHourlyMoonPositions(observer, calculatedAt, 36));

            return Task.FromResult<AstronomyConditions?>(result);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Local astronomy calculations are unavailable.");
            return Task.FromResult<AstronomyConditions?>(null);
        }
    }

    private static DateTimeOffset? ToDateTimeOffset(AstroTime? time) =>
        time is null
            ? null
            : new DateTimeOffset(time.ToUtcDateTime(), TimeSpan.Zero);

    private static string GetMoonPhaseName(double angle)
    {
        double normalized = ((angle % 360) + 360) % 360;
        return normalized switch
        {
            < 22.5 or >= 337.5 => "New Moon",
            < 67.5 => "Waxing Crescent",
            < 112.5 => "First Quarter",
            < 157.5 => "Waxing Gibbous",
            < 202.5 => "Full Moon",
            < 247.5 => "Waning Gibbous",
            < 292.5 => "Third Quarter",
            _ => "Waning Crescent",
        };
    }

    private static IReadOnlyList<HourlyMoonPosition> CalculateHourlyMoonPositions(
        Observer observer,
        DateTimeOffset start,
        int hours)
    {
        List<HourlyMoonPosition> positions = new(hours + 1);
        for (int hour = 0; hour <= hours; hour++)
        {
            DateTimeOffset time = start.AddHours(hour);
            AstroTime astroTime = new(time.UtcDateTime);
            Equatorial equatorial = Astronomy.Equator(
                Body.Moon,
                astroTime,
                observer,
                EquatorEpoch.OfDate,
                Aberration.Corrected);
            Topocentric horizontal = Astronomy.Horizon(
                astroTime,
                observer,
                equatorial.ra,
                equatorial.dec,
                Refraction.Normal);
            positions.Add(
                new HourlyMoonPosition(
                    time,
                    horizontal.altitude,
                    horizontal.azimuth));
        }

        return positions;
    }

    private static string GetCompassDirection(double azimuth)
    {
        string[] directions =
        [
            "N",
            "NNE",
            "NE",
            "ENE",
            "E",
            "ESE",
            "SE",
            "SSE",
            "S",
            "SSW",
            "SW",
            "WSW",
            "W",
            "WNW",
            "NW",
            "NNW",
        ];
        double normalized = ((azimuth % 360) + 360) % 360;
        int index = (int)Math.Round(normalized / 22.5, MidpointRounding.AwayFromZero) %
                    directions.Length;
        return directions[index];
    }
}
