using AstroDesk.Core.Calculations;

namespace AstroDesk.Core.Tests;

public sealed class ExposureCalculationsTests
{
    [Fact]
    public void CalculateIntegrationTime_MultipliesExposureByFrames()
    {
        var result = ExposureCalculations.CalculateIntegrationTime(TimeSpan.FromSeconds(20), 42);

        Assert.Equal(TimeSpan.FromMinutes(14), result);
    }

    [Fact]
    public void EstimateRemainingCaptureTime_UsesOnlyRemainingFrames()
    {
        var result = ExposureCalculations.EstimateRemainingCaptureTime(
            TimeSpan.FromSeconds(20),
            TimeSpan.Zero,
            plannedFrameCount: 120,
            completedFrameCount: 42);

        Assert.Equal(TimeSpan.FromMinutes(26), result);
    }

    [Fact]
    public void EstimateRemainingCaptureTime_CountsDelayOnlyBetweenFrames()
    {
        var result = ExposureCalculations.EstimateRemainingCaptureTime(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(2),
            plannedFrameCount: 3,
            completedFrameCount: 0);

        Assert.Equal(TimeSpan.FromSeconds(34), result);
    }

    [Theory]
    [InlineData(120, 42, 35)]
    [InlineData(120, 130, 100)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 100)]
    public void CalculateProgressPercentage_ReturnsBoundedPercentage(
        int planned,
        int completed,
        double expected)
    {
        var result = ExposureCalculations.CalculateProgressPercentage(planned, completed);

        Assert.Equal(expected, result, precision: 6);
    }

    [Fact]
    public void CalculateIntegrationTime_RejectsNonPositiveExposure()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ExposureCalculations.CalculateIntegrationTime(TimeSpan.Zero, 1));
    }
}
