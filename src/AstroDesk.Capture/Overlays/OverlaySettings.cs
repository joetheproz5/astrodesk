namespace AstroDesk.Capture.Overlays;

public enum OverlayColor
{
    Red,
    Amber,
    White,
    Green,
    Cyan,
}

public sealed record OverlaySettings
{
    public bool RuleOfThirds { get; init; } = true;

    public bool CenterCrosshair { get; init; } = true;

    public bool DiagonalGuides { get; init; }

    public bool SafeArea { get; init; }

    public bool HorizonLine { get; init; }

    public bool CustomRectangle { get; init; }

    public bool Circle { get; init; }

    public double Opacity { get; init; } = 0.7;

    public double LineThickness { get; init; } = 1;

    public OverlayColor Color { get; init; } = OverlayColor.Red;

    public double CustomRectangleWidthPercent { get; init; } = 70;

    public double CustomRectangleHeightPercent { get; init; } = 70;
}
