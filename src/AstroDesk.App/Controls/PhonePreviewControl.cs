using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AstroDesk.Capture.Geometry;

namespace AstroDesk.App.Controls;

public sealed class PhonePreviewControl : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(ImageSource),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RuleOfThirdsProperty = DependencyProperty.Register(
        nameof(RuleOfThirds),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CrosshairProperty = DependencyProperty.Register(
        nameof(Crosshair),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DiagonalGuidesProperty = DependencyProperty.Register(
        nameof(DiagonalGuides),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SafeAreaProperty = DependencyProperty.Register(
        nameof(SafeArea),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HorizonLineProperty = DependencyProperty.Register(
        nameof(HorizonLine),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CustomRectangleProperty = DependencyProperty.Register(
        nameof(CustomRectangle),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CircleOverlayProperty = DependencyProperty.Register(
        nameof(CircleOverlay),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CustomRectangleWidthPercentProperty = DependencyProperty.Register(
        nameof(CustomRectangleWidthPercent),
        typeof(double),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(70d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CustomRectangleHeightPercentProperty = DependencyProperty.Register(
        nameof(CustomRectangleHeightPercent),
        typeof(double),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(70d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OverlayBrushProperty = DependencyProperty.Register(
        nameof(OverlayBrush),
        typeof(Brush),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(
            Brushes.OrangeRed,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OverlayOpacityProperty = DependencyProperty.Register(
        nameof(OverlayOpacity),
        typeof(double),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(0.72, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty OverlayThicknessProperty = DependencyProperty.Register(
        nameof(OverlayThickness),
        typeof(double),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomScaleProperty = DependencyProperty.Register(
        nameof(ZoomScale),
        typeof(double),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomCenterProperty = DependencyProperty.Register(
        nameof(ZoomCenter),
        typeof(Point),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(
            new Point(0.5, 0.5),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FocusMagnifierProperty = DependencyProperty.Register(
        nameof(FocusMagnifier),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsFrozenProperty = DependencyProperty.Register(
        nameof(IsFrozen),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PixelPerfectProperty = DependencyProperty.Register(
        nameof(PixelPerfect),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText),
        typeof(string),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText),
        typeof(string),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata("Phone preview is not running", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DebugOverlayProperty = DependencyProperty.Register(
        nameof(DebugOverlay),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DebugTextProperty = DependencyProperty.Register(
        nameof(DebugText),
        typeof(string),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RotationProperty = DependencyProperty.Register(
        nameof(Rotation),
        typeof(FrameRotation),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(FrameRotation.Rotate0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ScrcpyClientSizeProperty = DependencyProperty.Register(
        nameof(ScrcpyClientSize),
        typeof(Size),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(default(Size), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// Draws the framing guides and nothing else, over a fully transparent
    /// background.
    /// </summary>
    /// <remarks>
    /// This is what lets the guides appear on top of the real scrcpy video. WPF
    /// cannot composite over the native child window that scrcpy is reparented
    /// into, so an instance in this mode is hosted in a separate transparent
    /// window floating above it. Sharing the control rather than reimplementing
    /// the geometry keeps the two surfaces from drifting apart.
    /// </remarks>
    public static readonly DependencyProperty GuidesOnlyProperty = DependencyProperty.Register(
        nameof(GuidesOnly),
        typeof(bool),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>
    /// The part of the mirrored screen the camera actually shoots. Guides are
    /// drawn inside this rather than across the whole phone display.
    /// </summary>
    public static readonly DependencyProperty ShootingZoneProperty = DependencyProperty.Register(
        nameof(ShootingZone),
        typeof(ShootingZone),
        typeof(PhonePreviewControl),
        new FrameworkPropertyMetadata(
            ShootingZone.FullScreen,
            FrameworkPropertyMetadataOptions.AffectsRender));

    private Point _lastCursorPosition;
    private TouchDevice? _activeTouchDevice;
    private long _suppressMouseUntilTimestamp;

    public PhonePreviewControl()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        IsManipulationEnabled = false;
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);

        // Listen during the tunneling phase so a parent ScrollViewer cannot turn a
        // phone tap into a dashboard pan before the preview sees it. Mouse handlers
        // deliberately do not opt into handled events: handled touch must never be
        // re-admitted as WPF's promoted mouse stream.
        AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(HandleMouseDown), false);
        AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(HandleMouseMove), false);
        AddHandler(Mouse.PreviewMouseUpEvent, new MouseButtonEventHandler(HandleMouseUp), false);
        AddHandler(Mouse.PreviewMouseWheelEvent, new MouseWheelEventHandler(HandleMouseWheel), false);
        AddHandler(PreviewTouchDownEvent, new EventHandler<TouchEventArgs>(HandleTouchDown), true);
        AddHandler(PreviewTouchMoveEvent, new EventHandler<TouchEventArgs>(HandleTouchMove), true);
        AddHandler(PreviewTouchUpEvent, new EventHandler<TouchEventArgs>(HandleTouchUp), true);
        LostTouchCapture += HandleLostTouchCapture;
        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(HandleKeyDown), true);
        AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(HandleTextInput), true);
    }

    public event EventHandler<PreviewInputEventArgs>? PreviewInput;

    public ImageSource? Source
    {
        get => (ImageSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool RuleOfThirds
    {
        get => (bool)GetValue(RuleOfThirdsProperty);
        set => SetValue(RuleOfThirdsProperty, value);
    }

    public bool Crosshair
    {
        get => (bool)GetValue(CrosshairProperty);
        set => SetValue(CrosshairProperty, value);
    }

    public bool DiagonalGuides
    {
        get => (bool)GetValue(DiagonalGuidesProperty);
        set => SetValue(DiagonalGuidesProperty, value);
    }

    public bool SafeArea
    {
        get => (bool)GetValue(SafeAreaProperty);
        set => SetValue(SafeAreaProperty, value);
    }

    public bool HorizonLine
    {
        get => (bool)GetValue(HorizonLineProperty);
        set => SetValue(HorizonLineProperty, value);
    }

    public bool CustomRectangle
    {
        get => (bool)GetValue(CustomRectangleProperty);
        set => SetValue(CustomRectangleProperty, value);
    }

    public bool CircleOverlay
    {
        get => (bool)GetValue(CircleOverlayProperty);
        set => SetValue(CircleOverlayProperty, value);
    }

    public double CustomRectangleWidthPercent
    {
        get => (double)GetValue(CustomRectangleWidthPercentProperty);
        set => SetValue(CustomRectangleWidthPercentProperty, value);
    }

    public double CustomRectangleHeightPercent
    {
        get => (double)GetValue(CustomRectangleHeightPercentProperty);
        set => SetValue(CustomRectangleHeightPercentProperty, value);
    }

    public Brush OverlayBrush
    {
        get => (Brush)GetValue(OverlayBrushProperty);
        set => SetValue(OverlayBrushProperty, value);
    }

    public double OverlayOpacity
    {
        get => (double)GetValue(OverlayOpacityProperty);
        set => SetValue(OverlayOpacityProperty, value);
    }

    public double OverlayThickness
    {
        get => (double)GetValue(OverlayThicknessProperty);
        set => SetValue(OverlayThicknessProperty, value);
    }

    public double ZoomScale
    {
        get => (double)GetValue(ZoomScaleProperty);
        set => SetValue(ZoomScaleProperty, value);
    }

    public Point ZoomCenter
    {
        get => (Point)GetValue(ZoomCenterProperty);
        set => SetValue(ZoomCenterProperty, value);
    }

    public bool FocusMagnifier
    {
        get => (bool)GetValue(FocusMagnifierProperty);
        set => SetValue(FocusMagnifierProperty, value);
    }

    public bool IsFrozen
    {
        get => (bool)GetValue(IsFrozenProperty);
        set => SetValue(IsFrozenProperty, value);
    }

    public bool PixelPerfect
    {
        get => (bool)GetValue(PixelPerfectProperty);
        set => SetValue(PixelPerfectProperty, value);
    }

    public string? ErrorText
    {
        get => (string?)GetValue(ErrorTextProperty);
        set => SetValue(ErrorTextProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public bool DebugOverlay
    {
        get => (bool)GetValue(DebugOverlayProperty);
        set => SetValue(DebugOverlayProperty, value);
    }

    public string DebugText
    {
        get => (string)GetValue(DebugTextProperty);
        set => SetValue(DebugTextProperty, value);
    }

    public FrameRotation Rotation
    {
        get => (FrameRotation)GetValue(RotationProperty);
        set => SetValue(RotationProperty, value);
    }

    public Size ScrcpyClientSize
    {
        get => (Size)GetValue(ScrcpyClientSizeProperty);
        set => SetValue(ScrcpyClientSizeProperty, value);
    }

    public bool GuidesOnly
    {
        get => (bool)GetValue(GuidesOnlyProperty);
        set => SetValue(GuidesOnlyProperty, value);
    }

    public ShootingZone ShootingZone
    {
        get => (ShootingZone)GetValue(ShootingZoneProperty);
        set => SetValue(ShootingZoneProperty, value);
    }

    public CoordinateMappingContext? CreateMappingContext()
    {
        if (Source is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        Size clientSize = ScrcpyClientSize.Width > 0 && ScrcpyClientSize.Height > 0
            ? ScrcpyClientSize
            : new Size(Source.Width, Source.Height);
        Rect zoom = GetNormalizedZoomRect();
        return new CoordinateMappingContext(
            new SizeD(ActualWidth, ActualHeight),
            new SizeD(Source.Width, Source.Height),
            new SizeD(clientSize.Width, clientSize.Height),
            dpi.DpiScaleX,
            dpi.DpiScaleY,
            Rotation,
            PixelPerfect ? PreviewSizingMode.PixelPerfect : PreviewSizingMode.Fit,
            new RectD(zoom.X, zoom.Y, zoom.Width, zoom.Height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        // No background fill: every pixel this mode does not draw has to stay
        // transparent so the scrcpy video underneath shows through. The guides
        // span the whole surface because the host is aspect-locked to the phone,
        // so scrcpy's own letterboxing is negligible.
        if (GuidesOnly)
        {
            Rect surface = new(RenderSize);
            DrawOverlays(drawingContext, surface);

            // The loupe needs pixels, and in this mode the picture on screen
            // belongs to scrcpy's own window, which WPF cannot read. It is drawn
            // from the captured frame instead - the same frames the histogram and
            // screenshots already use - so it works here without the overlay
            // having to render the video itself.
            if (FocusMagnifier)
            {
                DrawFocusMagnifier(drawingContext, surface);
            }

            return;
        }

        drawingContext.DrawRectangle(
            FindBrush("PreviewBackgroundBrush", Brushes.Black),
            null,
            new Rect(RenderSize));

        if (Source is null)
        {
            DrawCenteredStatus(drawingContext, StatusText, FindBrush("SecondaryTextBrush", Brushes.Gray));
            return;
        }

        Rect renderedRect = CalculateRenderedRect();
        Rect sourceRect = GetNormalizedZoomRect();
        ImageBrush imageBrush = new(Source)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = sourceRect,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
        };
        drawingContext.DrawRectangle(imageBrush, null, renderedRect);
        DrawOverlays(drawingContext, renderedRect);

        if (FocusMagnifier)
        {
            DrawFocusMagnifier(drawingContext, renderedRect);
        }

        if (IsFrozen)
        {
            DrawBadge(drawingContext, "FROZEN", renderedRect.Left + 10, renderedRect.Top + 10);
        }

        if (!string.IsNullOrWhiteSpace(ErrorText))
        {
            DrawError(drawingContext, renderedRect, ErrorText!);
        }

        if (DebugOverlay)
        {
            DrawDebug(drawingContext, renderedRect);
        }
    }

    private Rect CalculateRenderedRect()
    {
        CoordinateMappingContext context = CreateMappingContext()!;
        RectD rect = new CoordinateMapper().CalculateRenderedRect(context);
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private Rect GetNormalizedZoomRect()
    {
        double scale = ZoomScale is 2 or 4 or 8 ? ZoomScale : 1;
        double width = 1 / scale;
        double height = 1 / scale;
        double halfWidth = width / 2;
        double halfHeight = height / 2;
        double centerX = Math.Clamp(ZoomCenter.X, halfWidth, 1 - halfWidth);
        double centerY = Math.Clamp(ZoomCenter.Y, halfHeight, 1 - halfHeight);
        return new Rect(centerX - halfWidth, centerY - halfHeight, width, height);
    }

    /// <summary>
    /// Narrows a rendered preview rectangle to the camera's shooting zone.
    /// </summary>
    private Rect ApplyShootingZone(Rect rendered)
    {
        ShootingZone zone = ShootingZone.Normalised();
        if (zone.IsFullScreen)
        {
            return rendered;
        }

        RectD area = zone.Within(new RectD(rendered.X, rendered.Y, rendered.Width, rendered.Height));
        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    private void DrawOverlays(DrawingContext context, Rect renderedBounds)
    {
        Rect bounds = ApplyShootingZone(renderedBounds);

        // Outline the zone whenever it is not the whole screen. Guides that sit
        // inside an invisible boundary are impossible to sanity-check against the
        // camera's own framing; a faint edge makes a misaligned zone obvious
        // instead of quietly wrong.
        if (bounds != renderedBounds)
        {
            Brush edgeBrush = OverlayBrush.Clone();
            edgeBrush.Opacity = Math.Clamp(OverlayOpacity, 0, 1) * 0.45;
            Pen edgePen = new(edgeBrush, 1)
            {
                DashStyle = new DashStyle([4, 4], 0),
            };
            edgePen.Freeze();
            context.DrawRectangle(null, edgePen, bounds);
        }

        Brush brush = OverlayBrush.Clone();
        brush.Opacity = Math.Clamp(OverlayOpacity, 0, 1);
        Pen pen = new(brush, Math.Clamp(OverlayThickness, 0.5, 8));
        pen.Freeze();

        if (RuleOfThirds)
        {
            context.DrawLine(
                pen,
                new Point(bounds.Left + (bounds.Width / 3), bounds.Top),
                new Point(bounds.Left + (bounds.Width / 3), bounds.Bottom));
            context.DrawLine(
                pen,
                new Point(bounds.Left + (bounds.Width * 2 / 3), bounds.Top),
                new Point(bounds.Left + (bounds.Width * 2 / 3), bounds.Bottom));
            context.DrawLine(
                pen,
                new Point(bounds.Left, bounds.Top + (bounds.Height / 3)),
                new Point(bounds.Right, bounds.Top + (bounds.Height / 3)));
            context.DrawLine(
                pen,
                new Point(bounds.Left, bounds.Top + (bounds.Height * 2 / 3)),
                new Point(bounds.Right, bounds.Top + (bounds.Height * 2 / 3)));
        }

        if (Crosshair)
        {
            double centerX = bounds.Left + (bounds.Width / 2);
            double centerY = bounds.Top + (bounds.Height / 2);
            double length = Math.Min(bounds.Width, bounds.Height) * 0.06;
            context.DrawLine(pen, new Point(centerX - length, centerY), new Point(centerX + length, centerY));
            context.DrawLine(pen, new Point(centerX, centerY - length), new Point(centerX, centerY + length));
        }

        if (DiagonalGuides)
        {
            context.DrawLine(pen, bounds.TopLeft, bounds.BottomRight);
            context.DrawLine(pen, bounds.TopRight, bounds.BottomLeft);
        }

        if (SafeArea)
        {
            Rect safe = InflateByPercent(bounds, -0.08);
            context.DrawRectangle(null, pen, safe);
        }

        if (HorizonLine)
        {
            double y = bounds.Top + (bounds.Height / 2);
            context.DrawLine(pen, new Point(bounds.Left, y), new Point(bounds.Right, y));
        }

        if (CustomRectangle)
        {
            double widthPercent = Math.Clamp(CustomRectangleWidthPercent, 5, 100) / 100;
            double heightPercent = Math.Clamp(CustomRectangleHeightPercent, 5, 100) / 100;
            Rect custom = new(
                bounds.Left + (bounds.Width * (1 - widthPercent) / 2),
                bounds.Top + (bounds.Height * (1 - heightPercent) / 2),
                bounds.Width * widthPercent,
                bounds.Height * heightPercent);
            context.DrawRectangle(null, pen, custom);
        }

        if (CircleOverlay)
        {
            double radius = Math.Min(bounds.Width, bounds.Height) * 0.32;
            context.DrawEllipse(
                null,
                pen,
                new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2)),
                radius,
                radius);
        }
    }

    /// <summary>
    /// Draws the magnified focus check, inside the shooting zone.
    /// </summary>
    /// <remarks>
    /// Both the crop and the panel are kept within the zone. Magnifying the whole
    /// mirrored screen would spend the loupe on the camera's control panel, and
    /// parking the panel outside the zone would cover controls rather than the
    /// dead space beside the frame.
    /// </remarks>
    private void DrawFocusMagnifier(DrawingContext context, Rect renderedRect)
    {
        Rect zone = ApplyShootingZone(renderedRect);
        double width = Math.Min(220, zone.Width * 0.35);
        double height = width * 0.65;
        Rect magnifier = new(
            zone.Right - width - 12,
            zone.Top + 12,
            width,
            height);

        if (Source is null)
        {
            // Says why rather than showing an empty box. In overlay mode the
            // picture belongs to scrcpy, so a silent black rectangle would look
            // like a broken loupe rather than a missing capture.
            DrawLoupeUnavailable(context, magnifier);
            return;
        }

        // ZoomCenter is taken as a position within the shooting zone, so its
        // default of dead centre lands in the middle of the frame the camera is
        // actually shooting rather than the middle of the phone's screen.
        RectD cropped = ShootingZone.Normalised().CropAround(
            ZoomCenter.X,
            ZoomCenter.Y,
            0.12,
            0.12 * (height / width));
        Rect crop = new(cropped.X, cropped.Y, cropped.Width, cropped.Height);

        ImageBrush brush = new(Source)
        {
            Stretch = Stretch.Fill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = crop,
        };
        context.DrawRectangle(FindBrush("PanelBackgroundBrush", Brushes.Black), null, magnifier);
        context.DrawRectangle(brush, new Pen(FindBrush("AccentBrush", Brushes.OrangeRed), 2), magnifier);
        DrawBadge(context, "FOCUS", magnifier.Left + 7, magnifier.Top + 7);
    }

    private void DrawLoupeUnavailable(DrawingContext context, Rect magnifier)
    {
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(210, 12, 15, 18)),
            new Pen(FindBrush("PanelBorderBrush", Brushes.DimGray), 1),
            magnifier);

        FormattedText text = CreateText(
            "Loupe needs the captured frame",
            11,
            FindBrush("SecondaryTextBrush", Brushes.Gray));
        text.MaxTextWidth = Math.Max(1, magnifier.Width - 16);
        text.TextAlignment = TextAlignment.Center;
        context.DrawText(
            text,
            new Point(
                magnifier.Left + 8,
                magnifier.Top + Math.Max(6, (magnifier.Height - text.Height) / 2)));
    }

    private void DrawError(DrawingContext context, Rect bounds, string message)
    {
        context.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(210, 20, 4, 4)),
            null,
            bounds);
        DrawCenteredStatus(context, message, FindBrush("DangerBrush", Brushes.IndianRed));
    }

    private void DrawDebug(DrawingContext context, Rect bounds)
    {
        string value = string.IsNullOrWhiteSpace(DebugText)
            ? $"Cursor DIP: {_lastCursorPosition.X:0.0}, {_lastCursorPosition.Y:0.0}\n" +
              $"Frame: {Source?.Width:0} x {Source?.Height:0}\n" +
              $"Client: {ScrcpyClientSize.Width:0} x {ScrcpyClientSize.Height:0}\n" +
              $"Rotation: {(int)Rotation}°"
            : DebugText;
        FormattedText text = CreateText(value, 11, FindBrush("PrimaryTextBrush", Brushes.White));
        Rect panel = new(bounds.Left + 10, bounds.Bottom - text.Height - 24, text.Width + 18, text.Height + 14);
        context.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(220, 5, 7, 9)),
            new Pen(FindBrush("PanelBorderBrush", Brushes.DimGray), 1),
            panel,
            5,
            5);
        context.DrawText(text, new Point(panel.Left + 9, panel.Top + 7));
    }

    private void DrawBadge(DrawingContext context, string text, double x, double y)
    {
        FormattedText formatted = CreateText(text, 10, FindBrush("PrimaryTextBrush", Brushes.White));
        Rect background = new(x, y, formatted.Width + 12, formatted.Height + 6);
        context.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(215, 12, 15, 18)),
            null,
            background,
            4,
            4);
        context.DrawText(formatted, new Point(x + 6, y + 3));
    }

    private void DrawCenteredStatus(DrawingContext context, string text, Brush brush)
    {
        FormattedText formatted = CreateText(text, 14, brush);
        formatted.MaxTextWidth = Math.Max(1, ActualWidth - 24);
        formatted.MaxTextHeight = Math.Max(1, ActualHeight - 24);
        formatted.TextAlignment = TextAlignment.Center;
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        context.DrawText(
            formatted,
            new Point(
                12,
                Math.Max(12, (ActualHeight - formatted.Height) / 2)));
    }

    private FormattedText CreateText(string text, double size, Brush brush) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface("Segoe UI Variable Text"),
        size,
        brush,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private Brush FindBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private static Rect InflateByPercent(Rect bounds, double percent)
    {
        double x = bounds.Width * percent;
        double y = bounds.Height * percent;
        Rect result = bounds;
        result.Inflate(x, y);
        return result;
    }

    private void HandleMouseDown(object sender, MouseButtonEventArgs args)
    {
        if (ShouldSuppressMouse())
        {
            args.Handled = true;
            return;
        }

        Focus();
        CaptureMouse();
        Point position = args.GetPosition(this);
        _lastCursorPosition = position;
        PreviewInput?.Invoke(
            this,
            new PreviewInputEventArgs(PreviewInputKind.MouseDown, position, args.ChangedButton));
        InvalidateVisual();
        args.Handled = true;
    }

    private void HandleMouseMove(object sender, MouseEventArgs args)
    {
        if (ShouldSuppressMouse())
        {
            args.Handled = true;
            return;
        }

        Point position = args.GetPosition(this);
        _lastCursorPosition = position;
        PreviewInput?.Invoke(this, new PreviewInputEventArgs(PreviewInputKind.MouseMove, position));
        if (DebugOverlay)
        {
            InvalidateVisual();
        }
    }

    private void HandleMouseUp(object sender, MouseButtonEventArgs args)
    {
        if (ShouldSuppressMouse())
        {
            args.Handled = true;
            return;
        }

        Point position = args.GetPosition(this);
        _lastCursorPosition = position;
        PreviewInput?.Invoke(
            this,
            new PreviewInputEventArgs(PreviewInputKind.MouseUp, position, args.ChangedButton));
        ReleaseMouseCapture();
        args.Handled = true;
    }

    private void HandleMouseWheel(object sender, MouseWheelEventArgs args)
    {
        if (ShouldSuppressMouse())
        {
            args.Handled = true;
            return;
        }

        Point position = args.GetPosition(this);
        PreviewInput?.Invoke(
            this,
            new PreviewInputEventArgs(PreviewInputKind.MouseWheel, position, wheelDelta: args.Delta));
        args.Handled = true;
    }

    private void HandleTouchDown(object? sender, TouchEventArgs args)
    {
        SuppressPromotedMouse();
        if (_activeTouchDevice is not null)
        {
            args.Handled = true;
            return;
        }

        Focus();
        _activeTouchDevice = args.TouchDevice;
        Point position = args.GetTouchPoint(this).Position;
        _lastCursorPosition = position;
        _ = CaptureTouch(args.TouchDevice);
        PreviewInput?.Invoke(this, new PreviewInputEventArgs(PreviewInputKind.TouchDown, position));
        InvalidateVisual();
        args.Handled = true;
    }

    private void HandleTouchMove(object? sender, TouchEventArgs args)
    {
        SuppressPromotedMouse();
        if (_activeTouchDevice != args.TouchDevice)
        {
            args.Handled = true;
            return;
        }

        Point position = args.GetTouchPoint(this).Position;
        _lastCursorPosition = position;
        PreviewInput?.Invoke(this, new PreviewInputEventArgs(PreviewInputKind.TouchMove, position));
        if (DebugOverlay)
        {
            InvalidateVisual();
        }

        args.Handled = true;
    }

    private void HandleTouchUp(object? sender, TouchEventArgs args)
    {
        SuppressPromotedMouse();
        if (_activeTouchDevice != args.TouchDevice)
        {
            args.Handled = true;
            return;
        }

        Point position = args.GetTouchPoint(this).Position;
        _lastCursorPosition = position;
        PreviewInput?.Invoke(this, new PreviewInputEventArgs(PreviewInputKind.TouchUp, position));
        _activeTouchDevice = null;
        ReleaseTouchCapture(args.TouchDevice);
        args.Handled = true;
    }

    private void HandleLostTouchCapture(object? sender, TouchEventArgs args)
    {
        SuppressPromotedMouse();
        if (_activeTouchDevice != args.TouchDevice)
        {
            return;
        }

        _activeTouchDevice = null;
        PreviewInput?.Invoke(this, new PreviewInputEventArgs(PreviewInputKind.TouchUp, _lastCursorPosition));
    }

    private void SuppressPromotedMouse()
    {
        _suppressMouseUntilTimestamp = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 2);
    }

    private bool ShouldSuppressMouse() =>
        _activeTouchDevice is not null || Stopwatch.GetTimestamp() <= _suppressMouseUntilTimestamp;

    private void HandleKeyDown(object sender, KeyEventArgs args)
    {
        PreviewInput?.Invoke(
            this,
            new PreviewInputEventArgs(PreviewInputKind.KeyDown, _lastCursorPosition, key: args.Key));
        args.Handled = true;
    }

    private void HandleTextInput(object sender, TextCompositionEventArgs args)
    {
        if (!string.IsNullOrEmpty(args.Text))
        {
            PreviewInput?.Invoke(
                this,
                new PreviewInputEventArgs(
                    PreviewInputKind.TextInput,
                    _lastCursorPosition,
                    text: args.Text));
            args.Handled = true;
        }
    }
}
