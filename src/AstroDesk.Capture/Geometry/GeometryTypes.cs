namespace AstroDesk.Capture.Geometry;

public readonly record struct PointD(double X, double Y);

public readonly record struct SizeD(double Width, double Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(PointD point) =>
        point.X >= X &&
        point.Y >= Y &&
        point.X <= Right &&
        point.Y <= Bottom;
}
