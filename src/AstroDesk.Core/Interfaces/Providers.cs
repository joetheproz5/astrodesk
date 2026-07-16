using AstroDesk.Core.Models;

namespace AstroDesk.Core.Interfaces;

public interface IWeatherProvider
{
    string Name { get; }

    Task<WeatherConditions?> GetCurrentAsync(
        GeoCoordinate coordinate,
        CancellationToken cancellationToken = default);
}

public interface IAstronomyProvider
{
    string Name { get; }

    Task<AstronomyConditions?> GetConditionsAsync(
        GeoCoordinate coordinate,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

public interface ILocationProvider
{
    string Name { get; }

    Task<IReadOnlyList<LocationSearchResult>> SearchAsync(
        string query,
        CancellationToken cancellationToken = default);

    Task<LocationSearchResult?> GetCurrentAsync(
        CancellationToken cancellationToken = default);
}
