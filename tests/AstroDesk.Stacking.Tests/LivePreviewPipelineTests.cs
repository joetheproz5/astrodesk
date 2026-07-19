using System.Windows.Media;
using System.Windows.Media.Imaging;
using AstroDesk.Stacking;
using Xunit;

namespace AstroDesk.Stacking.Tests;

/// <summary>
/// Exercises the live preview against real encoded images rather than synthetic
/// float arrays, covering the decode path the app actually uses.
/// </summary>
/// <remarks>
/// The frames model a fixed-tripod sequence: the same star field shifted a few
/// pixels per frame with fresh noise each time, which is what the aligner has to
/// undo before averaging can reduce anything.
/// </remarks>
public sealed class LivePreviewPipelineTests : IDisposable
{
    private const int Width = 320;
    private const int Height = 240;
    private const int Frames = 12;
    private const int DriftPerFrame = 3;

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(),
        "astrodesk-live-preview",
        Guid.NewGuid().ToString("N"));

    public LivePreviewPipelineTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder must not fail the suite.
        }
    }

    [Fact]
    public void DecodingAndStacking_ReducesNoiseAcrossDriftingFrames()
    {
        string[] paths = WriteSequence();
        PreviewImage first = Decode(paths[0]);

        // Shaped from the frame rather than assumed. Assuming greyscale here is
        // what let this test keep passing after the pipeline moved to colour.
        var stacker = new LivePreviewStacker(first.Width, first.Height, first.Channels);

        foreach (string path in paths)
        {
            stacker.Add(Decode(path));
        }

        Assert.Equal(Frames, stacker.FramesStacked);

        PreviewImage? result = stacker.GetResult();
        Assert.NotNull(result);

        // Measure a star-free corner: averaging aligned frames must visibly
        // reduce the background spread, which is the point of the preview.
        double single = CornerSigma(first);
        double stacked = CornerSigma(result!);

        Assert.True(
            stacked < single / 1.8,
            $"expected the stack to cut background noise; single {single:F2}, stacked {stacked:F2}");
    }

    [Fact]
    public void Aligner_TracksTheDriftBetweenDecodedFrames()
    {
        string[] paths = WriteSequence();
        PreviewImage reference = Decode(paths[0]);
        PreviewImage third = Decode(paths[3]);

        FrameOffset offset = LivePreviewStacker.EstimateOffset(reference, third);

        // Frame 3 is drifted 3*3 = 9px right; the aligner samples it back.
        Assert.InRange(offset.Dx, DriftPerFrame * 3 - 2, DriftPerFrame * 3 + 2);
    }

    [Fact]
    public void DecodingHonoursTheRequestedWorkingWidth()
    {
        // The renderer decodes at a reduced width so a 50 megapixel capture never
        // becomes a full-size bitmap; verify the decode actually downscales.
        string[] paths = WriteSequence();
        PreviewImage small = Decode(paths[0], targetWidth: 160);

        Assert.Equal(160, small.Width);
        Assert.True(small.Height < Height);
    }

    /// <summary>
    /// Mirrors the decode in LivePreviewRenderer: small, greyscale, float.
    /// </summary>
    /// <summary>
    /// The decoder the app actually uses.
    /// </summary>
    /// <remarks>
    /// This test used to carry its own copy of the decode, which meant a
    /// regression in the real one passed green - including through the move from
    /// greyscale to colour, when the copy stayed stubbornly grey and agreed with
    /// nothing that shipped.
    /// </remarks>
    private static PreviewImage Decode(string path, int targetWidth = Width) =>
        PreviewDecoder.Decode(path, targetWidth)
        ?? throw new InvalidOperationException($"The decoder could not read '{path}'.");

    private string[] WriteSequence()
    {
        var random = new Random(23);
        (double X, double Y, double Amp)[] stars =
        [
            .. Enumerable.Range(0, 60).Select(_ => (
                X: random.NextDouble() * (Width - 80) + 40,
                Y: random.NextDouble() * (Height - 80) + 40,
                Amp: random.NextDouble() * 90 + 150)),
        ];

        var paths = new string[Frames];
        for (int frame = 0; frame < Frames; frame++)
        {
            byte[] pixels = new byte[Width * Height];
            for (int index = 0; index < pixels.Length; index++)
            {
                pixels[index] = (byte)Math.Clamp(14 + ((random.NextDouble() - 0.5) * 34), 0, 255);
            }

            foreach ((double sx, double sy, double amp) in stars)
            {
                double cx = sx + (DriftPerFrame * frame);
                double cy = sy + (DriftPerFrame * frame * 0.3);
                for (int oy = -3; oy <= 3; oy++)
                {
                    for (int ox = -3; ox <= 3; ox++)
                    {
                        int x = (int)cx + ox;
                        int y = (int)cy + oy;
                        if (x < 0 || x >= Width || y < 0 || y >= Height)
                        {
                            continue;
                        }

                        double falloff = Math.Exp(-((ox * ox) + (oy * oy)) / 2.6);
                        int value = pixels[(y * Width) + x] + (int)(amp * falloff);
                        pixels[(y * Width) + x] = (byte)Math.Min(255, value);
                    }
                }
            }

            paths[frame] = Path.Combine(_folder, $"frame_{frame:D3}.png");
            WriteGreyscalePng(paths[frame], pixels);
        }

        return paths;
    }

    private static void WriteGreyscalePng(string path, byte[] pixels)
    {
        BitmapSource source = BitmapSource.Create(
            Width, Height, 96, 96, PixelFormats.Gray8, null, pixels, Width);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static double CornerSigma(PreviewImage image)
    {
        var samples = new List<double>();
        int limitX = Math.Min(60, image.Width);
        int limitY = Math.Min(45, image.Height);

        for (int y = 2; y < limitY; y++)
        {
            for (int x = 2; x < limitX; x++)
            {
                samples.Add(image[x, y]);
            }
        }

        double mean = samples.Average();
        return Math.Sqrt(samples.Average(value => Math.Pow(value - mean, 2)));
    }
}
