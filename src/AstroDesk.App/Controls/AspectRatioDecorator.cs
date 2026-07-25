using System.Windows;
using System.Windows.Controls;

namespace AstroDesk.App.Controls;

/// <summary>
/// Arranges one child at a fixed width-to-height ratio while using as much of
/// the available area as possible. The child remains centered in unused space.
/// </summary>
public sealed class AspectRatioDecorator : Decorator
{
    public static readonly DependencyProperty AspectRatioProperty = DependencyProperty.Register(
        nameof(AspectRatio),
        typeof(double),
        typeof(AspectRatioDecorator),
        new FrameworkPropertyMetadata(
            1.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange),
        static value => value is double ratio && double.IsFinite(ratio) && ratio > 0);

    public double AspectRatio
    {
        get => (double)GetValue(AspectRatioProperty);
        set => SetValue(AspectRatioProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        if (Child is null)
        {
            return new Size();
        }

        Size fittedSize = Fit(constraint, AspectRatio, Child.DesiredSize);
        Child.Measure(fittedSize);

        double desiredWidth = double.IsInfinity(constraint.Width) ||
                              HorizontalAlignment != System.Windows.HorizontalAlignment.Stretch
            ? fittedSize.Width
            : constraint.Width;
        double desiredHeight = double.IsInfinity(constraint.Height) ||
                               VerticalAlignment != System.Windows.VerticalAlignment.Stretch
            ? fittedSize.Height
            : constraint.Height;
        return new Size(desiredWidth, desiredHeight);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is null)
        {
            return arrangeSize;
        }

        Size fittedSize = Fit(arrangeSize, AspectRatio, Child.DesiredSize);
        double left = Math.Max(0, (arrangeSize.Width - fittedSize.Width) / 2);
        double top = Math.Max(0, (arrangeSize.Height - fittedSize.Height) / 2);
        Child.Arrange(new Rect(new Point(left, top), fittedSize));
        return arrangeSize;
    }

    private static Size Fit(Size available, double aspectRatio, Size fallback)
    {
        bool widthIsFinite = double.IsFinite(available.Width);
        bool heightIsFinite = double.IsFinite(available.Height);

        if (!widthIsFinite && !heightIsFinite)
        {
            double fallbackHeight = fallback.Height > 0 ? fallback.Height : 1;
            return new Size(fallbackHeight * aspectRatio, fallbackHeight);
        }

        if (!widthIsFinite)
        {
            return new Size(available.Height * aspectRatio, available.Height);
        }

        if (!heightIsFinite)
        {
            return new Size(available.Width, available.Width / aspectRatio);
        }

        double widthFromHeight = available.Height * aspectRatio;
        if (widthFromHeight <= available.Width)
        {
            return new Size(widthFromHeight, available.Height);
        }

        return new Size(available.Width, available.Width / aspectRatio);
    }
}
