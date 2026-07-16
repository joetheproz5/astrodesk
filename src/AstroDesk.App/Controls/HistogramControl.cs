using System.Windows;
using System.Windows.Media;
using AstroDesk.Capture.Histogram;

namespace AstroDesk.App.Controls;

public sealed class HistogramControl : FrameworkElement
{
    public static readonly DependencyProperty HistogramProperty = DependencyProperty.Register(
        nameof(Histogram),
        typeof(HistogramResult),
        typeof(HistogramControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowRgbProperty = DependencyProperty.Register(
        nameof(ShowRgb),
        typeof(bool),
        typeof(HistogramControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public HistogramResult? Histogram
    {
        get => (HistogramResult?)GetValue(HistogramProperty);
        set => SetValue(HistogramProperty, value);
    }

    public bool ShowRgb
    {
        get => (bool)GetValue(ShowRgbProperty);
        set => SetValue(ShowRgbProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect bounds = new(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(210, 5, 8, 10)),
            new Pen(FindBrush("PanelBorderBrush", Brushes.DimGray), 1),
            bounds,
            6,
            6);

        if (Histogram is null || ActualWidth < 10 || ActualHeight < 10)
        {
            return;
        }

        Rect chart = new(8, 7, ActualWidth - 16, ActualHeight - 14);
        DrawSeries(
            drawingContext,
            chart,
            Histogram.Luminance,
            new Pen(new SolidColorBrush(Color.FromArgb(210, 225, 225, 225)), 1.2));

        if (ShowRgb)
        {
            DrawSeries(
                drawingContext,
                chart,
                Histogram.Red,
                new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 80, 80)), 1));
            DrawSeries(
                drawingContext,
                chart,
                Histogram.Green,
                new Pen(new SolidColorBrush(Color.FromArgb(150, 90, 220, 100)), 1));
            DrawSeries(
                drawingContext,
                chart,
                Histogram.Blue,
                new Pen(new SolidColorBrush(Color.FromArgb(170, 90, 140, 255)), 1));
        }
    }

    private static void DrawSeries(
        DrawingContext context,
        Rect bounds,
        IReadOnlyList<float> values,
        Pen pen)
    {
        if (values.Count < 2)
        {
            return;
        }

        float max = values.Max();
        if (max <= 0)
        {
            return;
        }

        StreamGeometry geometry = new();
        using (StreamGeometryContext stream = geometry.Open())
        {
            stream.BeginFigure(new Point(bounds.Left, bounds.Bottom), false, false);
            for (int index = 0; index < values.Count; index++)
            {
                double x = bounds.Left + (index / (double)(values.Count - 1) * bounds.Width);
                double normalized = Math.Log10(values[index] + 1) / Math.Log10(max + 1);
                double y = bounds.Bottom - (normalized * bounds.Height);
                stream.LineTo(new Point(x, y), true, false);
            }
        }

        geometry.Freeze();
        pen.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }

    private Brush FindBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;
}
