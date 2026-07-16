using System.Globalization;
using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;
using AstroDesk.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Windows.Devices.Geolocation;

namespace AstroDesk.App.Services;

public sealed class WindowsLocationProvider(
    OpenMeteoLocationProvider searchProvider,
    BigDataCloudIpLocationProvider ipLocationProvider,
    ILogger<WindowsLocationProvider> logger) : ILocationProvider
{
    private static readonly TimeSpan MaximumLocationAge = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LocationTimeout = TimeSpan.FromSeconds(15);
    private readonly OpenMeteoLocationProvider _searchProvider = searchProvider;
    private readonly BigDataCloudIpLocationProvider _ipLocationProvider = ipLocationProvider;
    private readonly ILogger<WindowsLocationProvider> _logger = logger;

    public string Name => "Windows Location";

    public Task<IReadOnlyList<LocationSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default) =>
        _searchProvider.SearchAsync(query, cancellationToken);

    public async Task<LocationSearchResult?> GetCurrentAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            GeolocationAccessStatus access = await Geolocator.RequestAccessAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (access != GeolocationAccessStatus.Allowed)
            {
                _logger.LogInformation(
                    "Windows location access was not granted. Access status: {AccessStatus}.",
                    access);
                return await GetNetworkFallbackAsync(cancellationToken).ConfigureAwait(true);
            }

            Geolocator geolocator = new()
            {
                DesiredAccuracy = PositionAccuracy.Default,
                DesiredAccuracyInMeters = 500,
            };
            Geoposition position = await geolocator.GetGeopositionAsync(
                MaximumLocationAge,
                LocationTimeout);
            cancellationToken.ThrowIfCancellationRequested();

            BasicGeoposition point = position.Coordinate.Point.Position;
            GeoCoordinate coordinate = new(point.Latitude, point.Longitude);
            string? countryCode = TryGetCountryCode();
            return new LocationSearchResult(
                "Current device location",
                coordinate,
                countryCode,
                TimeZoneInfo.Local.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Windows could not determine the current location.");
            return await GetNetworkFallbackAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<LocationSearchResult?> GetNetworkFallbackAsync(
        CancellationToken cancellationToken)
    {
        LocationSearchResult? result = await _ipLocationProvider
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(true);
        if (result is not null)
        {
            _logger.LogInformation(
                "Using an approximate public-IP location because Windows location was unavailable.");
        }

        return result;
    }

    private static string? TryGetCountryCode()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
