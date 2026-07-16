using AstroDesk.Core.Enums;

namespace AstroDesk.Core.Calculations;

public static class MeteorologyCalculations
{
    private const double MagnusA = 17.62;
    private const double MagnusB = 243.12;

    public static double CalculateDewPointCelsius(
        double temperatureCelsius,
        double relativeHumidityPercent)
    {
        if (!double.IsFinite(temperatureCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureCelsius), "Temperature must be finite.");
        }

        if (!double.IsFinite(relativeHumidityPercent) ||
            relativeHumidityPercent is <= 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(relativeHumidityPercent),
                "Relative humidity must be greater than 0 and no more than 100.");
        }

        var gamma = Math.Log(relativeHumidityPercent / 100d) +
                    (MagnusA * temperatureCelsius) / (MagnusB + temperatureCelsius);
        return MagnusB * gamma / (MagnusA - gamma);
    }

    public static DewRisk AssessDewRisk(double temperatureCelsius, double dewPointCelsius)
    {
        if (!double.IsFinite(temperatureCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(temperatureCelsius), "Temperature must be finite.");
        }

        if (!double.IsFinite(dewPointCelsius))
        {
            throw new ArgumentOutOfRangeException(nameof(dewPointCelsius), "Dew point must be finite.");
        }

        var spread = temperatureCelsius - dewPointCelsius;
        return spread switch
        {
            <= 2 => DewRisk.High,
            <= 5 => DewRisk.Moderate,
            _ => DewRisk.Low
        };
    }
}
