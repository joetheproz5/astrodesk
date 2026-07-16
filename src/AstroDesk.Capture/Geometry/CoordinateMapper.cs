namespace AstroDesk.Capture.Geometry;

public enum PreviewSizingMode
{
    Fit,
    PixelPerfect,
}

public enum FrameRotation
{
    Rotate0 = 0,
    Rotate90 = 90,
    Rotate180 = 180,
    Rotate270 = 270,
}

public sealed record CoordinateMappingContext(
    SizeD EmbeddedSizeDip,
    SizeD CapturedFrameSizePixels,
    SizeD ScrcpyClientSizePixels,
    double DpiScaleX = 1,
    double DpiScaleY = 1,
    FrameRotation Rotation = FrameRotation.Rotate0,
    PreviewSizingMode SizingMode = PreviewSizingMode.Fit,
    RectD? SourceViewNormalized = null);

public sealed record CoordinateMappingResult(
    bool IsInsidePreview,
    PointD EmbeddedPointDip,
    PointD CapturedPointPixels,
    PointD ScrcpyClientPointPixels,
    PointD NormalizedPoint,
    RectD RenderedPreviewRectDip);

public sealed class CoordinateMapper
{
    public RectD CalculateRenderedRect(CoordinateMappingContext context)
    {
        Validate(context);

        double containerWidthPx = context.EmbeddedSizeDip.Width * context.DpiScaleX;
        double containerHeightPx = context.EmbeddedSizeDip.Height * context.DpiScaleY;
        double frameWidth = context.CapturedFrameSizePixels.Width;
        double frameHeight = context.CapturedFrameSizePixels.Height;

        double renderedWidth;
        double renderedHeight;

        if (context.SizingMode == PreviewSizingMode.PixelPerfect)
        {
            double scale = Math.Min(
                1,
                Math.Min(containerWidthPx / frameWidth, containerHeightPx / frameHeight));
            renderedWidth = frameWidth * scale;
            renderedHeight = frameHeight * scale;
        }
        else
        {
            double scale = Math.Min(containerWidthPx / frameWidth, containerHeightPx / frameHeight);
            renderedWidth = frameWidth * scale;
            renderedHeight = frameHeight * scale;
        }

        double leftPx = (containerWidthPx - renderedWidth) / 2;
        double topPx = (containerHeightPx - renderedHeight) / 2;

        return new RectD(
            leftPx / context.DpiScaleX,
            topPx / context.DpiScaleY,
            renderedWidth / context.DpiScaleX,
            renderedHeight / context.DpiScaleY);
    }

    public CoordinateMappingResult MapToScrcpy(
        PointD embeddedPointDip,
        CoordinateMappingContext context)
    {
        RectD rendered = CalculateRenderedRect(context);
        if (!rendered.Contains(embeddedPointDip))
        {
            return new CoordinateMappingResult(
                false,
                embeddedPointDip,
                default,
                default,
                default,
                rendered);
        }

        double displayedU = Clamp01((embeddedPointDip.X - rendered.X) / rendered.Width);
        double displayedV = Clamp01((embeddedPointDip.Y - rendered.Y) / rendered.Height);
        RectD sourceView = GetSourceView(context);
        double sourceU = sourceView.X + (displayedU * sourceView.Width);
        double sourceV = sourceView.Y + (displayedV * sourceView.Height);

        PointD captured = new(
            sourceU * Math.Max(0, context.CapturedFrameSizePixels.Width - 1),
            sourceV * Math.Max(0, context.CapturedFrameSizePixels.Height - 1));

        PointD clientNormalized = Unrotate(sourceU, sourceV, context.Rotation);
        PointD client = new(
            clientNormalized.X * Math.Max(0, context.ScrcpyClientSizePixels.Width - 1),
            clientNormalized.Y * Math.Max(0, context.ScrcpyClientSizePixels.Height - 1));

        return new CoordinateMappingResult(
            true,
            embeddedPointDip,
            captured,
            client,
            clientNormalized,
            rendered);
    }

    public PointD MapFromScrcpy(
        PointD scrcpyClientPointPixels,
        CoordinateMappingContext context)
    {
        RectD rendered = CalculateRenderedRect(context);
        double clientU = Clamp01(
            scrcpyClientPointPixels.X / Math.Max(1, context.ScrcpyClientSizePixels.Width - 1));
        double clientV = Clamp01(
            scrcpyClientPointPixels.Y / Math.Max(1, context.ScrcpyClientSizePixels.Height - 1));
        PointD source = Rotate(clientU, clientV, context.Rotation);
        RectD sourceView = GetSourceView(context);
        PointD displayed = new(
            (source.X - sourceView.X) / sourceView.Width,
            (source.Y - sourceView.Y) / sourceView.Height);

        return new PointD(
            rendered.X + (displayed.X * rendered.Width),
            rendered.Y + (displayed.Y * rendered.Height));
    }

    private static PointD Rotate(double u, double v, FrameRotation rotation) => rotation switch
    {
        FrameRotation.Rotate0 => new PointD(u, v),
        FrameRotation.Rotate90 => new PointD(1 - v, u),
        FrameRotation.Rotate180 => new PointD(1 - u, 1 - v),
        FrameRotation.Rotate270 => new PointD(v, 1 - u),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
    };

    private static PointD Unrotate(double u, double v, FrameRotation rotation) => rotation switch
    {
        FrameRotation.Rotate0 => new PointD(u, v),
        FrameRotation.Rotate90 => new PointD(v, 1 - u),
        FrameRotation.Rotate180 => new PointD(1 - u, 1 - v),
        FrameRotation.Rotate270 => new PointD(1 - v, u),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation)),
    };

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static RectD GetSourceView(CoordinateMappingContext context) =>
        context.SourceViewNormalized ?? new RectD(0, 0, 1, 1);

    private static void Validate(CoordinateMappingContext context)
    {
        if (context.EmbeddedSizeDip.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Embedded preview size must be positive.");
        }

        if (context.CapturedFrameSizePixels.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "Captured frame size must be positive.");
        }

        if (context.ScrcpyClientSizePixels.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "scrcpy client size must be positive.");
        }

        if (context.DpiScaleX <= 0 || context.DpiScaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context), "DPI scales must be positive.");
        }

        RectD sourceView = GetSourceView(context);
        if (sourceView.Width <= 0 ||
            sourceView.Height <= 0 ||
            sourceView.X < 0 ||
            sourceView.Y < 0 ||
            sourceView.Right > 1 ||
            sourceView.Bottom > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                "The normalized source view must remain inside the captured frame.");
        }
    }
}
