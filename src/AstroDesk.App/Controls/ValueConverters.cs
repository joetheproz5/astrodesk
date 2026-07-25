using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace AstroDesk.App.Controls;

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not Visibility.Visible;
}

/// <summary>
/// Renders a byte count the way someone deciding what to delete reads it.
/// </summary>
public sealed class ByteSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is long bytes ? AstroDesk.Stacking.CaptureLibrary.FormatSize(bytes) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a capture path into a small thumbnail for the library list.
/// </summary>
/// <remarks>
/// Decodes through the same path as the live preview, so a DNG resolves via its
/// embedded JPEG rather than failing - Windows has no codec for the raw itself.
/// A frame that cannot be read yields no image rather than an error, because a
/// half-written file is a normal thing to meet while captures are still landing.
/// </remarks>
public sealed class CapturePreviewConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return AstroDesk.Stacking.PreviewDecoder.LoadDownscaled(path, 160);
        }
        catch (Exception exception) when (AstroDesk.Stacking.PreviewDecoder.IsUnreadable(exception))
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BooleanNegationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not true;
}
