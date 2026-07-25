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
        int weatherScore = (int)Math.Round(bestWindow.Average(hour => hour.WeatherScore));
        ObservingQuality quality = GetQuality(score);
        DateTimeOffset windowStart = bestWindow[0].Time;
        DateTimeOffset windowEnd = bestWindow[^1].Time.AddHours(1);
        string target = GetTargetLabel(sessionType);

        // The reason lives in its own telemetry cell rather than being appended
        // here: the score tile truncated the suffix, and a constraint reads
        // better beside the measurement that causes it.
        string headline = $"{quality} for {target}";

        return new ObservingPlan(
            quality,
            score,
            windowStart,
            windowEnd,
            headline,
            BuildDetails(bestWindow, lightPollution, weatherScore, score),
            weatherScore,
            (int)Math.Round(ScoreLightPollution(sessionType, lightPollution)));
    }

    private static ScoredHour ScoreHour(
        SessionType sessionType,
        HourlyWeatherConditions weather,
        AstronomyConditions? astronomy,
        LightPollutionConditions? lightPollution)
    {
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

        // Weather only, and cloud is deliberately not a term here either. Sky
        // darkness is applied after this because it is not weather; cloud is
        // applied to this because it is. Both are gates rather than ingredients:
        // neither trades off against how still or dry the air is.
        double conditions = sessionType is SessionType.Moon or SessionType.Planet
            ? (precipitation * 0.30) +
              (visibility * 0.20) +
              (moisture * 0.16) +
              (wind * 0.21) +
              (moon * 0.13)
            : (precipitation * 0.16) +
              (visibility * 0.16) +
              (moisture * 0.26) +
              (wind * 0.16) +
              (moon * 0.26);

        double weatherScore = Clamp(conditions) * CloudFactor(weather.CloudCoverPercent);
        double score = weatherScore * SkyglowFactor(sessionType, skyglow);

        return new ScoredHour(
            weather.Time,
            Clamp(score),
            Clamp(weatherScore),
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

    /// <summary>
    /// Multiplier applied to the weather score for the forecast cloud cover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cloud used to be the heaviest weighted term, which sounds severe but was
    /// not: at 95% cover it forfeited its own third of the total while the
    /// remaining two thirds paid out in full, so a fully overcast night at a dark
    /// site still rated 62 and was reported as fair. Nothing is shootable through
    /// solid overcast, whatever the wind and humidity are doing, so cloud belongs
    /// in the same category as sky brightness: a gate on the result rather than
    /// an ingredient trading off against the others.
    /// </para>
    /// <para>
    /// The curve is superlinear because broken cloud costs more than the fraction
    /// of sky it covers. Frames are minutes long and the cloud drifts, so a third
    /// of the sky covered ruins well over a third of the exposures, and every
    /// ruined frame has to be found and thrown out of the stack.
    /// </para>
    /// <para>
    /// A forecast reports how much sky is covered, never how thick the cloud is,
    /// so cover is treated as opaque. That is wrong for the thin high haze the
    /// Moon punches straight through, and the error is deliberately on the
    /// pessimistic side: being told to stay in on a night that turned out usable
    /// costs less than driving to a dark site under cloud.
    /// </para>
    /// </remarks>
    public static double CloudFactor(double? cloudCoverPercent)
    {
        if (cloudCoverPercent is null)
        {
            return 0.85;
        }

        double covered = Math.Clamp(cloudCoverPercent.Value / 100.0, 0, 1);
        return Math.Pow(1 - covered, 1.4);
    }

    /// <summary>
    /// How much a target depends on a dark sky, from 0 (indifferent) to 1.
    /// </summary>
    /// <remarks>
    /// The Moon is bright enough to shoot from a city centre; the Milky Way is
    /// not. Treating light pollution as one weighted term among several implied
    /// they were comparable concerns, and it was not: under the previous model
    /// sky darkness moved the total by at most ten points, so a cloudless night
    /// under Bortle 6 scored in the high eighties and was reported as excellent
    /// for the Milky Way. No amount of transparency, stillness or moonlessness
    /// makes a washed-out core appear.
    /// </remarks>
    public static double SkyglowSensitivity(SessionType sessionType) =>
        sessionType switch
        {
            // Faint extended structure. Sky brightness is the whole game.
            SessionType.MilkyWay or SessionType.DeepSky => 1.0,

            // Wide fields still want darkness, but a bright foreground subject
            // or bright stars survive a compromised sky.
            SessionType.Starscape => 0.85,
            SessionType.StarTrails or SessionType.Constellation => 0.5,
            SessionType.Timelapse => 0.4,

            // Bright targets. These are shot from balconies in cities.
            SessionType.Moon or SessionType.Planet => 0.0,

            _ => 0.3,
        };

    /// <summary>
    /// Multiplier applied to the weather score for the chosen target.
    /// </summary>
    /// <remarks>
    /// A multiplier rather than a term, so a bright sky caps what the night can
    /// be worth instead of costing it a few points. At full sensitivity a Bortle
    /// 6 sky yields roughly a fifth of the weather score, which is the honest
    /// answer for the Milky Way.
    /// </remarks>
    public static double SkyglowFactor(SessionType sessionType, double skyglowScore)
    {
        double sensitivity = SkyglowSensitivity(sessionType);
        if (sensitivity <= 0)
        {
            return 1.0;
        }

        double normalised = Math.Clamp(skyglowScore / 100.0, 0, 1);
        return 1.0 - (sensitivity * (1.0 - normalised));
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
        LightPollutionConditions? lightPollution,
        int weatherScore,
        int score)
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

        // Lead with the constraint when the sky, not the weather, is what is
        // holding the night back. Truncation below would otherwise drop it.
        if (weatherScore - score >= 15)
        {
            details.Insert(0, $"weather {weatherScore}/100 but sky brightness limits this target");
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
        double WeatherScore,
        double? CloudCoverPercent,
        double? PrecipitationProbabilityPercent,
        double? VisibilityKilometers,
        double? WindSpeedKilometersPerHour,
        double? MoonAltitudeDegrees);
}
