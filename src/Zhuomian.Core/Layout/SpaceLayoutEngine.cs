using Zhuomian.Core.Interaction;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Layout;

/// <summary>Injected provisional calculation parameters; there are no product defaults.</summary>
public sealed record ExpansionConstraints
{
    public ExpansionConstraints(double scale, LogicalSize minimum, LogicalSize maximum)
    {
        Scale = GeometryGuard.Number(scale, nameof(scale), 1, 1000);
        ArgumentNullException.ThrowIfNull(minimum);
        ArgumentNullException.ThrowIfNull(maximum);
        if (minimum.Width > maximum.Width || minimum.Height > maximum.Height) { throw new ArgumentException("Inverted constraints."); }
        Minimum = minimum;
        Maximum = maximum;
    }
    public double Scale { get; }
    public LogicalSize Minimum { get; }
    public LogicalSize Maximum { get; }
}

public enum LayoutTransitionDirection { Expand, Return }

/// <summary>Runtime plan only: adapter owns identity, generation, hit testing, interpolation and completion.</summary>
public sealed record SpaceLayoutPlan(
    Guid SpaceId, string MonitorId, LogicalRect UserBounds, LogicalRect IdleBounds,
    LogicalRect ExpandedBounds, LogicalRect FromBounds, LogicalRect TargetBounds);

public static class SpaceLayoutEngine
{
    public static SpaceLayoutPlan Calculate(Space space, LogicalRect userBounds, MonitorWorkArea monitor,
        ExpansionConstraints constraints, LayoutTransitionDirection direction, LogicalRect? currentVisualBounds = null)
    {
        ArgumentNullException.ThrowIfNull(space);
        GeometryGuard.Area(userBounds);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(constraints);
        if (!Enum.IsDefined(direction)) { throw new ArgumentOutOfRangeException(nameof(direction)); }
        if (currentVisualBounds is not null) { GeometryGuard.Area(currentVisualBounds); }
        var area = monitor.Bounds;
        var idle = Fit(userBounds, area);
        double width = Math.Clamp(userBounds.Width * constraints.Scale, constraints.Minimum.Width, constraints.Maximum.Width);
        double height = Math.Clamp(userBounds.Height * constraints.Scale, constraints.Minimum.Height, constraints.Maximum.Height);
        var size = FitSize(width, height, area);
        // A keeps the original center wherever possible; B uses this monitor's work-area center.
        double centerX = space.ActivationPolicy.Mode == ActivationMode.Hover ? idle.X + idle.Width / 2 : area.X + area.Width / 2;
        double centerY = space.ActivationPolicy.Mode == ActivationMode.Hover ? idle.Y + idle.Height / 2 : area.Y + area.Height / 2;
        var expanded = Place(centerX - size.Width / 2, centerY - size.Height / 2, size.Width, size.Height, area);
        bool returning = direction == LayoutTransitionDirection.Return;
        return new(space.Id, monitor.MonitorId, userBounds, idle, expanded,
            currentVisualBounds ?? (returning ? expanded : idle), returning ? idle : expanded);
    }

    /// <summary>Uniform shrink then positional clamp. A smaller work area takes precedence over minimum size.</summary>
    public static LogicalRect Fit(LogicalRect bounds, LogicalRect workArea)
    {
        GeometryGuard.Area(bounds);
        GeometryGuard.Area(workArea);
        var size = FitSize(bounds.Width, bounds.Height, workArea);
        return Place(bounds.X, bounds.Y, size.Width, size.Height, workArea);
    }

    private static (double Width, double Height) FitSize(double width, double height, LogicalRect area)
    {
        double factor = Math.Min(1, Math.Min(area.Width / width, area.Height / height));
        // A thin aspect may fall below the accepted space extent: use a safe thin rectangle.
        return (Math.Min(area.Width, Math.Max(GeometryGuard.MinimumExtent, width * factor)),
            Math.Min(area.Height, Math.Max(GeometryGuard.MinimumExtent, height * factor)));
    }

    private static LogicalRect Place(double x, double y, double width, double height, LogicalRect area)
    {
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height));
        // Roundoff at a large origin must not put an endpoint beyond the work area.
        return new(x, y, Math.Min(width, area.Right - x), Math.Min(height, area.Bottom - y));
    }
}
