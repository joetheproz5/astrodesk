namespace AstroDesk.Stacking;

/// <summary>
/// A single-channel image at working resolution, used for the running preview.
/// </summary>
public sealed class PreviewImage(int width, int height, float[] pixels)
{
    public int Width { get; } = width;

    public int Height { get; } = height;

    public float[] Pixels { get; } = pixels;

    public float this[int x, int y] => Pixels[(y * Width) + x];
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

    public LivePreviewStacker(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        _width = width;
        _height = height;
        _accumulator = new double[width * height];
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
        for (int index = 0; index < pixels.Length; index++)
        {
            int contributions = _counts[index];
            pixels[index] = contributions == 0
                ? 0f
                : (float)(_accumulator[index] / contributions);
        }

        return new PreviewImage(_width, _height, pixels);
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

                int index = (y * _width) + x;
                _accumulator[index] += frame[sourceX, sourceY];
                _counts[index]++;
            }
        }
    }
}
