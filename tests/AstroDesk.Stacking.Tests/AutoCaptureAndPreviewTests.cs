using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

public sealed class LivePreviewStackerTests
{
    [Fact]
    public void EstimateOffset_RecoversAKnownShift()
    {
        PreviewImage reference = StarField(120, 100, dx: 0, dy: 0);
        PreviewImage shifted = StarField(120, 100, dx: 5, dy: -3);

        FrameOffset offset = LivePreviewStacker.EstimateOffset(reference, shifted);

        // The frame is the reference moved by (5, -3), so lining it back up
        // means sampling it at that same offset.
        Assert.Equal(5, offset.Dx);
        Assert.Equal(-3, offset.Dy);
    }

    [Fact]
    public void EstimateOffset_IsZeroForAnIdenticalFrame()
    {
        PreviewImage reference = StarField(120, 100, 0, 0);
        FrameOffset offset = LivePreviewStacker.EstimateOffset(reference, StarField(120, 100, 0, 0));

        Assert.Equal(FrameOffset.Zero, offset);
    }

    [Fact]
    public void Add_AveragesFramesAndCutsNoise()
    {
        var stacker = new LivePreviewStacker(64, 64);
        var random = new Random(11);

        // A flat field plus independent noise. Averaging N frames should reduce
        // the spread by about sqrt(N); this is the whole point of stacking.
        const int frames = 16;
        for (int index = 0; index < frames; index++)
        {
            stacker.Add(NoisyFlat(64, 64, level: 100f, noise: 20f, random));
        }

        PreviewImage? result = stacker.GetResult();
        Assert.NotNull(result);
        Assert.Equal(frames, stacker.FramesStacked);

        double spread = StandardDeviation(result!);
        Assert.True(
            spread < 20f / 2,
            $"expected noise well below the single-frame 20, got {spread:F2}");
    }

    [Fact]
    public void GetResult_IsNullBeforeAnyFrame() =>
        Assert.Null(new LivePreviewStacker(32, 32).GetResult());

    [Fact]
    public void Reset_ClearsTheRun()
    {
        var stacker = new LivePreviewStacker(32, 32);
        stacker.Add(NoisyFlat(32, 32, 50f, 1f, new Random(3)));
        Assert.Equal(1, stacker.FramesStacked);

        stacker.Reset();

        Assert.Equal(0, stacker.FramesStacked);
        Assert.Null(stacker.GetResult());
    }

    [Fact]
    public void Add_RejectsAFrameOfTheWrongSize()
    {
        var stacker = new LivePreviewStacker(32, 32);
        Assert.Throws<ArgumentException>(
            () => stacker.Add(NoisyFlat(16, 16, 10f, 1f, new Random(1))));
    }

    private static PreviewImage StarField(int width, int height, int dx, int dy)
    {
        float[] pixels = new float[width * height];
        (int X, int Y)[] stars =
        [
            (30, 25), (60, 40), (85, 70), (45, 80), (70, 20), (20, 60), (95, 50),
        ];

        foreach ((int x, int y) in stars)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                for (int ox = -1; ox <= 1; ox++)
                {
                    int px = x + ox + dx;
                    int py = y + oy + dy;
                    if (px >= 0 && px < width && py >= 0 && py < height)
                    {
                        pixels[(py * width) + px] += ox == 0 && oy == 0 ? 200f : 60f;
                    }
                }
            }
        }

        return new PreviewImage(width, height, pixels);
    }

    private static PreviewImage NoisyFlat(
        int width,
        int height,
        float level,
        float noise,
        Random random)
    {
        float[] pixels = new float[width * height];
        for (int index = 0; index < pixels.Length; index++)
        {
            pixels[index] = level + (float)((random.NextDouble() - 0.5) * 2 * noise);
        }

        return new PreviewImage(width, height, pixels);
    }

    private static double StandardDeviation(PreviewImage image)
    {
        double mean = image.Pixels.Average(value => (double)value);
        double variance = image.Pixels.Average(value => Math.Pow(value - mean, 2));
        return Math.Sqrt(variance);
    }
}
