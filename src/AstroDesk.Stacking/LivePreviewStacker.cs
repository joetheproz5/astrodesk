namespace AstroDesk.Stacking;

/// <summary>
/// An image at working resolution, used for the running preview.
/// </summary>
/// <remarks>
/// Carries either one channel or three interleaved as RGB. A separate luminance
/// plane is built once up front because alignment reads it many thousands of
/// times per frame: collapsing three channels inside that loop would make the
/// search several times more expensive for a number that never changes.
/// </remarks>
public sealed class PreviewImage
{
    public const int Greyscale = 1;
    public const int Rgb = 3;

    public PreviewImage(int width, int height, float[] pixels, int channels = Greyscale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(pixels);

        if (channels is not (Greyscale or Rgb))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels),
                channels,
                "A preview frame must be greyscale or RGB.");
        }

        if (pixels.Length != width * height * channels)
        {
            throw new ArgumentException(
                $"Expected {width * height * channels} samples for a {width}x{height} " +
                $"image with {channels} channel(s), got {pixels.Length}.",
                nameof(pixels));
        }

        Width = width;
        Height = height;
        Channels = channels;
        Pixels = pixels;
        Luminance = channels == Greyscale ? pixels : BuildLuminance(pixels, width * height);
    }

    public int Width { get; }

    public int Height { get; }

    public int Channels { get; }

    /// <summary>Samples, interleaved when there is more than one channel.</summary>
    public float[] Pixels { get; }

    /// <summary>
    /// One sample per pixel. Aliases <see cref="Pixels"/> when greyscale.
    /// </summary>
    public float[] Luminance { get; }

    public bool IsColour => Channels == Rgb;

    /// <summary>Luminance at a pixel. This is what alignment works on.</summary>
    public float this[int x, int y] => Luminance[(y * Width) + x];

    public float this[int x, int y, int channel] =>
        Pixels[(((y * Width) + x) * Channels) + channel];

    private static float[] BuildLuminance(float[] pixels, int pixelCount)
    {
        float[] luminance = new float[pixelCount];
        for (int index = 0; index < pixelCount; index++)
        {
            int source = index * Rgb;
            luminance[index] =
                (pixels[source] * 0.299f) +
                (pixels[source + 1] * 0.587f) +
                (pixels[source + 2] * 0.114f);
        }

        return luminance;
    }
}

/// <summary>
/// Integer pixel offset of a frame relative to the reference.
/// </summary>
public readonly record struct FrameOffset(int Dx, int Dy)
{
    public static FrameOffset Zero => new(0, 0);
}

/// <summary>
/// Builds a rough running average of the frames captured so far.
/// </summary>
/// <remarks>
/// This exists to answer "is tonight's run working?" while it is still running,
/// not to produce the final image. It estimates a whole-pixel translation only:
/// over a few minutes sky drift is dominated by translation, so this is enough
/// to show stars staying round and noise dropping. It deliberately does not
/// model the field rotation that becomes significant over a longer session —
/// correcting that is Siril's job in the final stack, and attempting it here
/// would cost far more time than a preview justifies.
/// </remarks>
public sealed class LivePreviewStacker
{
    /// <summary>
    /// How far the aligner will search, in pixels, at preview resolution.
    /// </summary>
    public const int SearchRadius = 24;

    private readonly int _width;
    private readonly int _height;
    private readonly int _channels;
    private readonly double[] _accumulator;

    /// <summary>
    /// How many frames actually contributed to each pixel.
    /// </summary>
    /// <remarks>
    /// Once frames are shifted to align them, the edges of the stack are not
    /// covered by every frame. Dividing the whole accumulator by the total frame
    /// count therefore darkens those margins into bands. The per-pixel count is
    /// what makes the running mean correct right to the edge.
    /// </remarks>
    private readonly int[] _counts;

    private PreviewImage? _reference;

