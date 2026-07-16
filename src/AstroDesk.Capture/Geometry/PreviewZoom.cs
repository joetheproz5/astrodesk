namespace AstroDesk.Capture.Geometry;

public sealed record PreviewZoomState(double Scale, PointD CenterNormalized)
{
    public static PreviewZoomState Reset { get; } = new(1, new PointD(0.5, 0.5));
}

public sealed class PreviewZoom
{
    public PreviewZoomState SetScale(
        PreviewZoomState current,
        double scale,
        PointD? centerNormalized = null)
    {
        if (scale is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Supported zoom levels are 1x, 2x, 4x and 8x.");
        }

        PointD center = centerNormalized ?? current.CenterNormalized;
        return new PreviewZoomState(scale, ClampCenter(center, scale));
    }

    public PreviewZoomState Pan(PreviewZoomState current, double normalizedDeltaX, double normalizedDeltaY)
    {
        PointD center = new(
            current.CenterNormalized.X + normalizedDeltaX,
            current.CenterNormalized.Y + normalizedDeltaY);
        return current with { CenterNormalized = ClampCenter(center, current.Scale) };
    }

    public RectD GetSourceRectNormalized(PreviewZoomState state)
    {
        double width = 1 / state.Scale;
        double height = 1 / state.Scale;
        PointD center = ClampCenter(state.CenterNormalized, state.Scale);
        return new RectD(center.X - (width / 2), center.Y - (height / 2), width, height);
    }

    private static PointD ClampCenter(PointD center, double scale)
    {
        double half = 0.5 / scale;
        return new PointD(
            Math.Clamp(center.X, half, 1 - half),
            Math.Clamp(center.Y, half, 1 - half));
    }
}
