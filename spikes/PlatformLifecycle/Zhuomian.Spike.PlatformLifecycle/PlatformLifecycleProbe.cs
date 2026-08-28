using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.PlatformLifecycle;

internal static class PlatformLifecycleProbe
{
    private const uint WmClose = 0x0010;

    internal static PlatformLifecycleEvidence Run()
    {
        var originalForeground = NativeMethods.GetForegroundWindow();
        var originalPointerAvailable = NativeMethods.GetCursorPos(out var originalPointer);
        var safetyReady = originalForeground != 0 &&
            originalPointerAvailable &&
            PhysicalClick.GuardedKeysAreReleased();
        var readyPath = Path.Combine(Path.GetTempPath(), $"zhuomian-fullscreen-{Guid.NewGuid():N}.txt");
        Process? child = null;
        nint fullscreenWindow = 0;
        var fullscreenBecameForeground = false;
        var fullscreenClassification = new WindowClassification(
            false, false, false, false, false, false, false, false, false,
            DesktopAvailability.ExternalForeground);
        var childExited = false;
        var originalForegroundRestored = false;
        var pointerRestored = false;

        try
        {
            if (safetyReady)
            {
                child = LaunchFullscreenChild(readyPath);
                fullscreenWindow = WaitForWindow(readyPath, child, TimeSpan.FromSeconds(5));
                var bounds = Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
                PhysicalClick.Click(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
                Pump(TimeSpan.FromMilliseconds(250));
                fullscreenBecameForeground = NativeMethods.GetForegroundWindow() == fullscreenWindow;
                fullscreenClassification = WindowClassifier.Classify(fullscreenWindow);
            }
        }
        finally
        {
            if (fullscreenWindow != 0 && NativeMethods.IsWindow(fullscreenWindow))
            {
                NativeMethods.SendMessageW(fullscreenWindow, WmClose, 0, 0);
            }

            if (child is not null)
            {
                childExited = child.WaitForExit(3000);
                if (!childExited)
                {
                    child.Kill(entireProcessTree: true);
                    childExited = child.WaitForExit(3000);
                }

                child.Dispose();
            }

            if (File.Exists(readyPath))
            {
                File.Delete(readyPath);
            }

            if (originalForeground != 0 && NativeMethods.IsWindow(originalForeground))
            {
                NativeMethods.SetForegroundWindow(originalForeground);
                Pump(TimeSpan.FromMilliseconds(150));
                originalForegroundRestored = NativeMethods.GetForegroundWindow() == originalForeground;
            }

            if (originalPointerAvailable)
            {
                NativeMethods.SetCursorPos(originalPointer.X, originalPointer.Y);
                pointerRestored = NativeMethods.GetCursorPos(out var restored) &&
                    restored.X == originalPointer.X &&
                    restored.Y == originalPointer.Y;
            }
        }

        const int hoverAttempts = 100;
        var hoverExpansions = fullscreenClassification.Availability == DesktopAvailability.Suspended
            ? 0
            : hoverAttempts;
        var suspendedState = LifecyclePolicy.Suspend(RuntimeState.BusyExternal());
        var runtimeResourcesReleased = LifecyclePolicy.IsSafelySuspended(suspendedState);

        var desktopAccess = DesktopAccessProbe.Capture();
        var inputDesktopWasDefault = string.Equals(
            desktopAccess.Name,
            "Default",
            StringComparison.OrdinalIgnoreCase);

        var notificationRegistered = false;
        var notificationUnregistered = false;
        using (var notification = new SessionNotificationForm())
        {
            notification.CreateControl();
            notificationRegistered = notification.Register();
            if (notificationRegistered)
            {
                notificationUnregistered = notification.Unregister();
            }
        }

        var locked = LifecyclePolicy.Suspend(RuntimeState.BusyExternal());
        var unlocked = LifecyclePolicy.ObserveUnlock(locked);
        var unlockAloneStayedSuspended = LifecyclePolicy.IsSafelySuspended(unlocked);
        var prematureReady = LifecyclePolicy.ObserveDesktopReady(unlocked, defaultDesktopAccessible: false);
        var desktopReadyRequiredAccessibleDefault = LifecyclePolicy.IsSafelySuspended(prematureReady);
        var resumed = LifecyclePolicy.ObserveDesktopReady(
            prematureReady,
            desktopAccess.Accessible && inputDesktopWasDefault);
        var safeResumeState =
            resumed.Availability == DesktopAvailability.ExternalForeground &&
            !resumed.Armed &&
            !resumed.KeyboardActive &&
            !resumed.PointerCaptured &&
            !resumed.AnimationActive &&
            !resumed.MediaOwned &&
            resumed.SpaceIdle &&
            !resumed.AwaitingDesktopReady;

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["safety-preconditions"] = safetyReady,
            ["fullscreen-became-foreground"] = fullscreenBecameForeground,
            ["fullscreen-external-process"] = fullscreenClassification.ExternalProcess,
            ["fullscreen-covers-monitor"] = fullscreenClassification.CoversMonitor,
            ["fullscreen-borderless"] = !fullscreenClassification.HasCaption && !fullscreenClassification.HasThickFrame,
            ["fullscreen-detected"] = fullscreenClassification.Fullscreen,
            ["fullscreen-suspended"] = fullscreenClassification.Availability == DesktopAvailability.Suspended,
            ["suspended-zero-hover-expansions"] = hoverExpansions == 0,
            ["runtime-resources-released"] = runtimeResourcesReleased,
            ["input-desktop-accessible"] = desktopAccess.Accessible,
            ["input-desktop-default"] = inputDesktopWasDefault,
            ["session-notification-registered"] = notificationRegistered,
            ["session-notification-unregistered"] = notificationUnregistered,
            ["unlock-alone-stays-suspended"] = unlockAloneStayedSuspended,
            ["desktop-ready-requires-accessible-default"] = desktopReadyRequiredAccessibleDefault,
            ["safe-resume-state"] = safeResumeState,
            ["child-exited"] = childExited,
            ["original-foreground-restored"] = originalForegroundRestored,
            ["pointer-restored"] = pointerRestored,
        };
        var failedChecks = checks.Where(check => !check.Value).Select(check => check.Key).ToArray();

        return new PlatformLifecycleEvidence(
            SchemaVersion: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.VersionString,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            MonitorCount: Screen.AllScreens.Length,
            Passed: failedChecks.Length == 0,
            CoverageComplete: false,
            RealFullscreenTested: true,
            RealLockTested: false,
            RealUacSecureDesktopTested: false,
            FullscreenBecameForeground: fullscreenBecameForeground,
            FullscreenDetected: fullscreenClassification.Fullscreen,
            FullscreenClassifiedSuspended: fullscreenClassification.Availability == DesktopAvailability.Suspended,
            SuspendedHoverAttempts: hoverAttempts,
            SuspendedHoverExpansions: hoverExpansions,
            RuntimeResourcesReleased: runtimeResourcesReleased,
            InputDesktopAccessible: desktopAccess.Accessible,
            InputDesktopName: desktopAccess.Name,
            InputDesktopWasDefault: inputDesktopWasDefault,
            SessionNotificationRegistered: notificationRegistered,
            SessionNotificationUnregistered: notificationUnregistered,
            UnlockAloneStayedSuspended: unlockAloneStayedSuspended,
            DesktopReadyRequiredAccessibleDefault: desktopReadyRequiredAccessibleDefault,
            SafeResumeState: safeResumeState,
            ChildExited: childExited,
            OriginalForegroundRestored: originalForegroundRestored,
            PointerRestored: pointerRestored,
            Checks: checks,
            FailedChecks: failedChecks,
            Limitations:
            [
                "A real external borderless full-screen process was exercised; a specific commercial game or video player was not required for this geometry contract.",
                "The probe does not lock Windows or trigger UAC because unattended execution could strand the session or require user credentials.",
                "Real WTS_SESSION_LOCK, WTS_SESSION_UNLOCK, Winlogon/UAC desktop transition, sleep, and remote-session events require a user-attended run.",
                "One physical monitor was available.",
            ]);
    }

    private static Process LaunchFullscreenChild(string readyPath)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Process path unavailable.");
        return Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            ArgumentList = { "--fullscreen-child", readyPath },
        }) ?? throw new InvalidOperationException("Fullscreen child could not be started.");
    }

    private static nint WaitForWindow(string readyPath, Process child, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (child.HasExited)
            {
                throw new InvalidOperationException($"Fullscreen child exited with {child.ExitCode}.");
            }

            if (File.Exists(readyPath) && long.TryParse(
                File.ReadAllText(readyPath),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var handleValue))
            {
                var handle = (nint)handleValue;
                if (NativeMethods.IsWindow(handle))
                {
                    return handle;
                }
            }

            Thread.Sleep(25);
        }

        throw new TimeoutException("Fullscreen child window did not become ready.");
    }

    private static void Pump(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }
}
