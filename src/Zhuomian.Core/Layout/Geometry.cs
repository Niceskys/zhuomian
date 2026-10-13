namespace Zhuomian.Core.Layout;

/// <summary>Engineering numeric envelope, not product size defaults. Units are logical DIPs.</summary>
internal static class GeometryGuard
{
    internal const double Limit = 1_000_000_000;
    internal const double MinimumExtent = 0.000001;
    internal static double Number(double value, string name, double minimum = -Limit, double maximum = Limit)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new ArgumentOutOfRangeException(name);
        }
        return value;
    }
    internal static void Area(LogicalRect rect)
    {
        ArgumentNullException.ThrowIfNull(rect);
        Number(rect.Width, nameof(rect.Width), MinimumExtent);
        Number(rect.Height, nameof(rect.Height), MinimumExtent);
    }
    internal static string MonitorId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 256 || value.Any(char.IsControl)) { throw new ArgumentException("Invalid monitor identity.", nameof(value)); }
        return value;
    }
}

public sealed record LogicalSize
{
    public LogicalSize(double width, double height)
    {
        Width = GeometryGuard.Number(width, nameof(width), GeometryGuard.MinimumExtent);
        Height = GeometryGuard.Number(height, nameof(height), GeometryGuard.MinimumExtent);
    }
    public double Width { get; }
    public double Height { get; }
}

/// <summary>Logical coordinates in one adapter-defined desktop coordinate system. Zero extent is an empty content area.</summary>
public sealed record LogicalRect
{
    public LogicalRect(double x, double y, double width, double height)
    {
        X = GeometryGuard.Number(x, nameof(x));
        Y = GeometryGuard.Number(y, nameof(y));
        Width = GeometryGuard.Number(width, nameof(width), 0);
        Height = GeometryGuard.Number(height, nameof(height), 0);
        GeometryGuard.Number(x + width, nameof(width));
        GeometryGuard.Number(y + height, nameof(height));
    }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public double Right => X + Width;
    public double Bottom => Y + Height;
    public bool Contains(LogicalRect other) => other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
}

/// <summary>Density metadata only; layout does not multiply absolute desktop coordinates by this factor.</summary>
public sealed record PixelDensity
{
    public PixelDensity(double pixelsPerDip)
    {
        PixelsPerDip = GeometryGuard.Number(pixelsPerDip, nameof(pixelsPerDip), 0.1, 16);
    }
    public double PixelsPerDip { get; }
    public LogicalSize FromPixelSize(double width, double height) => new(width / PixelsPerDip, height / PixelsPerDip);
}

public sealed record MonitorWorkArea
{
    public MonitorWorkArea(string monitorId, LogicalRect bounds, PixelDensity density)
    {
        MonitorId = GeometryGuard.MonitorId(monitorId);
        GeometryGuard.Area(bounds);
        ArgumentNullException.ThrowIfNull(density);
        Bounds = bounds;
        Density = density;
    }
    public string MonitorId { get; }
    public LogicalRect Bounds { get; }
    public PixelDensity Density { get; }
}

public sealed record ContentInsets
{
    public ContentInsets(double left, double top, double right, double bottom)
    {
        Left = GeometryGuard.Number(left, nameof(left), 0);
        Top = GeometryGuard.Number(top, nameof(top), 0);
        Right = GeometryGuard.Number(right, nameof(right), 0);
        Bottom = GeometryGuard.Number(bottom, nameof(bottom), 0);
    }
    public double Left { get; }
    public double Top { get; }
    public double Right { get; }
    public double Bottom { get; }
    public LogicalRect Apply(LogicalRect bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        return new(bounds.X + Math.Min(Left, bounds.Width), bounds.Y + Math.Min(Top, bounds.Height),
            Math.Max(0, bounds.Width - Left - Right), Math.Max(0, bounds.Height - Top - Bottom));
    }
}
