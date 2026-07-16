using AstroDesk.Core.Interfaces;
using AstroDesk.Core.Models;

namespace AstroDesk.Infrastructure.Providers;

public sealed class UnavailableWeatherProvider : IWeatherProvider
{
    public string Name => "Unavailable";

    public Task<WeatherConditions?> GetCurrentAsync(
        GeoCoordinate coordinate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<WeatherConditions?>(null);
    }
}

public sealed class UnavailableLocationProvider : ILocationProvider
{
    public string Name => "Unavailable";

    public Task<IReadOnlyList<LocationSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LocationSearchResult>>([]);
    }

    public Task<LocationSearchResult?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<LocationSearchResult?>(null);
    }
}
