using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Covers the live preview stack carrying colour rather than collapsing every
/// frame to grey.
/// </summary>
/// <remarks>
/// The preview was greyscale because it was cheap, but a colour cast is itself
/// diagnostic: it is how skyglow and a bad white balance announce themselves
/// while the run is still going, which is precisely when they can be fixed.
/// </remarks>
public sealed class ColourLivePreviewTests
{
    [Fact]
    public void AColourFrameKeepsItsChannelsApart()
    {
        // The failure this guards: averaging the channels into one, which would
        // stack "correctly" and produce grey.
        PreviewImage frame = Solid(4, 4, red: 200, green: 100, blue: 50);
        LivePreviewStacker stacker = new(4, 4, PreviewImage.Rgb);

        stacker.Add(frame);
        PreviewImage result = stacker.GetResult()!;

        Assert.True(result.IsColour);
        Assert.Equal(200, result[2, 2, 0], 2);
        Assert.Equal(100, result[2, 2, 1], 2);
        Assert.Equal(50, result[2, 2, 2], 2);
    }

    [Fact]
    public void StackingAveragesEachChannelIndependently()
    {
        LivePreviewStacker stacker = new(4, 4, PreviewImage.Rgb);

        stacker.Add(Solid(4, 4, 200, 100, 0));
        stacker.Add(Solid(4, 4, 100, 0, 100));

        PreviewImage result = stacker.GetResult()!;
        Assert.Equal(150, result[1, 1, 0], 2);
        Assert.Equal(50, result[1, 1, 1], 2);
        Assert.Equal(50, result[1, 1, 2], 2);
    }

    [Fact]
    public void LuminanceUsesTheStandardWeights()
    {
        PreviewImage frame = Solid(2, 2, red: 255, green: 0, blue: 0);

        // 255 * 0.299
        Assert.Equal(76.2, frame[0, 0], 1);
    }

    [Fact]
    public void AGreyscaleFrameAliasesItsOwnSamplesAsLuminance()
    {
        // Avoids allocating and filling a second identical plane on every frame.
        float[] pixels = [1, 2, 3, 4];
        PreviewImage frame = new(2, 2, pixels);

        Assert.False(frame.IsColour);
        Assert.Same(pixels, frame.Luminance);
    }

    [Fact]
    public void AlignmentOnColourFramesUsesLuminance()
    {
        // A star shifted by a known amount must be found regardless of its hue,
        // because the search runs on the luminance plane.
        PreviewImage reference = StarAt(64, 64, 30, 30, red: 250, green: 40, blue: 40);
        PreviewImage shifted = StarAt(64, 64, 34, 27, red: 250, green: 40, blue: 40);

        FrameOffset offset = LivePreviewStacker.EstimateOffset(reference, shifted);

        Assert.Equal(4, offset.Dx);
        Assert.Equal(-3, offset.Dy);
    }

    [Fact]
    public void AColourFrameIsRejectedByAGreyscaleStack()
    {
        // Silently accepting it would read the interleaved samples as pixels and
        // produce a garbled third-width image.
        LivePreviewStacker stacker = new(4, 4);

        Assert.Throws<ArgumentException>(() => stacker.Add(Solid(4, 4, 10, 20, 30)));
    }

    [Fact]
    public void AGreyscaleFrameIsRejectedByAColourStack()
    {
        LivePreviewStacker stacker = new(4, 4, PreviewImage.Rgb);

        Assert.Throws<ArgumentException>(
            () => stacker.Add(new PreviewImage(4, 4, new float[16])));
    }

    [Fact]
    public void ASampleCountThatDoesNotMatchTheShapeIsRejected() =>
        Assert.Throws<ArgumentException>(
            () => new PreviewImage(4, 4, new float[16], PreviewImage.Rgb));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void OnlyGreyscaleAndRgbAreSupported(int channels)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PreviewImage(2, 2, new float[2 * 2 * Math.Max(channels, 1)], channels));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LivePreviewStacker(2, 2, channels));
    }

    [Fact]
    public void ColourStackingStillCoversTheEdgesAfterAShift()
    {
        // The per-pixel count has to stay per-pixel rather than per-sample, or the
        // aligned margins darken into bands in colour just as they did in grey.
        LivePreviewStacker stacker = new(32, 32, PreviewImage.Rgb);

        stacker.Add(Solid(32, 32, 120, 120, 120));
        stacker.Add(StarAt(32, 32, 20, 20, 120, 120, 120, background: 120));

        PreviewImage result = stacker.GetResult()!;
        Assert.Equal(120, result[0, 0, 0], 1);
        Assert.Equal(120, result[31, 31, 2], 1);
    }

    private static PreviewImage Solid(int width, int height, float red, float green, float blue)
    {
        float[] pixels = new float[width * height * PreviewImage.Rgb];
        for (int index = 0; index < width * height; index++)
        {
            pixels[(index * 3) + 0] = red;
            pixels[(index * 3) + 1] = green;
            pixels[(index * 3) + 2] = blue;
        }

        return new PreviewImage(width, height, pixels, PreviewImage.Rgb);
    }

    private static PreviewImage StarAt(
        int width,
        int height,
        int x,
        int y,
        float red,
        float green,
        float blue,
        float background = 0)
    {
        float[] pixels = new float[width * height * PreviewImage.Rgb];
        if (background != 0)
        {
            Array.Fill(pixels, background);
        }

        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int px = x + dx;
                int py = y + dy;
                if (px < 0 || px >= width || py < 0 || py >= height)
                {
                    continue;
                }

                int offset = ((py * width) + px) * 3;
                pixels[offset] = red;
                pixels[offset + 1] = green;
                pixels[offset + 2] = blue;
            }
        }

        return new PreviewImage(width, height, pixels, PreviewImage.Rgb);
    }
}
