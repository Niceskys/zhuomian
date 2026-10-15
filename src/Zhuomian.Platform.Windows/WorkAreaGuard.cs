namespace Zhuomian.Platform.Windows;

internal readonly record struct WorkAreaSnapshot(int Left, int Top, int Right, int Bottom)
{
    internal bool IsValid => Right > Left && Bottom > Top;
}

internal enum WorkAreaStatus { Unchanged, Changed, Unavailable }

/// <summary>Event-driven snapshot validation. Never updates the accepted coordinate system in place.</summary>
internal sealed class WorkAreaGuard(WorkAreaSnapshot initial, Func<WorkAreaSnapshot?> query)
{
    internal static WorkAreaSnapshot? Read(Func<WorkAreaSnapshot?> query)
    {
        try { return query() is { IsValid: true } value ? value : null; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }

    internal WorkAreaStatus Check() => Read(query) is not { } current ? WorkAreaStatus.Unavailable :
        current == initial ? WorkAreaStatus.Unchanged : WorkAreaStatus.Changed;
}
