using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AstroDesk.Core.Models;
using Microsoft.Extensions.Logging;

namespace AstroDesk.Infrastructure.Providers;

public sealed class BigDataCloudIpLocationProvider(
    HttpClient httpClient,
    ILogger<BigDataCloudIpLocationProvider> logger)
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<BigDataCloudIpLocationProvider> _logger = logger;

    public async Task<LocationSearchResult?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            IpLocationResponse? response = await _httpClient
                .GetFromJsonAsync<IpLocationResponse>(
                    "data/reverse-geocode-client?localityLanguage=en",
                    cancellationToken)
                .ConfigureAwait(false);
            if (response?.Latitude is not (>= -90 and <= 90) ||
                response.Longitude is not (>= -180 and <= 180))
            {
                _logger.LogWarning("BigDataCloud returned no valid IP-based coordinates.");
                return null;
            }

            string displayName = BuildDisplayName(response);
            string? timeZoneId = response.LocalityInfo?.Informative?
                .FirstOrDefault(
                    static item => item.Description?.Equals(
                        "time zone",
                        StringComparison.OrdinalIgnoreCase) == true)
                ?.Name;
            return new LocationSearchResult(
                $"{displayName} (IP estimate)",
                new GeoCoordinate(response.Latitude.Value, response.Longitude.Value),
                response.CountryCode,
                timeZoneId);
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
            _logger.LogWarning(exception, "IP-based location fallback is unavailable.");
            return null;
        }
    }

    private static string BuildDisplayName(IpLocationResponse response)
    {
        string[] parts =
        [
            response.City ?? response.Locality ?? string.Empty,
            response.PrincipalSubdivision ?? string.Empty,
            response.CountryName ?? string.Empty,
        ];
        string displayName = string.Join(
            ", ",
            parts.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct());
        return string.IsNullOrWhiteSpace(displayName)
            ? "Approximate current location"
            : displayName;
    }

    private sealed class IpLocationResponse
    {
        [JsonPropertyName("latitude")]
        public double? Latitude { get; init; }

        [JsonPropertyName("longitude")]
        public double? Longitude { get; init; }

        [JsonPropertyName("city")]
        public string? City { get; init; }

        [JsonPropertyName("locality")]
        public string? Locality { get; init; }

        [JsonPropertyName("principalSubdivision")]
        public string? PrincipalSubdivision { get; init; }

        [JsonPropertyName("countryName")]
        public string? CountryName { get; init; }

        [JsonPropertyName("countryCode")]
        public string? CountryCode { get; init; }

        [JsonPropertyName("localityInfo")]
        public LocalityInformation? LocalityInfo { get; init; }
    }

    private sealed class LocalityInformation
    {
        [JsonPropertyName("informative")]
        public IReadOnlyList<LocalityInformationItem>? Informative { get; init; }
    }

    private sealed class LocalityInformationItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }
}
