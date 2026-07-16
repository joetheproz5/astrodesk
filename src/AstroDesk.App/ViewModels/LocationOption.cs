using AstroDesk.Core.Models;

namespace AstroDesk.App.ViewModels;

public sealed record LocationOption(
    string Name,
    double Latitude,
    double Longitude,
    string? TimeZoneId = null,
    double? ElevationMeters = null)
{
    public GeoCoordinate Coordinate => new(Latitude, Longitude);

    public string CoordinateText => $"{Latitude:0.####}, {Longitude:0.####}";

    public override string ToString() => Name;

    public static LocationOption FromSearchResult(LocationSearchResult result) => new(
        result.DisplayName,
        result.Coordinate.Latitude,
        result.Coordinate.Longitude,
        result.TimeZoneId,
        result.ElevationMeters);
}
