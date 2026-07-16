namespace AstroDesk.Core.Entities;

public sealed class OverlayPreset : EntityBase
{
    private OverlayPreset()
    {
        Name = string.Empty;
        ColorHex = "#FF4040";
    }

    public OverlayPreset(
        string name,
        bool showRuleOfThirds = true,
        bool showCenterCrosshair = true,
        bool showDiagonalGuides = false,
        bool showSafeArea = false,
        bool showHorizon = false,
        bool showCustomRectangle = false,
        bool showCircle = false,
        double opacity = 0.75,
        double lineThickness = 1.0,
        string colorHex = "#FF4040",
        bool isDefault = false)
    {
        Name = RequiredText(name, nameof(name), 200);
        ValidateOpacity(opacity);
        ValidateThickness(lineThickness);
        ColorHex = ValidateColor(colorHex);
        ShowRuleOfThirds = showRuleOfThirds;
        ShowCenterCrosshair = showCenterCrosshair;
        ShowDiagonalGuides = showDiagonalGuides;
        ShowSafeArea = showSafeArea;
        ShowHorizon = showHorizon;
        ShowCustomRectangle = showCustomRectangle;
        ShowCircle = showCircle;
        Opacity = opacity;
        LineThickness = lineThickness;
        IsDefault = isDefault;
    }

    public string Name { get; private set; }

    public bool ShowRuleOfThirds { get; private set; }

    public bool ShowCenterCrosshair { get; private set; }

    public bool ShowDiagonalGuides { get; private set; }

    public bool ShowSafeArea { get; private set; }

    public bool ShowHorizon { get; private set; }

    public bool ShowCustomRectangle { get; private set; }

    public bool ShowCircle { get; private set; }

    public double Opacity { get; private set; }

    public double LineThickness { get; private set; }

    public string ColorHex { get; private set; }

    public bool IsDefault { get; private set; }

    public void UpdateStyle(
        double opacity,
        double lineThickness,
        string colorHex,
        DateTimeOffset? timestamp = null)
    {
        ValidateOpacity(opacity);
        ValidateThickness(lineThickness);
        Opacity = opacity;
        LineThickness = lineThickness;
        ColorHex = ValidateColor(colorHex);
        MarkUpdated(timestamp);
    }

    private static string RequiredText(string value, string paramName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value cannot exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static void ValidateOpacity(double opacity)
    {
        if (!double.IsFinite(opacity) || opacity is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(opacity), "Opacity must be between 0 and 1.");
        }
    }

    private static void ValidateThickness(double lineThickness)
    {
        if (!double.IsFinite(lineThickness) || lineThickness is <= 0 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lineThickness),
                "Line thickness must be greater than 0 and no more than 20.");
        }
    }

    private static string ValidateColor(string colorHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(colorHex);
        var value = colorHex.Trim();
        if (value.Length is not (7 or 9) ||
            value[0] != '#' ||
            !value.AsSpan(1).ContainsAnyExcept("0123456789abcdefABCDEF"))
        {
            throw new ArgumentException("Color must be in #RRGGBB or #AARRGGBB format.", nameof(colorHex));
        }

        return value.ToUpperInvariant();
    }
}