    public LivePreviewStacker(int width, int height, int channels = PreviewImage.Greyscale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (channels is not (PreviewImage.Greyscale or PreviewImage.Rgb))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channels),
                channels,
                "A preview stack must be greyscale or RGB.");
        }

        _width = width;
        _height = height;
        _channels = channels;
        _accumulator = new double[width * height * channels];

        // One count per pixel, not per sample: every channel of a pixel is
        // covered by exactly the same set of frames.
        _counts = new int[width * height];
    }

    public int FramesStacked { get; private set; }

    /// <summary>
    /// Adds a frame and returns the offset it was aligned by.
    /// </summary>
    public FrameOffset Add(PreviewImage frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width != _width || frame.Height != _height)
        {
            throw new ArgumentException(
                $"Frame must be {_width}x{_height} to match the preview accumulator.",
                nameof(frame));
        }

        if (frame.Channels != _channels)
        {
            throw new ArgumentException(
                $"Frame has {frame.Channels} channel(s) but the stack has {_channels}.",
                nameof(frame));
        }

        FrameOffset offset;
        if (_reference is null)
        {
            _reference = frame;
            offset = FrameOffset.Zero;
        }
        else
        {
            offset = EstimateOffset(_reference, frame);
        }

        Accumulate(frame, offset);
        FramesStacked++;
        return offset;
    }

    /// <summary>
    /// Current running mean. Returns null before any frame has been added.
    /// </summary>
    public PreviewImage? GetResult()
    {
        if (FramesStacked == 0)
        {
            return null;
        }

        float[] pixels = new float[_accumulator.Length];
        for (int pixel = 0; pixel < _counts.Length; pixel++)
        {
            int contributions = _counts[pixel];
            for (int channel = 0; channel < _channels; channel++)
            {
                int index = (pixel * _channels) + channel;
                pixels[index] = contributions == 0
                    ? 0f
                    : (float)(_accumulator[index] / contributions);
            }
        }

        return new PreviewImage(_width, _height, pixels, _channels);
    }

    public void Reset()
    {
        Array.Clear(_accumulator);
        Array.Clear(_counts);
        _reference = null;
        FramesStacked = 0;
    }

    /// <summary>
    /// Finds the whole-pixel shift that best lines <paramref name="frame"/> up
    /// with <paramref name="reference"/>.
    /// </summary>
    /// <remarks>
    /// Uses sum of absolute differences over a centre window. A star field is
    /// mostly empty sky, so the few bright pixels dominate the score and the
    /// minimum is sharp, which makes a plain search reliable here without the
    /// machinery of a frequency-domain correlation.
    /// </remarks>
    public static FrameOffset EstimateOffset(PreviewImage reference, PreviewImage frame)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(frame);

        int marginX = Math.Min(SearchRadius, reference.Width / 4);
        int marginY = Math.Min(SearchRadius, reference.Height / 4);

        double best = double.MaxValue;
        FrameOffset bestOffset = FrameOffset.Zero;

        for (int dy = -marginY; dy <= marginY; dy++)
        {
            for (int dx = -marginX; dx <= marginX; dx++)
            {
                double score = Score(reference, frame, dx, dy, marginX, marginY);
                if (score < best)
                {
                    best = score;
                    bestOffset = new FrameOffset(dx, dy);
                }
            }
        }

        return bestOffset;
    }

    private static double Score(
        PreviewImage reference,
        PreviewImage frame,
        int dx,
        int dy,
        int marginX,
        int marginY)
    {
        double total = 0;
        // Step over the window rather than every pixel: the preview only needs a
        // good offset quickly, and sampling keeps this cheap enough to run on
        // every incoming frame.
        const int step = 2;

        for (int y = marginY; y < reference.Height - marginY; y += step)
        {
            for (int x = marginX; x < reference.Width - marginX; x += step)
            {
                total += Math.Abs(reference[x, y] - frame[x + dx, y + dy]);
            }
        }

        return total;
    }

    private void Accumulate(PreviewImage frame, FrameOffset offset)
    {
        for (int y = 0; y < _height; y++)
        {
            int sourceY = y + offset.Dy;
            if (sourceY < 0 || sourceY >= _height)
            {
                continue;
            }

            for (int x = 0; x < _width; x++)
            {
                int sourceX = x + offset.Dx;
                if (sourceX < 0 || sourceX >= _width)
                {
                    continue;
                }

                int pixel = (y * _width) + x;
                for (int channel = 0; channel < _channels; channel++)
                {
                    _accumulator[(pixel * _channels) + channel] +=
                        frame[sourceX, sourceY, channel];
                }

                _counts[pixel]++;
            }
        }
    }
}
