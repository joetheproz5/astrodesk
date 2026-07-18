using System.Windows;
using System.Windows.Media;
using AstroDesk.Core.Enums;

namespace AstroDesk.App.Services;

public interface INightModeService
{
    NightDisplayMode CurrentMode { get; }

    void Apply(NightDisplayMode mode);
}

public sealed class NightModeService : INightModeService
{
    public NightDisplayMode CurrentMode { get; private set; } = NightDisplayMode.NormalDark;

    public void Apply(NightDisplayMode mode)
    {
        CurrentMode = mode;
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        IReadOnlyDictionary<string, Color> palette = mode switch
        {
            NightDisplayMode.Dim => new Dictionary<string, Color>
            {
                ["WindowBackgroundColor"] = Color.FromRgb(3, 4, 6),
                ["PanelBackgroundColor"] = Color.FromRgb(6, 9, 12),
                ["PanelRaisedColor"] = Color.FromRgb(10, 14, 18),
                ["PanelBorderColor"] = Color.FromRgb(22, 28, 35),
                ["PrimaryTextColor"] = Color.FromRgb(150, 158, 167),
                ["SecondaryTextColor"] = Color.FromRgb(84, 93, 102),
                ["AccentColor"] = Color.FromRgb(122, 88, 40),
                ["AccentMutedColor"] = Color.FromRgb(20, 15, 8),
                ["SuccessColor"] = Color.FromRgb(52, 100, 70),
                ["WarningColor"] = Color.FromRgb(120, 95, 32),
                ["DangerColor"] = Color.FromRgb(112, 55, 55),
                ["PreviewBackgroundColor"] = Colors.Black,
                ["ReadoutColor"] = Color.FromRgb(95, 111, 124),
            },
            NightDisplayMode.FullRed => new Dictionary<string, Color>
            {
                ["WindowBackgroundColor"] = Color.FromRgb(5, 0, 0),
                ["PanelBackgroundColor"] = Color.FromRgb(12, 0, 0),
                ["PanelRaisedColor"] = Color.FromRgb(21, 0, 0),
                ["PanelBorderColor"] = Color.FromRgb(58, 0, 0),
                ["PrimaryTextColor"] = Color.FromRgb(224, 0, 0),
                ["SecondaryTextColor"] = Color.FromRgb(138, 0, 0),
                ["AccentColor"] = Color.FromRgb(255, 0, 0),
                ["AccentMutedColor"] = Color.FromRgb(74, 0, 0),
                ["SuccessColor"] = Color.FromRgb(180, 0, 0),
                ["WarningColor"] = Color.FromRgb(210, 0, 0),
                ["DangerColor"] = Color.FromRgb(255, 0, 0),
                ["PreviewBackgroundColor"] = Colors.Black,
                ["ReadoutColor"] = Color.FromRgb(176, 0, 0),
            },
            _ => new Dictionary<string, Color>
            {
                ["WindowBackgroundColor"] = Color.FromRgb(6, 8, 11),
                ["PanelBackgroundColor"] = Color.FromRgb(11, 15, 20),
                ["PanelRaisedColor"] = Color.FromRgb(17, 23, 30),
                ["PanelBorderColor"] = Color.FromRgb(29, 36, 46),
                ["PrimaryTextColor"] = Color.FromRgb(221, 228, 236),
                ["SecondaryTextColor"] = Color.FromRgb(118, 130, 143),
                ["AccentColor"] = Color.FromRgb(192, 138, 62),
                ["AccentMutedColor"] = Color.FromRgb(36, 26, 13),
                ["SuccessColor"] = Color.FromRgb(78, 154, 106),
                ["WarningColor"] = Color.FromRgb(184, 145, 47),
                ["DangerColor"] = Color.FromRgb(168, 82, 82),
                ["PreviewBackgroundColor"] = Colors.Black,
                ["ReadoutColor"] = Color.FromRgb(143, 168, 188),
            },
        };

        ResourceDictionary resources = FindPaletteResources(application.Resources)
            ?? application.Resources;
        foreach ((string key, Color color) in palette)
        {
            resources[key] = color;
            string brushKey = key.EndsWith("Color", StringComparison.Ordinal)
                ? $"{key[..^5]}Brush"
                : string.Empty;
            if (brushKey.Length > 0 && resources.Contains(brushKey))
            {
                SolidColorBrush brush = new(color);
                brush.Freeze();
                resources[brushKey] = brush;
            }
        }
    }

    private static ResourceDictionary? FindPaletteResources(ResourceDictionary resources)
    {
        if (resources.Contains("WindowBackgroundColor") &&
            resources.Contains("PrimaryTextColor"))
        {
            return resources;
        }

        foreach (ResourceDictionary merged in resources.MergedDictionaries)
        {
            ResourceDictionary? match = FindPaletteResources(merged);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
