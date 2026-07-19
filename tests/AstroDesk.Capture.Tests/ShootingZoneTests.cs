using AstroDesk.Capture.Geometry;
using Xunit;

namespace AstroDesk.Capture.Tests;

/// <summary>
/// Covers drawing the framing guides inside the camera's viewfinder rather than
/// across the whole mirrored phone screen.
/// </summary>
public sealed class ShootingZoneTests
{
    [Fact]
    public void TheFullScreenZoneLeavesTheRenderedRectangleAlone()
    {
        RectD rendered = new(10, 20, 800, 400);

        RectD area = ShootingZone.FullScreen.Within(rendered);

        Assert.Equal(rendered, area);
        Assert.True(ShootingZone.FullScreen.IsFullScreen);
    }

    [Fact]
    public void TheExpertRawZoneIsFourThirdsOfTheFullHeight()
    {
        // Measured from the app's own thirds grid on a 1440x3088 S23 Ultra. The
        // landscape preview is 3088x1440, so a full-height 4:3 viewfinder is
        // 1920 px wide; the zone must agree to within a pixel or two.
        RectD landscape = new(0, 0, 3088, 1440);

        RectD area = ShootingZone.ExpertRaw43.Within(landscape);

        Assert.Equal(1440, area.Height, 1);
        Assert.InRange(area.Width, 1905, 1935);
        Assert.Equal(4.0 / 3.0, area.Width / area.Height, 2);
    }

    [Fact]
    public void TheExpertRawZoneLandsOnTheAppsOwnGridLines()
    {
        // The strongest available check: the camera draws its own thirds, so if
        // our zone is right, our thirds fall on top of them. Ground truth is
        // x=877.5 and x=1515.5 across the landscape frame.
        RectD area = ShootingZone.ExpertRaw43.Within(new RectD(0, 0, 3088, 1440));

        double firstThird = area.X + (area.Width / 3);
        double secondThird = area.X + (area.Width * 2 / 3);

        // Within a few pixels: the grid lines were located to about +/-2 px, so
        // demanding more than that would be pinning down measurement noise.
        Assert.InRange(firstThird, 877.5 - 6, 877.5 + 6);
        Assert.InRange(secondThird, 1515.5 - 6, 1515.5 + 6);
    }

    [Fact]
    public void TheExpertRawZoneIsNotTheWholeScreen()
    {
        // The bug this exists for: guides spanning the whole screen fall inside
        // the control panel instead of on the frame.
        Assert.False(ShootingZone.ExpertRaw43.IsFullScreen);
        Assert.True(ShootingZone.ExpertRaw43.Width < 0.75);
    }

    [Fact]
    public void ZoneOffsetsScaleWithThePreview()
    {
        // The preview is resized and shown fullscreen, so the zone has to be
        // fractional rather than pixel-based.
        RectD small = ShootingZone.ExpertRaw43.Within(new RectD(0, 0, 1000, 466));
        RectD large = ShootingZone.ExpertRaw43.Within(new RectD(0, 0, 3088, 1440));

        Assert.Equal(small.X / 1000, large.X / 3088, 4);
        Assert.Equal(small.Width / 1000, large.Width / 3088, 4);
    }

    [Fact]
    public void ZoneRespectsTheOriginOfTheRenderedRectangle()
    {
        // The preview is letterboxed inside the control, so the rendered
        // rectangle rarely starts at the origin.
        RectD area = ShootingZone.ExpertRaw43.Within(new RectD(100, 50, 1000, 466));

        Assert.Equal(100 + (0.0776 * 1000), area.X, 1);
        Assert.Equal(50, area.Y, 1);
    }

    [Theory]
    [InlineData(-0.5, 0, 2, 1)]
    [InlineData(0.9, 0, 0.91, 1)]
    [InlineData(0, 0.98, 1, 0.99)]
    public void AnUnusableStoredZoneFallsBackToTheFullScreen(
        double left,
        double top,
        double right,
        double bottom)
    {
        // A collapsed or inverted zone would draw the guides into nothing, which
        // reads as the overlays being broken rather than misconfigured.
        ShootingZone zone = new ShootingZone(left, top, right, bottom).Normalised();

        Assert.True(zone.Width >= 0.05 && zone.Height >= 0.05, $"was {zone}");
    }

    [Fact]
    public void AnInvertedZoneIsRighted()
    {
        ShootingZone zone = new ShootingZone(0.7, 0.9, 0.2, 0.1).Normalised();

        Assert.True(zone.Left < zone.Right);
        Assert.True(zone.Top < zone.Bottom);
        Assert.Equal(0.2, zone.Left, 3);
        Assert.Equal(0.7, zone.Right, 3);
    }

    [Fact]
    public void NormalisingAGoodZoneChangesNothing() =>
        Assert.Equal(ShootingZone.ExpertRaw43, ShootingZone.ExpertRaw43.Normalised());
}
