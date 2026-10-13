namespace Zhuomian.Core.Layout;

/// <summary>Typed user placement candidate only, not a frozen disk schema. Never capture animation bounds here.</summary>
public sealed record UserPlacement
{
    public UserPlacement(string monitorId, double normalizedX, double normalizedY, LogicalSize size)
    {
        MonitorId = GeometryGuard.MonitorId(monitorId);
        NormalizedX = GeometryGuard.Number(normalizedX, nameof(normalizedX), 0, 1);
        NormalizedY = GeometryGuard.Number(normalizedY, nameof(normalizedY), 0, 1);
        ArgumentNullException.ThrowIfNull(size);
        Size = size;
    }
    public string MonitorId { get; }
    public double NormalizedX { get; }
    public double NormalizedY { get; }
    public LogicalSize Size { get; }
}

public sealed record PlacementResolution(UserPlacement UserPlacement, MonitorWorkArea Monitor,
    LogicalRect IdleBounds, bool UsedPrimaryFallback);

public static class PlacementRecovery
{
    /// <summary>Call only for an explicitly accepted edit. Top-left is normalized against full work-area extent.</summary>
    public static UserPlacement CaptureUserEdit(LogicalRect editedBounds, MonitorWorkArea monitor)
    {
        GeometryGuard.Area(editedBounds);
        ArgumentNullException.ThrowIfNull(monitor);
        if (!monitor.Bounds.Contains(editedBounds)) { throw new ArgumentException("Edit must be inside work area.", nameof(editedBounds)); }
        return new(monitor.MonitorId, (editedBounds.X - monitor.Bounds.X) / monitor.Bounds.Width,
            (editedBounds.Y - monitor.Bounds.Y) / monitor.Bounds.Height, new(editedBounds.Width, editedBounds.Height));
    }

    /// <summary>Caller supplies a matching monitor or null plus primary. No OS lookup, automatic save or DPI rescaling.</summary>
    public static PlacementResolution Resolve(UserPlacement placement, MonitorWorkArea? matchedMonitor, MonitorWorkArea primary)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(primary);
        if (matchedMonitor is not null && matchedMonitor.MonitorId != placement.MonitorId)
        {
            throw new ArgumentException("Monitor identity mismatch.", nameof(matchedMonitor));
        }
        var monitor = matchedMonitor ?? primary;
        var area = monitor.Bounds;
        // Clamp size/position before creating a rectangle, avoiding overflow for a normalized edge at a large origin.
        var size = SpaceLayoutEngine.Fit(new(0, 0, placement.Size.Width, placement.Size.Height), new(0, 0, area.Width, area.Height));
        double x = area.X + Math.Min(placement.NormalizedX * area.Width, area.Width - size.Width);
        double y = area.Y + Math.Min(placement.NormalizedY * area.Height, area.Height - size.Height);
        var bounds = new LogicalRect(x, y, Math.Min(size.Width, area.Right - x), Math.Min(size.Height, area.Bottom - y));
        return new(placement, monitor, bounds, matchedMonitor is null);
    }
}
