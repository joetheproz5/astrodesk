using AstroDesk.Core.Calculations;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Models;

namespace AstroDesk.Core.Tests;

public sealed class AstrophotographyPlannerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 18, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void CreatePlan_RatesClearDarkConditionsAsExcellent()
    {
        WeatherConditions weather = CreateWeather(
            cloud: 5,
            precipitation: 0,
            visibility: 30,
            humidity: 50,
            wind: 5,
            temperature: 20,
            dewPoint: 10);
        AstronomyConditions astronomy = CreateAstronomy(
            illumination: 20,
            moonAltitude: -20);
        LightPollutionConditions lightPollution = CreateLightPollution("2a");

        ObservingPlan result = AstrophotographyPlanner.CreatePlan(
            SessionType.MilkyWay,
            weather,
            astronomy,
            lightPollution,
            Now);

        Assert.Equal(ObservingQuality.Excellent, result.Quality);
        Assert.InRange(result.Score, 80, 100);
        Assert.NotNull(result.BestWindowStart);
        Assert.NotNull(result.BestWindowEnd);
        Assert.Contains("low cloud", result.Details, StringComparison.Ordinal);
        Assert.Contains("good visibility", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlan_RatesCloudRainAndPoorVisibilityAsPoor()
    {
        WeatherConditions weather = CreateWeather(
            cloud: 95,
            precipitation: 90,
            visibility: 2,
            humidity: 98,
            wind: 40,
            temperature: 15,
            dewPoint: 14.8);
        AstronomyConditions astronomy = CreateAstronomy(
            illumination: 100,
            moonAltitude: 60);

        ObservingPlan result = AstrophotographyPlanner.CreatePlan(
            SessionType.DeepSky,
            weather,
            astronomy,
            CreateLightPollution("7b"),
            Now);

        Assert.Equal(ObservingQuality.Poor, result.Quality);
        Assert.InRange(result.Score, 0, 34);
        Assert.Contains("heavy cloud", result.Details, StringComparison.Ordinal);
        Assert.Contains("rain risk", result.Details, StringComparison.Ordinal);
        Assert.Contains("poor visibility", result.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlan_UsesTargetSpecificMoonAndSkyglowWeighting()
    {
        WeatherConditions weather = CreateWeather(
            cloud: 10,
            precipitation: 0,
            visibility: 25,
            humidity: 55,
            wind: 5,
            temperature: 20,
            dewPoint: 10);
        AstronomyConditions astronomy = CreateAstronomy(
            illumination: 100,
            moonAltitude: 60);
        LightPollutionConditions lightPollution = CreateLightPollution("7b");

        ObservingPlan milkyWay = AstrophotographyPlanner.CreatePlan(
            SessionType.MilkyWay,
            weather,
            astronomy,
            lightPollution,
            Now);
        ObservingPlan moon = AstrophotographyPlanner.CreatePlan(
            SessionType.Moon,
            weather,
            astronomy,
            lightPollution,
            Now);

        Assert.True(moon.Score > milkyWay.Score);
        Assert.Contains("Moon", moon.Headline, StringComparison.Ordinal);
    }

    private static WeatherConditions CreateWeather(
        double cloud,
        double precipitation,
        double visibility,
        double humidity,
        double wind,
        double temperature,
        double dewPoint)
    {
        IReadOnlyList<HourlyWeatherConditions> forecast = Enumerable
            .Range(0, 7)
            .Select(
                hour => new HourlyWeatherConditions(
                    Now.AddHours(3 + hour),
                    temperature,
                    humidity,
                    wind,
                    cloud,
                    visibility,
                    dewPoint,
                    precipitation))
            .ToArray();
        return new WeatherConditions(
            temperature,
            humidity,
            wind,
            cloud,
            visibility,
            dewPoint,
            DewRisk.Low,
            "Test",
            Now,
            "Asia/Beirut",
            "GMT+3",
            100,
            forecast);
    }

    private static AstronomyConditions CreateAstronomy(
        double illumination,
        double moonAltitude)
    {
        IReadOnlyList<HourlyMoonPosition> moonPositions = Enumerable
            .Range(0, 7)
            .Select(
                hour => new HourlyMoonPosition(
                    Now.AddHours(3 + hour),
                    moonAltitude,
                    180))
            .ToArray();
        return new AstronomyConditions(
            Now.AddHours(1),
            Now.AddHours(3),
            Now.AddHours(13),
            Now.AddHours(10),
            "Test Moon",
            illumination,
            null,
            null,
            moonAltitude,
            Now.AddHours(3),
            Now.AddHours(10),
            "Test",
            Now,
            180,
            "S",
            moonPositions);
    }

    private static LightPollutionConditions CreateLightPollution(string zone) =>
        new(
            1,
            20,
            zone,
            "test sky",
            "Test",
            2024,
            Now);
}
