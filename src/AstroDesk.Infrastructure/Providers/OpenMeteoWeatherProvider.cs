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
            "&hourly=temperature_2m,relative_humidity_2m,dew_point_2m,precipitation_probability,cloud_cover,visibility,wind_speed_10m" +
            "&timezone=auto&forecast_hours=36";

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

            DateTimeOffset observedAt = ParseObservedAt(
                current.Time,
                response?.UtcOffsetSeconds);

            return new WeatherConditions(
                current.Temperature,
                current.RelativeHumidity,
                current.WindSpeed,
                current.CloudCover,
                current.VisibilityMeters / 1000,
                current.DewPoint,
                dewRisk,
                Name,
                observedAt,
                response?.TimeZone,
                response?.TimeZoneAbbreviation,
                response?.Elevation,
                ParseHourlyForecast(response?.Hourly, response?.UtcOffsetSeconds));
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

        [JsonPropertyName("utc_offset_seconds")]
        public int? UtcOffsetSeconds { get; init; }

        [JsonPropertyName("timezone")]
        public string? TimeZone { get; init; }

        [JsonPropertyName("timezone_abbreviation")]
        public string? TimeZoneAbbreviation { get; init; }

        [JsonPropertyName("elevation")]
        public double? Elevation { get; init; }

        [JsonPropertyName("hourly")]
        public OpenMeteoHourly? Hourly { get; init; }
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

    private sealed class OpenMeteoHourly
    {
        [JsonPropertyName("time")]
        public IReadOnlyList<string?>? Time { get; init; }

        [JsonPropertyName("temperature_2m")]
        public IReadOnlyList<double?>? Temperature { get; init; }

        [JsonPropertyName("relative_humidity_2m")]
        public IReadOnlyList<double?>? RelativeHumidity { get; init; }

        [JsonPropertyName("wind_speed_10m")]
        public IReadOnlyList<double?>? WindSpeed { get; init; }

        [JsonPropertyName("cloud_cover")]
        public IReadOnlyList<double?>? CloudCover { get; init; }

        [JsonPropertyName("visibility")]
        public IReadOnlyList<double?>? VisibilityMeters { get; init; }

        [JsonPropertyName("dew_point_2m")]
        public IReadOnlyList<double?>? DewPoint { get; init; }

        [JsonPropertyName("precipitation_probability")]
        public IReadOnlyList<double?>? PrecipitationProbability { get; init; }
    }

    private static IReadOnlyList<HourlyWeatherConditions> ParseHourlyForecast(
        OpenMeteoHourly? hourly,
        int? utcOffsetSeconds)
    {
        if (hourly?.Time is not { Count: > 0 } times)
        {
            return [];
        }

        List<HourlyWeatherConditions> forecast = new(times.Count);
        for (int index = 0; index < times.Count; index++)
        {
            DateTimeOffset? time = ParseTime(times[index], utcOffsetSeconds);
            if (time is null)
            {
                continue;
            }

            forecast.Add(
                new HourlyWeatherConditions(
                    time.Value,
                    GetAt(hourly.Temperature, index),
                    GetAt(hourly.RelativeHumidity, index),
                    GetAt(hourly.WindSpeed, index),
                    GetAt(hourly.CloudCover, index),
                    GetAt(hourly.VisibilityMeters, index) / 1000,
                    GetAt(hourly.DewPoint, index),
                    GetAt(hourly.PrecipitationProbability, index)));
        }

        return forecast;
    }

    private static double? GetAt(IReadOnlyList<double?>? values, int index) =>
        values is not null && index < values.Count ? values[index] : null;

    private static DateTimeOffset ParseObservedAt(string? value, int? utcOffsetSeconds)
        => ParseTime(value, utcOffsetSeconds) ?? DateTimeOffset.UtcNow;

    private static DateTimeOffset? ParseTime(string? value, int? utcOffsetSeconds)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out DateTime localTime))
        {
            return null;
        }

        int seconds = Math.Clamp(utcOffsetSeconds ?? 0, -18 * 60 * 60, 18 * 60 * 60);
        return new DateTimeOffset(
            DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            TimeSpan.FromSeconds(seconds));
    }
}
