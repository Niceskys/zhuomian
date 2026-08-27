namespace Zhuomian.Spike.MultiMonitorDpi;

internal sealed record LogicalPlacement(
    string MonitorKey,
    double NormalizedX,
    double NormalizedY,
    double WidthDip,
    double HeightDip);

internal sealed record MonitorGeometry(
    string Key,
    NativeMethods.Rect WorkArea,
    uint Dpi,
    bool IsPrimary);

internal sealed record MappedPlacement(
    string MonitorKey,
    NativeMethods.Rect PhysicalBounds,
    bool MigratedToPrimary);

internal static class PlacementMapper
{
    private const double BaseDpi = 96.0;

    public static MappedPlacement Map(
        LogicalPlacement placement,
        IReadOnlyList<MonitorGeometry> monitors)
    {
        ArgumentNullException.ThrowIfNull(placement);
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        var monitor = monitors.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, placement.MonitorKey, StringComparison.Ordinal));
        var migrated = monitor is null;
        monitor ??= monitors.FirstOrDefault(candidate => candidate.IsPrimary) ?? monitors[0];

        var width = Math.Clamp(
            (int)Math.Round(placement.WidthDip * monitor.Dpi / BaseDpi),
            1,
            monitor.WorkArea.Width);
        var height = Math.Clamp(
            (int)Math.Round(placement.HeightDip * monitor.Dpi / BaseDpi),
            1,
            monitor.WorkArea.Height);
        var availableX = monitor.WorkArea.Width - width;
        var availableY = monitor.WorkArea.Height - height;
        var normalizedX = Math.Clamp(placement.NormalizedX, 0, 1);
        var normalizedY = Math.Clamp(placement.NormalizedY, 0, 1);
        var left = monitor.WorkArea.Left + (int)Math.Round(availableX * normalizedX);
        var top = monitor.WorkArea.Top + (int)Math.Round(availableY * normalizedY);

        return new MappedPlacement(
            monitor.Key,
            new NativeMethods.Rect
            {
                Left = left,
                Top = top,
                Right = left + width,
                Bottom = top + height,
            },
            migrated);
    }
}
