using AstroDesk.Core.Calculations;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Models;
using Xunit;

namespace AstroDesk.Core.Tests;

/// <summary>
/// Covers sky brightness gating the rating rather than nudging it.
/// </summary>
/// <remarks>
/// Under the previous model light pollution was one weighted term of seven, so
/// the entire range from a pristine site to an inner-city sky moved the total by
/// about ten points. A cloudless night under Bortle 6 therefore scored in the
/// high eighties and was reported as "Excellent for Milky Way", which is the
/// opposite of useful: it sends you out to shoot something the sky cannot give.
/// </remarks>
public sealed class SkyglowScoringTests
{
    [Fact]
    public void PerfectWeatherUnderABrightSkyIsNotExcellentForTheMilkyWay()
    {
        // The reported case: cloudless, dry, still, moonless, Bortle 6.
        ObservingPlan plan = Plan(SessionType.MilkyWay, zone: "6a");

        Assert.NotEqual(ObservingQuality.Excellent, plan.Quality);
        Assert.True(
            plan.Score < 40,
            $"a Bortle 6 sky should not rate {plan.Score}/100 for the Milky Way");
    }

    [Fact]
    public void TheSameNightIsStillExcellentForTheMoon()
    {
        // The Moon is bright enough to shoot from a city centre, so the same
        // sky must not be penalised for it.
        ObservingPlan plan = Plan(SessionType.Moon, zone: "6a");

        Assert.Equal(ObservingQuality.Excellent, plan.Quality);
        Assert.True(plan.Score >= 80, $"score was {plan.Score}");
    }

    [Fact]
    public void ADarkSiteScoresWellForTheMilkyWay()
    {
        ObservingPlan plan = Plan(SessionType.MilkyWay, zone: "1");

        Assert.True(plan.Score >= 80, $"a Bortle 1 site should rate highly, got {plan.Score}");
        Assert.Equal(ObservingQuality.Excellent, plan.Quality);
    }

    [Fact]
    public void TheWeatherScoreStaysVisibleSeparately()
    {
        // "The weather is fine, the sky is the problem" is actionable in a way
        // that one merged number is not: bad weather means wait, a bright sky
        // means drive somewhere darker.
        ObservingPlan plan = Plan(SessionType.MilkyWay, zone: "6a");

        Assert.True(plan.WeatherScore >= 80, $"weather score was {plan.WeatherScore}");
        Assert.True(plan.WeatherScore > plan.Score);
    }

