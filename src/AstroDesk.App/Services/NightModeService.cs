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
                ["WindowBackgroundColor"] = Color.FromRgb(4, 5, 6),
                ["PanelBackgroundColor"] = Color.FromRgb(8, 10, 12),
                ["PanelRaisedColor"] = Color.FromRgb(13, 16, 19),
                ["PanelBorderColor"] = Color.FromRgb(28, 32, 36),
                ["PrimaryTextColor"] = Color.FromRgb(181, 186, 191),
                ["SecondaryTextColor"] = Color.FromRgb(105, 111, 117),
                ["AccentColor"] = Color.FromRgb(162, 77, 41),
                ["AccentMutedColor"] = Color.FromRgb(65, 34, 22),
                ["SuccessColor"] = Color.FromRgb(70, 136, 98),
                ["WarningColor"] = Color.FromRgb(176, 125, 49),
                ["DangerColor"] = Color.FromRgb(177, 67, 67),
                ["PreviewBackgroundColor"] = Color.FromRgb(1, 1, 1),
            },
            NightDisplayMode.FullRed => new Dictionary<string, Color>
            {
                ["WindowBackgroundColor"] = Color.FromRgb(6, 0, 0),
                ["PanelBackgroundColor"] = Color.FromRgb(14, 0, 0),
                ["PanelRaisedColor"] = Color.FromRgb(24, 0, 0),
                ["PanelBorderColor"] = Color.FromRgb(62, 0, 0),
                ["PrimaryTextColor"] = Color.FromRgb(232, 0, 0),
                ["SecondaryTextColor"] = Color.FromRgb(146, 0, 0),
                ["AccentColor"] = Color.FromRgb(222, 0, 0),
                ["AccentMutedColor"] = Color.FromRgb(83, 0, 0),
                ["SuccessColor"] = Color.FromRgb(191, 0, 0),
                ["WarningColor"] = Color.FromRgb(219, 0, 0),
                ["DangerColor"] = Color.FromRgb(255, 0, 0),
                ["PreviewBackgroundColor"] = Colors.Black,
            },
            _ => new Dictionary<string, Color>
            {
                ["WindowBackgroundColor"] = Color.FromRgb(9, 12, 16),
                ["PanelBackgroundColor"] = Color.FromRgb(16, 21, 28),
                ["PanelRaisedColor"] = Color.FromRgb(23, 30, 39),
                ["PanelBorderColor"] = Color.FromRgb(40, 49, 61),
                ["PrimaryTextColor"] = Color.FromRgb(232, 237, 242),
                ["SecondaryTextColor"] = Color.FromRgb(143, 155, 168),
                ["AccentColor"] = Color.FromRgb(212, 106, 58),
                ["AccentMutedColor"] = Color.FromRgb(96, 51, 33),
                ["SuccessColor"] = Color.FromRgb(93, 187, 138),
                ["WarningColor"] = Color.FromRgb(225, 166, 74),
                ["DangerColor"] = Color.FromRgb(217, 89, 89),
                ["PreviewBackgroundColor"] = Color.FromRgb(2, 3, 3),
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
