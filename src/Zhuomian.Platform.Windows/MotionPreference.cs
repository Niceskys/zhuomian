namespace Zhuomian.Platform.Windows;

/// <summary>Unknown system preference is a supported no-motion policy, never permission to animate.</summary>
internal static class MotionPreference
{
    internal static bool? Read(Func<bool?> query)
    {
        try { return query(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return null; }
    }
    internal static bool Allow(bool? clientAnimation, bool highContrast) => clientAnimation == true && !highContrast;
}
