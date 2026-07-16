using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AstroDesk.Core.Calculations;
using AstroDesk.Core.Enums;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Infrastructure.Providers;

public sealed class OpenMeteoWeatherProvider(
    HttpClient httpClient,
    ILogger<OpenMeteoWeatherProvider> logger) : IWeatherProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OpenMeteoWeatherProvider> _logger = logger;

    public string Name => "Open-Meteo";

    public async Task<WeatherConditions?> GetCurrentAsync(
        GeoCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        string latitude = coordinate.Latitude.ToString("0.######", CultureInfo.InvariantCulture);
        string longitude = coordinate.Longitude.ToString("0.######", CultureInfo.InvariantCulture);
        string requestUri =
            $"v1/forecast?latitude={latitude}&longitude={longitude}" +
            "&current=temperature_2m,relative_humidity_2m,wind_speed_10m,cloud_cover,visibility,dew_point_2m" +
            "&timezone=auto&forecast_days=1";

        try
        {
            OpenMeteoWeatherResponse? response = await _httpClient
                .GetFromJsonAsync<OpenMeteoWeatherResponse>(requestUri, cancellationToken)
                .ConfigureAwait(false);

            OpenMeteoCurrent? current = response?.Current;
            if (current is null)
            {
                _logger.LogWarning("Open-Meteo returned no current conditions.");
                return null;
            }

            DewRisk dewRisk = current.Temperature is { } temperature &&
                              current.DewPoint is { } dewPoint
                ? MeteorologyCalculations.AssessDewRisk(temperature, dewPoint)
                : DewRisk.Unavailable;

            DateTimeOffset observedAt = DateTimeOffset.TryParse(
                current.Time,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsed)
                ? parsed
                : DateTimeOffset.UtcNow;

            return new WeatherConditions(
                current.Temperature,
                current.RelativeHumidity,
                current.WindSpeed,
                current.CloudCover,
                current.VisibilityMeters / 1000,
                current.DewPoint,
                dewRisk,
                Name,
                observedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                NotSupportedException or
                System.Text.Json.JsonException)
        {
            _logger.LogWarning(exception, "Open-Meteo weather data is unavailable.");
            return null;
        }
    }

    private sealed class OpenMeteoWeatherResponse
    {
        [JsonPropertyName("current")]
        public OpenMeteoCurrent? Current { get; init; }
    }

    private sealed class OpenMeteoCurrent
    {
        [JsonPropertyName("time")]
        public string? Time { get; init; }

        [JsonPropertyName("temperature_2m")]
        public double? Temperature { get; init; }

        [JsonPropertyName("relative_humidity_2m")]
        public double? RelativeHumidity { get; init; }

        [JsonPropertyName("wind_speed_10m")]
        public double? WindSpeed { get; init; }

        [JsonPropertyName("cloud_cover")]
        public double? CloudCover { get; init; }

        [JsonPropertyName("visibility")]
        public double? VisibilityMeters { get; init; }

        [JsonPropertyName("dew_point_2m")]
        public double? DewPoint { get; init; }
    }
}
