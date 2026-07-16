using System.Net;
using System.Text;
using AstroDesk.Core.Models;
using AstroDesk.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Core.Tests;

public sealed class OpenMeteoWeatherProviderTests
{
    [Fact]
    public async Task GetCurrentAsync_MapsLiveConditionsAndResolvedTimeZone()
    {
        const string responseJson =
            """
            {
              "utc_offset_seconds": 10800,
              "timezone": "Asia/Beirut",
              "timezone_abbreviation": "GMT+3",
              "elevation": 112,
              "current": {
                "time": "2026-07-16T15:30",
                "temperature_2m": 27.4,
                "relative_humidity_2m": 61,
                "wind_speed_10m": 13.2,
                "cloud_cover": 18,
                "visibility": 24100,
                "dew_point_2m": 19.2
              },
              "hourly": {
                "time": ["2026-07-16T21:00", "2026-07-16T22:00"],
                "temperature_2m": [23.1, 22.4],
                "relative_humidity_2m": [68, 71],
                "wind_speed_10m": [8.2, 7.1],
                "cloud_cover": [12, 9],
                "visibility": [28000, 30000],
                "dew_point_2m": [17.0, 17.1],
                "precipitation_probability": [4, 2]
              }
            }
            """;
        RecordingHttpMessageHandler handler = new(responseJson);
        using HttpClient client = new(handler)
        {
            BaseAddress = new Uri("https://api.open-meteo.com/"),
        };
        OpenMeteoWeatherProvider provider = new(
            client,
            NullLogger<OpenMeteoWeatherProvider>.Instance);

        WeatherConditions? result = await provider.GetCurrentAsync(
            new GeoCoordinate(33.8938, 35.5018));

        Assert.NotNull(result);
        Assert.Equal(27.4, result.TemperatureCelsius);
        Assert.Equal(61, result.HumidityPercent);
        Assert.Equal(24.1, result.VisibilityKilometers);
        Assert.Equal("Asia/Beirut", result.TimeZoneId);
        Assert.Equal("GMT+3", result.TimeZoneAbbreviation);
        Assert.Equal(112, result.ElevationMeters);
        Assert.Equal(TimeSpan.FromHours(3), result.ObservedAt.Offset);
        Assert.Equal(15, result.ObservedAt.Hour);
        Assert.Equal(30, result.ObservedAt.Minute);
        Assert.NotNull(result.HourlyForecast);
        Assert.Collection(
            result.HourlyForecast,
            first =>
            {
                Assert.Equal(21, first.Time.Hour);
                Assert.Equal(TimeSpan.FromHours(3), first.Time.Offset);
                Assert.Equal(28, first.VisibilityKilometers);
                Assert.Equal(4, first.PrecipitationProbabilityPercent);
            },
            second =>
            {
                Assert.Equal(22, second.Time.Hour);
                Assert.Equal(9, second.CloudCoverPercent);
            });
        Assert.NotNull(handler.RequestUri);
        Assert.Contains("timezone=auto", handler.RequestUri.Query, StringComparison.Ordinal);
        Assert.Contains(
            "current=temperature_2m",
            handler.RequestUri.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            "hourly=temperature_2m",
            handler.RequestUri.Query,
            StringComparison.Ordinal);
        Assert.Contains("forecast_hours=36", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    private sealed class RecordingHttpMessageHandler(string responseJson) : HttpMessageHandler
    {
        private readonly string _responseJson = responseJson;

        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        _responseJson,
                        Encoding.UTF8,
                        "application/json"),
                });
        }
    }
}
