using AstroDesk.Capture.Histogram;

namespace AstroDesk.Capture.Tests;

public sealed class HistogramAnalyzerTests
{
    [Fact]
    public void BlackFrame_IsReportedAsShadowClipped()
    {
        HistogramAnalyzer analyzer = new();
        byte[] pixels = new byte[4 * 4 * 4];

        HistogramResult result = analyzer.Analyze(pixels, 4, 4, 16);

        Assert.Equal(16, result.Luminance[0]);
        Assert.Equal(100, result.ShadowClippingPercent, 6);
        Assert.Equal(0, result.HighlightClippingPercent, 6);
    }

    [Fact]
    public void WhiteFrame_IsReportedAsHighlightClipped()
    {
        HistogramAnalyzer analyzer = new();
        byte[] pixels = Enumerable.Repeat((byte)255, 2 * 2 * 4).ToArray();

        HistogramResult result = analyzer.Analyze(pixels, 2, 2, 8);

        Assert.Equal(4, result.Luminance[255]);
        Assert.Equal(0, result.ShadowClippingPercent, 6);
        Assert.Equal(100, result.HighlightClippingPercent, 6);
    }
}
