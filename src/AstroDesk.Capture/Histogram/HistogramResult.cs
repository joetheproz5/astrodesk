namespace AstroDesk.Capture.Histogram;

public sealed record HistogramResult(
    IReadOnlyList<float> Luminance,
    IReadOnlyList<float> Red,
    IReadOnlyList<float> Green,
    IReadOnlyList<float> Blue,
    double HighlightClippingPercent,
    double ShadowClippingPercent,
    DateTimeOffset CalculatedAt);
