using AstroDesk.Core.Enums;
using AstroDesk.Core.Models;

namespace AstroDesk.Core.Calculations;

public static class AstrophotographyPlanner
{
    public static ObservingPlan CreatePlan(
        SessionType sessionType,
        WeatherConditions? weather,
        AstronomyConditions? astronomy,
        LightPollutionConditions? lightPollution,
        DateTimeOffset? now = null)
    {
        if (weather?.HourlyForecast is not { Count: > 0 } forecast)
        {
            return new ObservingPlan(
                ObservingQuality.Unavailable,
                0,
                null,
                null,
                "Forecast unavailable",
                "An hourly weather forecast is required to rate tonight.");
        }

        DateTimeOffset current = now ?? weather.ObservedAt;
        DateTimeOffset searchEnd = current.AddHours(30);
        DateTimeOffset? darkStart = astronomy?.DarkSkyWindowStart ?? astronomy?.Sunset;
        DateTimeOffset? darkEnd = astronomy?.DarkSkyWindowEnd ?? astronomy?.Sunrise;
        if (darkStart is not null &&
            darkEnd is not null &&
            darkEnd <= darkStart)
        {
            darkEnd = darkEnd.Value.AddDays(1);
        }

        List<ScoredHour> scoredHours = forecast
            .Where(hour => hour.Time >= current.AddMinutes(-30) && hour.Time <= searchEnd)
            .Where(
                hour =>
                    darkStart is null ||
                    darkEnd is null ||
                    (hour.Time >= darkStart && hour.Time <= darkEnd))
            .Select(
                hour => ScoreHour(
                    sessionType,
                    hour,
                    astronomy,
                    lightPollution))
            .OrderBy(hour => hour.Time)
            .ToList();

        if (scoredHours.Count == 0)
        {
            return new ObservingPlan(
                ObservingQuality.Unavailable,
                0,
                null,
                null,
                "No dark forecast window",
                "The available forecast does not overlap tonight's dark-sky window.");
        }

        double highestScore = scoredHours.Max(hour => hour.Score);
        double qualifyingScore = Math.Max(55, highestScore - 12);
        List<List<ScoredHour>> windows = [];
        foreach (ScoredHour hour in scoredHours.Where(hour => hour.Score >= qualifyingScore))
        {
            List<ScoredHour>? currentWindow = windows.LastOrDefault();
            if (currentWindow is null ||
                hour.Time - currentWindow[^1].Time > TimeSpan.FromMinutes(90))
            {
                currentWindow = [];
                windows.Add(currentWindow);
            }

            currentWindow.Add(hour);
        }

        List<ScoredHour> bestWindow = windows
            .OrderByDescending(window => window.Average(hour => hour.Score))
            .ThenByDescending(window => window.Count)
            .FirstOrDefault()
            ?? [scoredHours.OrderByDescending(hour => hour.Score).First()];

        int score = (int)Math.Round(bestWindow.Average(hour => hour.Score));
        ObservingQuality quality = GetQuality(score);
        DateTimeOffset windowStart = bestWindow[0].Time;
        DateTimeOffset windowEnd = bestWindow[^1].Time.AddHours(1);
        string target = GetTargetLabel(sessionType);

        return new ObservingPlan(
            quality,
            score,
            windowStart,
            windowEnd,
            $"{quality} for {target}",
            BuildDetails(bestWindow, lightPollution));
    }

    private static ScoredHour ScoreHour(
        SessionType sessionType,
        HourlyWeatherConditions weather,
        AstronomyConditions? astronomy,
        LightPollutionConditions? lightPollution)
    {
        double cloud = ScoreLowerIsBetter(weather.CloudCoverPercent, 1.4);
        double precipitation = ScoreLowerIsBetter(
            weather.PrecipitationProbabilityPercent,
            1.5);
        double visibility = weather.VisibilityKilometers is { } visibilityKilometers
            ? Clamp(visibilityKilometers / 25 * 100)
            : 50;
        double humidity = weather.HumidityPercent is { } humidityPercent
            ? Clamp((100 - humidityPercent) * 2)
            : 50;
        double dew = ScoreDewSpread(
            weather.TemperatureCelsius,
            weather.DewPointCelsius);
        double moisture = (humidity + dew) / 2;
        double wind = ScoreLowerIsBetter(weather.WindSpeedKilometersPerHour, 3);
        double moon = ScoreMoon(sessionType, weather.Time, astronomy);
        double skyglow = ScoreLightPollution(sessionType, lightPollution);

        double score = sessionType is SessionType.Moon or SessionType.Planet
            ? (cloud * 0.38) +
              (precipitation * 0.18) +
              (visibility * 0.12) +
              (moisture * 0.10) +
              (wind * 0.12) +
              (moon * 0.08) +
              (skyglow * 0.02)
            : (cloud * 0.30) +
              (precipitation * 0.10) +
              (visibility * 0.10) +
              (moisture * 0.15) +
              (wind * 0.10) +
              (moon * 0.15) +
              (skyglow * 0.10);

        return new ScoredHour(
            weather.Time,
            Clamp(score),
            weather.CloudCoverPercent,
            weather.PrecipitationProbabilityPercent,
            weather.VisibilityKilometers,
            weather.WindSpeedKilometersPerHour,
            FindMoonAltitude(weather.Time, astronomy));
    }

