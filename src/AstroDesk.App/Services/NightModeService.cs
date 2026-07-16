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
                ["WindowBackgroundColor"] = Color.FromRgb(3, 5, 9),
                ["PanelBackgroundColor"] = Color.FromRgb(7, 10, 16),
                ["PanelRaisedColor"] = Color.FromRgb(12, 17, 26),
                ["PanelBorderColor"] = Color.FromRgb(27, 36, 52),
                ["PrimaryTextColor"] = Color.FromRgb(184, 190, 201),
                ["SecondaryTextColor"] = Color.FromRgb(99, 109, 126),
                ["AccentColor"] = Color.FromRgb(75, 116, 177),
                ["AccentMutedColor"] = Color.FromRgb(17, 38, 66),
                ["SuccessColor"] = Color.FromRgb(61, 139, 105),
                ["WarningColor"] = Color.FromRgb(169, 137, 74),
                ["DangerColor"] = Color.FromRgb(174, 69, 81),
                ["PreviewBackgroundColor"] = Color.FromRgb(0, 1, 4),
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
                ["WindowBackgroundColor"] = Color.FromRgb(7, 10, 17),
                ["PanelBackgroundColor"] = Color.FromRgb(13, 19, 31),
                ["PanelRaisedColor"] = Color.FromRgb(20, 29, 45),
                ["PanelBorderColor"] = Color.FromRgb(38, 51, 75),
                ["PrimaryTextColor"] = Color.FromRgb(243, 246, 251),
                ["SecondaryTextColor"] = Color.FromRgb(139, 154, 178),
                ["AccentColor"] = Color.FromRgb(105, 163, 255),
                ["AccentMutedColor"] = Color.FromRgb(24, 57, 99),
                ["SuccessColor"] = Color.FromRgb(83, 214, 160),
                ["WarningColor"] = Color.FromRgb(242, 198, 109),
                ["DangerColor"] = Color.FromRgb(255, 107, 122),
                ["PreviewBackgroundColor"] = Color.FromRgb(1, 3, 10),
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
