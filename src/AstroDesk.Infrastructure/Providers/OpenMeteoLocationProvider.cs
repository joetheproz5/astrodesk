using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Infrastructure.Providers;

public sealed class OpenMeteoLocationProvider(
    HttpClient httpClient,
    ILogger<OpenMeteoLocationProvider> logger) : ILocationProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<OpenMeteoLocationProvider> _logger = logger;

    public string Name => "Open-Meteo Geocoding";

    public async Task<IReadOnlyList<LocationSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return [];
        }

        string uri = $"v1/search?name={Uri.EscapeDataString(query.Trim())}&count=20&language=en&format=json";
        try
        {
            GeocodingResponse? response = await _httpClient
                .GetFromJsonAsync<GeocodingResponse>(uri, cancellationToken)
                .ConfigureAwait(false);

            return response?.Results?
                       .Where(static item =>
                           item.Name is not null &&
                           item.Latitude is >= -90 and <= 90 &&
                           item.Longitude is >= -180 and <= 180)
                       .Select(static item => new LocationSearchResult(
                           BuildDisplayName(item),
                           new GeoCoordinate(item.Latitude!.Value, item.Longitude!.Value),
                           item.CountryCode,
                           item.TimeZone,
                           item.Elevation))
                       .ToArray()
                   ?? [];
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
            _logger.LogWarning(exception, "Location search is unavailable.");
            return [];
        }
    }

    public Task<LocationSearchResult?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.LogInformation(
            "Current-device location is unavailable because AstroDesk has not been granted a Windows location source.");
        return Task.FromResult<LocationSearchResult?>(null);
    }

    private static string BuildDisplayName(GeocodingResult item)
    {
        string[] parts =
        [
            item.Name!,
            item.Admin1 ?? string.Empty,
            item.Country ?? string.Empty,
        ];
        return string.Join(
            ", ",
            parts.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct());
    }

    private sealed class GeocodingResponse
    {
        [JsonPropertyName("results")]
        public IReadOnlyList<GeocodingResult>? Results { get; init; }
    }

    private sealed class GeocodingResult
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("latitude")]
        public double? Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; init; }

        [JsonPropertyName("elevation")]
        public double? Elevation { get; init; }

        [JsonPropertyName("country_code")]
        public string? CountryCode { get; init; }

        [JsonPropertyName("timezone")]
        public string? TimeZone { get; init; }

        [JsonPropertyName("country")]
        public string? Country { get; init; }

        [JsonPropertyName("admin1")]
        public string? Admin1 { get; init; }
    }
}