    private static double ScoreMoon(
        SessionType sessionType,
        DateTimeOffset time,
        AstronomyConditions? astronomy)
    {
        double? altitude = FindMoonAltitude(time, astronomy);
        if (sessionType == SessionType.Moon)
        {
            return altitude is null
                ? 50
                : Clamp(Math.Max(0, altitude.Value) / 45 * 100);
        }

        if (sessionType == SessionType.Planet)
        {
            return 85;
        }

        if (altitude is null ||
            altitude <= 0 ||
            astronomy?.MoonIlluminationPercent is not { } illumination)
        {
            return 100;
        }

        double altitudeFactor = Math.Min(1, altitude.Value / 45);
        return Clamp(100 - ((illumination / 100) * altitudeFactor * 85));
    }

    private static double? FindMoonAltitude(
        DateTimeOffset time,
        AstronomyConditions? astronomy)
    {
        HourlyMoonPosition? nearest = astronomy?.MoonPositions?
            .OrderBy(position => Math.Abs((position.Time - time).TotalMinutes))
            .FirstOrDefault();
        return nearest is not null &&
               Math.Abs((nearest.Time - time).TotalMinutes) <= 90
            ? nearest.AltitudeDegrees
            : astronomy?.MoonAltitudeDegrees;
    }

    private static double ScoreLightPollution(
        SessionType sessionType,
        LightPollutionConditions? lightPollution)
    {
        if (sessionType is SessionType.Moon or SessionType.Planet)
        {
            return 90;
        }

        return lightPollution?.Zone.FirstOrDefault() switch
        {
            '0' => 100,
            '1' => 95,
            '2' => 88,
            '3' => 76,
            '4' => 58,
            '5' => 38,
            '6' => 20,
            '7' => 7,
            _ => 60,
        };
    }

    private static double ScoreDewSpread(double? temperature, double? dewPoint)
    {
        if (temperature is null || dewPoint is null)
        {
            return 50;
        }

        double spread = temperature.Value - dewPoint.Value;
        return spread switch
        {
            <= 0 => 0,
            < 2 => spread / 2 * 35,
            < 5 => 35 + ((spread - 2) / 3 * 45),
            < 8 => 80 + ((spread - 5) / 3 * 20),
            _ => 100,
        };
    }

    private static double ScoreLowerIsBetter(double? value, double multiplier) =>
        value is null ? 50 : Clamp(100 - (value.Value * multiplier));

    private static string BuildDetails(
        IReadOnlyCollection<ScoredHour> hours,
        LightPollutionConditions? lightPollution)
    {
        double? cloud = Average(hours.Select(hour => hour.CloudCoverPercent));
        double? precipitation = Average(
            hours.Select(hour => hour.PrecipitationProbabilityPercent));
        double? visibility = Average(hours.Select(hour => hour.VisibilityKilometers));
        double? wind = Average(hours.Select(hour => hour.WindSpeedKilometersPerHour));
        double? moonAltitude = Average(hours.Select(hour => hour.MoonAltitudeDegrees));
        List<string> details = [];

        details.Add(cloud switch
        {
            <= 20 => "low cloud",
            >= 60 => "heavy cloud",
            null => "cloud unknown",
            _ => "some cloud",
        });

        if (precipitation >= 40)
        {
            details.Add("rain risk");
        }
        else if (precipitation is not null)
        {
            details.Add("low rain risk");
        }

        details.Add(visibility switch
        {
            >= 20 => "good visibility",
            < 10 => "poor visibility",
            null => "visibility unknown",
            _ => "moderate visibility",
        });

        if (wind >= 25)
        {
            details.Add("strong wind");
        }

        if (moonAltitude <= 0)
        {
            details.Add("Moon below horizon");
        }

        if (lightPollution?.Zone.FirstOrDefault() is '6' or '7')
        {
            details.Add("strong local skyglow");
        }

        return string.Join(" · ", details.Take(4));
    }

    private static double? Average(IEnumerable<double?> values)
    {
        double[] available = values
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();
        return available.Length == 0 ? null : available.Average();
    }

    private static ObservingQuality GetQuality(int score) =>
        score switch
        {
            >= 80 => ObservingQuality.Excellent,
            >= 65 => ObservingQuality.Good,
            >= 50 => ObservingQuality.Fair,
            _ => ObservingQuality.Poor,
        };

    private static string GetTargetLabel(SessionType sessionType) =>
        sessionType switch
        {
            SessionType.MilkyWay => "Milky Way",
            SessionType.Starscape => "starscapes",
            SessionType.StarTrails => "star trails",
            SessionType.Moon => "Moon",
            SessionType.Planet => "planets",
            SessionType.Constellation => "constellations",
            SessionType.DeepSky => "deep sky",
            SessionType.Timelapse => "a timelapse",
            SessionType.TestSession => "a test session",
            _ => "this session",
        };

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);

    private sealed record ScoredHour(
        DateTimeOffset Time,
        double Score,
        double? CloudCoverPercent,
        double? PrecipitationProbabilityPercent,
        double? VisibilityKilometers,
        double? WindSpeedKilometersPerHour,
        double? MoonAltitudeDegrees);
}
