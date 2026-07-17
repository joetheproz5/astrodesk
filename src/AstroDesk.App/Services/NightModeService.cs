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
                ["WindowBackgroundColor"] = Color.FromRgb(5, 7, 9),
                ["PanelBackgroundColor"] = Color.FromRgb(8, 11, 14),
                ["PanelRaisedColor"] = Color.FromRgb(13, 17, 21),
                ["PanelBorderColor"] = Color.FromRgb(28, 35, 42),
                ["PrimaryTextColor"] = Color.FromRgb(190, 198, 201),
                ["SecondaryTextColor"] = Color.FromRgb(103, 116, 122),
                ["AccentColor"] = Color.FromRgb(33, 139, 127),
                ["AccentMutedColor"] = Color.FromRgb(12, 44, 42),
                ["SuccessColor"] = Color.FromRgb(64, 151, 110),
                ["WarningColor"] = Color.FromRgb(175, 143, 69),
                ["DangerColor"] = Color.FromRgb(181, 76, 76),
                ["PreviewBackgroundColor"] = Color.FromRgb(1, 2, 3),
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
                ["WindowBackgroundColor"] = Color.FromRgb(9, 11, 15),
                ["PanelBackgroundColor"] = Color.FromRgb(16, 20, 26),
                ["PanelRaisedColor"] = Color.FromRgb(24, 30, 38),
                ["PanelBorderColor"] = Color.FromRgb(39, 49, 61),
                ["PrimaryTextColor"] = Color.FromRgb(237, 242, 244),
                ["SecondaryTextColor"] = Color.FromRgb(145, 160, 172),
                ["AccentColor"] = Color.FromRgb(45, 212, 191),
                ["AccentMutedColor"] = Color.FromRgb(18, 59, 58),
                ["SuccessColor"] = Color.FromRgb(94, 225, 164),
                ["WarningColor"] = Color.FromRgb(244, 201, 93),
                ["DangerColor"] = Color.FromRgb(255, 107, 107),
                ["PreviewBackgroundColor"] = Color.FromRgb(2, 3, 4),
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
