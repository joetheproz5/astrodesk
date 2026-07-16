using AstroDesk.Core.Models;

namespace AstroDesk.Infrastructure.Providers;

public static class LocationSeeds
{
    public static IReadOnlyList<LocationSearchResult> LebanonExamples { get; } =
    [
        new("Beirut, Lebanon", new GeoCoordinate(33.8938, 35.5018), "LB", "Asia/Beirut", 10),
        new("Zahle, Lebanon", new GeoCoordinate(33.8463, 35.9020), "LB", "Asia/Beirut", 950),
        new("Faraya, Lebanon", new GeoCoordinate(34.0106, 35.8258), "LB", "Asia/Beirut", 1300),
        new("Tannourine, Lebanon", new GeoCoordinate(34.2093, 35.9295), "LB", "Asia/Beirut", 1450),
        new("Jezzine, Lebanon", new GeoCoordinate(33.5417, 35.5844), "LB", "Asia/Beirut", 950),
        new("Bekaa Valley, Lebanon", new GeoCoordinate(33.8500, 35.9000), "LB", "Asia/Beirut", 900),
    ];
}
