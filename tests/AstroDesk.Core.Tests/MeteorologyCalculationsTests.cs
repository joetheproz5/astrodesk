using AstroDesk.Core.Calculations;
using AstroDesk.Core.Enums;

namespace AstroDesk.Core.Tests;

public sealed class MeteorologyCalculationsTests
{
    [Fact]
    public void CalculateDewPointCelsius_UsesMagnusFormula()
    {
        var dewPoint = MeteorologyCalculations.CalculateDewPointCelsius(20, 50);

        Assert.Equal(9.26, dewPoint, precision: 2);
    }

    [Theory]
    [InlineData(10, 9, DewRisk.High)]
    [InlineData(10, 6, DewRisk.Moderate)]
    [InlineData(10, 2, DewRisk.Low)]
    public void AssessDewRisk_UsesTemperatureSpread(
        double temperature,
        double dewPoint,
        DewRisk expected)
    {
        Assert.Equal(expected, MeteorologyCalculations.AssessDewRisk(temperature, dewPoint));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void CalculateDewPointCelsius_RejectsInvalidHumidity(double humidity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => MeteorologyCalculations.CalculateDewPointCelsius(10, humidity));
    }
}
