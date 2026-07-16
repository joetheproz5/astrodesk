using AstroDesk.Core.Models;
using AstroDesk.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AstroDesk.Core.Tests;

public sealed class AstronomyEngineProviderTests
{
    [Fact]
    public async Task GetConditionsAsync_ReturnsCurrentDirectionAndHourlyMoonTrack()
    {
        AstronomyEngineProvider provider = new(
            NullLogger<AstronomyEngineProvider>.Instance);

        AstronomyConditions? result = await provider.GetConditionsAsync(
            new GeoCoordinate(33.8938, 35.5018),
            DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.NotNull(result);
        Assert.InRange(result.MoonAltitudeDegrees!.Value, -90, 90);
        Assert.InRange(result.MoonAzimuthDegrees!.Value, 0, 360);
        Assert.False(string.IsNullOrWhiteSpace(result.MoonDirection));
        Assert.NotNull(result.MoonPositions);
        Assert.Equal(37, result.MoonPositions.Count);
        Assert.All(
            result.MoonPositions,
            position =>
            {
                Assert.InRange(position.AltitudeDegrees, -90, 90);
                Assert.InRange(position.AzimuthDegrees, 0, 360);
            });
    }
}
