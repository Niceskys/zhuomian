namespace Zhuomian.Spike.ForegroundClassification;

internal enum ForegroundKind
{
    None,
    Zhuomian,
    ShellDesktop,
    ExternalWindow,
    ExternalFullscreen,
    UnavailableSession,
}

internal enum DesktopAvailability
{
    DesktopAvailable,
    ExternalForeground,
    Suspended,
}

internal enum DesktopIntentGate
{
    Disarmed,
    Armed,
}

internal enum ActivationMode
{
    Hover,
    Click,
}

internal sealed record WindowSignals(
    bool SessionInteractive,
    bool WindowExists,
    bool ProcessIdAvailable,
    uint WindowProcessId,
    uint CurrentProcessId,
    bool ExactShellWindow,
    bool Visible,
    bool Cloaked,
    bool Minimized,
    bool Maximized,
    bool HasCaption,
    bool HasThickFrame,
    NativeMethods.Rect WindowBounds,
    NativeMethods.Rect MonitorBounds);

internal sealed record ClassificationResult(
    ForegroundKind Kind,
    DesktopAvailability Availability,
    bool FullscreenDetected,
    bool FailSafeApplied);

internal static class ForegroundClassifier
{
    private const int GeometryTolerance = 2;

    public static ClassificationResult Classify(WindowSignals signals)
    {
        ArgumentNullException.ThrowIfNull(signals);

        if (!signals.SessionInteractive)
        {
            return new(
                ForegroundKind.UnavailableSession,
                DesktopAvailability.Suspended,
                false,
                true);
        }

        if (!signals.WindowExists)
        {
            return new(ForegroundKind.None, DesktopAvailability.Suspended, false, true);
        }

        if (signals.ProcessIdAvailable && signals.WindowProcessId == signals.CurrentProcessId)
        {
            return new(
                ForegroundKind.Zhuomian,
                DesktopAvailability.DesktopAvailable,
                false,
                false);
        }

        if (signals.ExactShellWindow)
        {
            return new(
                ForegroundKind.ShellDesktop,
                DesktopAvailability.DesktopAvailable,
                false,
                false);
        }

        var coversMonitor =
            signals.WindowBounds.Left <= signals.MonitorBounds.Left + GeometryTolerance &&
            signals.WindowBounds.Top <= signals.MonitorBounds.Top + GeometryTolerance &&
            signals.WindowBounds.Right >= signals.MonitorBounds.Right - GeometryTolerance &&
            signals.WindowBounds.Bottom >= signals.MonitorBounds.Bottom - GeometryTolerance;
        var standardMaximizedWindow =
            signals.Maximized && (signals.HasCaption || signals.HasThickFrame);
        var fullscreen =
            signals.Visible &&
            !signals.Cloaked &&
            !signals.Minimized &&
            coversMonitor &&
            !standardMaximizedWindow;

        if (fullscreen)
        {
            return new(
                ForegroundKind.ExternalFullscreen,
                DesktopAvailability.Suspended,
                true,
                !signals.ProcessIdAvailable);
        }

        return new(
            ForegroundKind.ExternalWindow,
            DesktopAvailability.ExternalForeground,
            false,
            !signals.ProcessIdAvailable);
    }
}

internal static class HoverGate
{
    public static bool CanEnterIntent(
        ActivationMode activationMode,
        DesktopAvailability availability,
        DesktopIntentGate intentGate,
        bool higherPriorityEventPending) =>
        activationMode == ActivationMode.Hover &&
        availability != DesktopAvailability.Suspended &&
        (availability == DesktopAvailability.DesktopAvailable ||
            (availability == DesktopAvailability.ExternalForeground &&
                intentGate == DesktopIntentGate.Armed)) &&
        !higherPriorityEventPending;
}
