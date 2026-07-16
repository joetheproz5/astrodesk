namespace AstroDesk.Core.Calculations;

public static class ExposureCalculations
{
    public static TimeSpan CalculateIntegrationTime(TimeSpan exposureTime, int frameCount)
    {
        ValidateExposure(exposureTime);
        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), "Frame count cannot be negative.");
        }

        return TimeSpan.FromTicks(checked(exposureTime.Ticks * frameCount));
    }

    public static int CalculateRemainingFrames(int plannedFrameCount, int completedFrameCount)
    {
        ValidateFrameCounts(plannedFrameCount, completedFrameCount);
        return Math.Max(0, plannedFrameCount - completedFrameCount);
    }

    public static double CalculateProgressPercentage(int plannedFrameCount, int completedFrameCount)
    {
        ValidateFrameCounts(plannedFrameCount, completedFrameCount);
        if (plannedFrameCount == 0)
        {
            return completedFrameCount == 0 ? 0 : 100;
        }

        return Math.Min(100, completedFrameCount * 100d / plannedFrameCount);
    }

    public static TimeSpan EstimateRemainingCaptureTime(
        TimeSpan exposureTime,
        TimeSpan delayBetweenFrames,
        int plannedFrameCount,
        int completedFrameCount)
    {
        ValidateExposure(exposureTime);
        if (delayBetweenFrames < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delayBetweenFrames),
                "Delay between frames cannot be negative.");
        }

        var remainingFrames = CalculateRemainingFrames(plannedFrameCount, completedFrameCount);
        if (remainingFrames == 0)
        {
            return TimeSpan.Zero;
        }

        var exposureTicks = checked(exposureTime.Ticks * remainingFrames);
        var interFrameDelayCount = Math.Max(0, remainingFrames - 1);
        var delayTicks = checked(delayBetweenFrames.Ticks * interFrameDelayCount);
        return TimeSpan.FromTicks(checked(exposureTicks + delayTicks));
    }

    public static TimeSpan EstimateSequenceDuration(
        TimeSpan exposureTime,
        TimeSpan delayBetweenFrames,
        int frameCount,
        TimeSpan initialDelay)
    {
        if (initialDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay), "Initial delay cannot be negative.");
        }

        var captureTime = EstimateRemainingCaptureTime(exposureTime, delayBetweenFrames, frameCount, 0);
        return initialDelay + captureTime;
    }

    private static void ValidateExposure(TimeSpan exposureTime)
    {
        if (exposureTime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(exposureTime), "Exposure time must be greater than zero.");
        }
    }

    private static void ValidateFrameCounts(int plannedFrameCount, int completedFrameCount)
    {
        if (plannedFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedFrameCount), "Planned frame count cannot be negative.");
        }

        if (completedFrameCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(completedFrameCount),
                "Completed frame count cannot be negative.");
        }
    }
}
