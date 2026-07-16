using AstroDesk.Capture.Geometry;

namespace AstroDesk.Capture.Tests;

public sealed class PreviewZoomTests
{
    private readonly PreviewZoom _zoom = new();

    [Fact]
    public void FourTimesZoom_UsesQuarterSizedSource()
    {
        PreviewZoomState state = _zoom.SetScale(
            PreviewZoomState.Reset,
            4,
            new PointD(0.5, 0.5));

        RectD source = _zoom.GetSourceRectNormalized(state);

        Assert.Equal(0.375, source.X, 6);
        Assert.Equal(0.375, source.Y, 6);
        Assert.Equal(0.25, source.Width, 6);
        Assert.Equal(0.25, source.Height, 6);
    }

    [Fact]
    public void Pan_ClampsSourceInsideFrame()
    {
        PreviewZoomState state = _zoom.SetScale(PreviewZoomState.Reset, 8);
        PreviewZoomState panned = _zoom.Pan(state, 10, -10);

        RectD source = _zoom.GetSourceRectNormalized(panned);

        Assert.Equal(0.875, source.X, 6);
        Assert.Equal(0, source.Y, 6);
        Assert.Equal(1, source.Right, 6);
        Assert.Equal(0.125, source.Bottom, 6);
    }
}
