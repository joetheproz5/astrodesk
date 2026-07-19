namespace AstroDesk.Capture.Geometry;

/// <summary>
/// The part of the mirrored phone screen that the camera actually shoots.
/// </summary>
/// <remarks>
/// <para>
/// The preview shows the whole phone screen, but a camera app spends a good deal
/// of that on its own chrome. Expert RAW puts a thin information strip down one
/// side and a wide column of controls down the other, leaving a 4:3 viewfinder
/// occupying a little under two thirds of the width.
/// </para>
/// <para>
/// Framing guides drawn across the whole screen are therefore not merely
/// decorative, they are wrong: the thirds fall inside the control panel rather
/// than on the frame, so composing to them puts the subject in the wrong place
/// in the actual photograph. The guides are drawn inside this zone instead.
/// </para>
/// <para>
/// Values are fractions of the preview, so they survive the preview being
/// resized or shown fullscreen.
/// </para>
/// </remarks>
public readonly record struct ShootingZone(double Left, double Top, double Right, double Bottom)
{
    /// <summary>
    /// The whole screen. Correct when no camera app is running, and the safe
    /// fallback for an app whose layout has not been measured.
    /// </summary>
    public static ShootingZone FullScreen { get; } = new(0, 0, 1, 1);

    /// <summary>
    /// Samsung Expert RAW in landscape, shooting 4:3.
    /// </summary>
    /// <remarks>
    /// Measured from the app's own thirds grid on a 1440x3088 S23 Ultra: the
    /// grid lines sit at x=877.5 and x=1515.5 across the landscape frame, giving
    /// a viewfinder 1914 px wide against the 1920 px that a full-height 4:3 frame
    /// predicts, and horizontal lines at y=480.5 and y=958.5 confirming it spans
    /// the full height. Reading the app's own grid rather than guessing at the
    /// edges is what makes this exact.
    /// </remarks>
    public static ShootingZone ExpertRaw43 { get; } = new(0.0776, 0, 0.6994, 1);

    public double Width => Right - Left;

    public double Height => Bottom - Top;

    public bool IsFullScreen =>
        Left <= 0 && Top <= 0 && Right >= 1 && Bottom >= 1;

    /// <summary>
    /// Maps this zone onto a rendered preview rectangle.
    /// </summary>
    public RectD Within(RectD rendered) =>
        new(
            rendered.X + (rendered.Width * Left),
            rendered.Y + (rendered.Height * Top),
            rendered.Width * Width,
            rendered.Height * Height);

    /// <summary>
    /// A crop for the focus loupe, centred on a point given relative to this
    /// zone and never straying outside it.
    /// </summary>
    /// <param name="centreX">0 to 1 across the zone, not the whole frame.</param>
    /// <param name="centreY">0 to 1 down the zone, not the whole frame.</param>
    /// <param name="widthFraction">Crop width as a fraction of the zone.</param>
    /// <param name="heightFraction">Crop height as a fraction of the zone.</param>
    /// <returns>The crop in whole-frame coordinates, ready to use as a viewbox.</returns>
    /// <remarks>
    /// Taking the centre relative to the zone is what makes the untouched default
    /// of dead centre land in the middle of the frame the camera is shooting,
    /// rather than the middle of the phone's screen - which for Expert RAW is
    /// somewhere in the control panel. Clamping to the zone stops the loupe
    /// sliding onto the chrome when the centre is pushed to an edge.
    /// </remarks>
    public RectD CropAround(
        double centreX,
        double centreY,
        double widthFraction,
        double heightFraction)
    {
        double cropWidth = Math.Clamp(widthFraction, 0, 1) * Width;
        double cropHeight = Math.Clamp(heightFraction, 0, 1) * Height;

        double x = Left + (Math.Clamp(centreX, 0, 1) * Width) - (cropWidth / 2);
        double y = Top + (Math.Clamp(centreY, 0, 1) * Height) - (cropHeight / 2);

        return new RectD(
            Math.Clamp(x, Left, Math.Max(Left, Right - cropWidth)),
            Math.Clamp(y, Top, Math.Max(Top, Bottom - cropHeight)),
            cropWidth,
            cropHeight);
    }

    /// <summary>
    /// Clamps to the preview and guarantees a usable area, so a bad stored value
    /// cannot collapse the guides to nothing or push them off the frame.
    /// </summary>
    public ShootingZone Normalised()
    {
        double left = Math.Clamp(Math.Min(Left, Right), 0, 1);
        double right = Math.Clamp(Math.Max(Left, Right), 0, 1);
        double top = Math.Clamp(Math.Min(Top, Bottom), 0, 1);
        double bottom = Math.Clamp(Math.Max(Top, Bottom), 0, 1);

        if (right - left < 0.05 || bottom - top < 0.05)
        {
            return FullScreen;
        }

        return new ShootingZone(left, top, right, bottom);
    }
}
