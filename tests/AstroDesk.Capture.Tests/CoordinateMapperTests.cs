using AstroDesk.Capture.Geometry;

namespace AstroDesk.Capture.Tests;

public sealed class CoordinateMapperTests
{
    private readonly CoordinateMapper _mapper = new();

    [Fact]
    public void Fit_PreservesAspectRatioAndCentersLetterbox()
    {
        CoordinateMappingContext context = new(
            new SizeD(1000, 1000),
            new SizeD(1080, 2400),
            new SizeD(1080, 2400));

        RectD rect = _mapper.CalculateRenderedRect(context);

        Assert.Equal(275, rect.X, 6);
        Assert.Equal(0, rect.Y, 6);
        Assert.Equal(450, rect.Width, 6);
        Assert.Equal(1000, rect.Height, 6);
    }

    [Fact]
    public void MapToScrcpy_RejectsPointInLetterbox()
    {
        CoordinateMappingContext context = new(
            new SizeD(1000, 1000),
            new SizeD(1080, 2400),
            new SizeD(1080, 2400));

        CoordinateMappingResult result = _mapper.MapToScrcpy(new PointD(20, 500), context);

        Assert.False(result.IsInsidePreview);
    }

    [Fact]
    public void MapToScrcpy_MapsCenterAcrossDifferentClientSize()
    {
        CoordinateMappingContext context = new(
            new SizeD(1600, 900),
            new SizeD(1440, 900),
            new SizeD(1080, 2400));

        CoordinateMappingResult result = _mapper.MapToScrcpy(new PointD(800, 450), context);

        Assert.True(result.IsInsidePreview);
        Assert.Equal(539.5, result.ScrcpyClientPointPixels.X, 6);
        Assert.Equal(1199.5, result.ScrcpyClientPointPixels.Y, 6);
    }

    [Fact]
    public void DpiScaling_DoesNotChangeNormalizedMapping()
    {
        CoordinateMappingContext standard = new(
            new SizeD(1000, 800),
            new SizeD(1000, 500),
            new SizeD(1000, 500));
        CoordinateMappingContext scaled = standard with { DpiScaleX = 1.5, DpiScaleY = 1.5 };

        CoordinateMappingResult first = _mapper.MapToScrcpy(new PointD(500, 400), standard);
        CoordinateMappingResult second = _mapper.MapToScrcpy(new PointD(500, 400), scaled);

        Assert.Equal(first.NormalizedPoint.X, second.NormalizedPoint.X, 8);
        Assert.Equal(first.NormalizedPoint.Y, second.NormalizedPoint.Y, 8);
    }

    [Theory]
    [InlineData(FrameRotation.Rotate0, 0, 0)]
    [InlineData(FrameRotation.Rotate90, 0, 1)]
    [InlineData(FrameRotation.Rotate180, 1, 1)]
    [InlineData(FrameRotation.Rotate270, 1, 0)]
    public void Rotation_MapsDisplayedTopLeftToExpectedClientCorner(
        FrameRotation rotation,
        double expectedU,
        double expectedV)
    {
        CoordinateMappingContext context = new(
            new SizeD(100, 100),
            new SizeD(100, 100),
            new SizeD(101, 101),
            Rotation: rotation);

        CoordinateMappingResult result = _mapper.MapToScrcpy(new PointD(0, 0), context);

        Assert.True(result.IsInsidePreview);
        Assert.Equal(expectedU * 100, result.ScrcpyClientPointPixels.X, 6);
        Assert.Equal(expectedV * 100, result.ScrcpyClientPointPixels.Y, 6);
    }

    [Theory]
    [InlineData(FrameRotation.Rotate0)]
    [InlineData(FrameRotation.Rotate90)]
    [InlineData(FrameRotation.Rotate180)]
    [InlineData(FrameRotation.Rotate270)]
    public void ReverseMapping_RoundTrips(FrameRotation rotation)
    {
        CoordinateMappingContext context = new(
            new SizeD(1200, 900),
            new SizeD(1080, 2400),
            new SizeD(1080, 2400),
            1.25,
            1.25,
            rotation);
        PointD client = new(713, 1402);

        PointD embedded = _mapper.MapFromScrcpy(client, context);
        CoordinateMappingResult result = _mapper.MapToScrcpy(embedded, context);

        Assert.True(result.IsInsidePreview);
        Assert.Equal(client.X, result.ScrcpyClientPointPixels.X, 5);
        Assert.Equal(client.Y, result.ScrcpyClientPointPixels.Y, 5);
    }

    [Fact]
    public void PixelPerfect_DoesNotUpscale()
    {
        CoordinateMappingContext context = new(
            new SizeD(2560, 1440),
            new SizeD(1920, 1080),
            new SizeD(1920, 1080),
            SizingMode: PreviewSizingMode.PixelPerfect);

        RectD rect = _mapper.CalculateRenderedRect(context);

        Assert.Equal(320, rect.X, 6);
        Assert.Equal(180, rect.Y, 6);
        Assert.Equal(1920, rect.Width, 6);
        Assert.Equal(1080, rect.Height, 6);
    }

    [Fact]
    public void ZoomedSourceView_MapsCenterToZoomCenter()
    {
        CoordinateMappingContext context = new(
            new SizeD(1000, 500),
            new SizeD(2000, 1000),
            new SizeD(2000, 1000),
            SourceViewNormalized: new RectD(0.375, 0.375, 0.25, 0.25));

        CoordinateMappingResult result = _mapper.MapToScrcpy(new PointD(500, 250), context);

        Assert.True(result.IsInsidePreview);
        Assert.Equal(999.5, result.ScrcpyClientPointPixels.X, 6);
        Assert.Equal(499.5, result.ScrcpyClientPointPixels.Y, 6);
    }
}