    [Fact]
    public void TheDetailNamesTheSkyAsTheConstraint()
    {
        // The reason lives beside the measurement that causes it rather than
        // appended to the headline, where it was being truncated.
        ObservingPlan plan = Plan(SessionType.MilkyWay, zone: "6a");

        Assert.Contains("sky brightness", plan.Details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Poor for Milky Way", plan.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void SkyDarknessIsReportedSoTheScoreIsTraceable()
    {
        // This is the figure the weather score is multiplied by, so publishing it
        // turns the rating from a mysterious number into something checkable.
        ObservingPlan bright = Plan(SessionType.MilkyWay, zone: "6a");
        ObservingPlan dark = Plan(SessionType.MilkyWay, zone: "1");

        Assert.True(bright.SkyDarknessPercent < 40, $"was {bright.SkyDarknessPercent}");
        Assert.True(dark.SkyDarknessPercent > 90, $"was {dark.SkyDarknessPercent}");
    }

    [Fact]
    public void CloudStillMattersWhenTheSkyIsDark()
    {
        // Gating on darkness must not make the weather irrelevant.
        ObservingPlan clear = Plan(SessionType.MilkyWay, zone: "1", cloud: 2);
        ObservingPlan overcast = Plan(SessionType.MilkyWay, zone: "1", cloud: 95);

        Assert.True(
            overcast.Score < clear.Score - 20,
            $"overcast {overcast.Score} should be far below clear {clear.Score}");
        Assert.NotEqual(ObservingQuality.Excellent, overcast.Quality);
    }

    [Fact]
    public void AnOvercastNightAtADarkSiteIsNotWorthDrivingTo()
    {
        // The case cloud gating exists for. While cloud was a weighted term this
        // rated 62 and read as "Fair" at 95% cover, because cloud forfeited only
        // its own third while the rest of the terms paid out in full.
        ObservingPlan plan = Plan(SessionType.MilkyWay, zone: "1", cloud: 95);

        Assert.Equal(ObservingQuality.Poor, plan.Quality);
        Assert.True(plan.Score < 15, $"a 95% overcast night should not rate {plan.Score}/100");
    }

    [Fact]
    public void CloudIsReportedAsWeatherRatherThanSkyBrightness()
    {
        // Cloud gates the weather score itself, unlike sky brightness which gates
        // the total. If it did not, the weather readout would stay in the high
        // nineties under solid overcast and simply be untrue.
        ObservingPlan overcast = Plan(SessionType.MilkyWay, zone: "1", cloud: 95);

        Assert.True(
            overcast.WeatherScore < 15,
            $"the weather score should reflect the cloud, was {overcast.WeatherScore}");
    }

    [Fact]
    public void SolidOvercastIsPoorEvenForTheMoon()
    {
        // Sky brightness is target-dependent; cloud is not. The Moon is shot from
        // city centres but not through a closed lid.
        ObservingPlan plan = Plan(SessionType.Moon, zone: "6a", cloud: 98);

        Assert.Equal(ObservingQuality.Poor, plan.Quality);
    }

    [Fact]
    public void BrokenCloudCostsMoreThanTheFractionOfSkyItCovers()
    {
        // Frames are minutes long and the cloud drifts, so half the sky covered
        // ruins well over half the exposures.
        Assert.True(
            AstrophotographyPlanner.CloudFactor(50) < 0.5,
            $"was {AstrophotographyPlanner.CloudFactor(50)}");

        Assert.Equal(1.0, AstrophotographyPlanner.CloudFactor(0), 3);
        Assert.Equal(0.0, AstrophotographyPlanner.CloudFactor(100), 3);
    }

    [Fact]
    public void CloudGatingIsMonotonic()
    {
        // More cloud must never score better, at any point on the curve.
        for (int percent = 0; percent < 100; percent += 5)
        {
            Assert.True(
                AstrophotographyPlanner.CloudFactor(percent) >
                AstrophotographyPlanner.CloudFactor(percent + 5),
                $"{percent}% did not beat {percent + 5}%");
        }
    }

    [Fact]
    public void AClearNightIsBarelyPenalisedForCloud()
    {
        // The gate must not quietly tax good nights: this is the common case and
        // it has to keep reading as excellent.
        ObservingPlan plan = Plan(SessionType.MilkyWay, zone: "1", cloud: 2);

        Assert.Equal(ObservingQuality.Excellent, plan.Quality);
        Assert.True(plan.Score >= 85, $"a clear dark night should rate highly, got {plan.Score}");
    }

    [Fact]
    public void UnknownCloudIsAssumedMostlyClearButNotCertain() =>
        // Missing data must not read as a guaranteed clear sky, nor bury an
        // otherwise fine forecast.
        Assert.InRange(AstrophotographyPlanner.CloudFactor(null), 0.7, 0.95);

    [Theory]
    [InlineData(SessionType.MilkyWay, 1.0)]
    [InlineData(SessionType.DeepSky, 1.0)]
    [InlineData(SessionType.Moon, 0.0)]
    [InlineData(SessionType.Planet, 0.0)]
    public void SensitivityMatchesHowMuchTheTargetNeedsDarkness(
        SessionType sessionType,
        double expected) =>
        Assert.Equal(expected, AstrophotographyPlanner.SkyglowSensitivity(sessionType));

    [Fact]
    public void AnInsensitiveTargetIsNeverScaled() =>
        Assert.Equal(1.0, AstrophotographyPlanner.SkyglowFactor(SessionType.Moon, skyglowScore: 0));

    [Fact]
    public void AFullySensitiveTargetTracksTheSkyDirectly()
    {
        Assert.Equal(1.0, AstrophotographyPlanner.SkyglowFactor(SessionType.MilkyWay, 100));
        Assert.Equal(0.2, AstrophotographyPlanner.SkyglowFactor(SessionType.MilkyWay, 20), 3);
        Assert.Equal(0.0, AstrophotographyPlanner.SkyglowFactor(SessionType.MilkyWay, 0));
    }

    /// <summary>
    /// A near-perfect night: clear, dry, still, and moonless.
    /// </summary>
    private static ObservingPlan Plan(SessionType sessionType, string zone, double cloud = 2)
    {
        DateTimeOffset start = new(2026, 7, 18, 22, 0, 0, TimeSpan.Zero);

        HourlyWeatherConditions[] forecast =
        [
            .. Enumerable.Range(0, 8).Select(hour => new HourlyWeatherConditions(
                Time: start.AddHours(hour),
                TemperatureCelsius: 18,
                HumidityPercent: 45,
                WindSpeedKilometersPerHour: 4,
                CloudCoverPercent: cloud,
                VisibilityKilometers: 40,
                DewPointCelsius: 6,
                PrecipitationProbabilityPercent: 0)),
        ];

        var weather = new WeatherConditions(
            TemperatureCelsius: 18,
            HumidityPercent: 45,
            WindSpeedKilometersPerHour: 4,
            CloudCoverPercent: cloud,
            VisibilityKilometers: 40,
            DewPointCelsius: 6,
            DewRisk: DewRisk.Low,
            ProviderName: "test",
            ObservedAt: start,
            HourlyForecast: forecast);

        var astronomy = new AstronomyConditions(
            Sunset: start.AddHours(-2),
            EndOfAstronomicalTwilight: start.AddHours(-1),
            Sunrise: start.AddHours(8),
            StartOfAstronomicalTwilight: start.AddHours(7),
            MoonPhase: "New",
            MoonIlluminationPercent: 0,
            Moonrise: null,
            Moonset: null,
            MoonAltitudeDegrees: -30,
            DarkSkyWindowStart: start,
            DarkSkyWindowEnd: start.AddHours(8),
            ProviderName: "test",
            CalculatedAt: start);

        var lightPollution = new LightPollutionConditions(
            ArtificialBrightnessRatio: 1,
            MagnitudesPerSquareArcSecond: 19.1,
            Zone: zone,
            Description: "test",
            ProviderName: "test",
            DataYear: 2024,
            CalculatedAt: start);

        return AstrophotographyPlanner.CreatePlan(
            sessionType, weather, astronomy, lightPollution, start);
    }
}
