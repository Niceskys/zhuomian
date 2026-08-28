using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Zhuomian.Spike.ExplorerRecovery;

internal sealed class ExplorerRecoveryProbe
{
    private const int GwlExStyle = -20;
    private const ulong WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndBottom = 1;

    public static ExplorerRecoveryEvidence Run(string readyPath)
    {
        var runtimeState = new RecoveryRuntimeState();
        var initialShellWasAvailable = false;
        var initialHostWasCreated = false;
        var hostLostWasDetected = false;
        var initialHostWasDestroyedAfterLoss = false;
        var taskbarCreatedWasReceived = false;
        var newShellGenerationWasDetected = false;
        var newHostWasCreated = false;
        var newHostWasNoActivate = false;
        var newHostNeverBecameForeground = false;
        var recoveryMilliseconds = 0.0;
        var explorerWasAvailableAtEnd = false;
        var allProbeWindowsWereDestroyed = false;
        nint lifecycleHandle = 0;
        nint initialHostHandle = 0;
        nint newHostHandle = 0;

        using var lifecycleSink = new LifecycleSinkForm();
        VisualHostForm? initialHost = null;
        VisualHostForm? newHost = null;

        try
        {
            lifecycleHandle = lifecycleSink.Handle;
            initialShellWasAvailable = WaitForShell(
                previousProcessId: 0,
                TimeSpan.FromSeconds(5),
                out _,
                out var initialShellProcessId);

            var primaryScreen = Screen.PrimaryScreen;
            if (initialShellWasAvailable && primaryScreen is not null)
            {
                initialHost = CreateHost(primaryScreen.WorkingArea, 1);
                initialHostHandle = initialHost.Handle;
                initialHostWasCreated = NativeMethods.IsWindow(initialHostHandle);
            }

            if (initialShellWasAvailable && initialHostWasCreated)
            {
                WriteReadySignal(readyPath, initialShellProcessId);

                hostLostWasDetected = WaitUntil(
                    () => !TryGetShell(out _, out var currentProcessId) ||
                        currentProcessId != initialShellProcessId,
                    TimeSpan.FromSeconds(30));
            }

            var recoveryTimer = Stopwatch.StartNew();
            if (hostLostWasDetected)
            {
                runtimeState.OnHostLost();
                initialHost?.Close();
                Pump(TimeSpan.FromMilliseconds(50));
                initialHostWasDestroyedAfterLoss = !NativeMethods.IsWindow(initialHostHandle);

                newShellGenerationWasDetected = WaitForShell(
                    initialShellProcessId,
                    TimeSpan.FromSeconds(15),
                    out _,
                    out _);

                if (newShellGenerationWasDetected)
                {
                    Pump(TimeSpan.FromMilliseconds(250));
                    taskbarCreatedWasReceived = lifecycleSink.TaskbarCreatedReceived;

                    primaryScreen = Screen.PrimaryScreen;
                    if (primaryScreen is not null)
                    {
                        newHost = CreateHost(primaryScreen.WorkingArea, 2);
                        newHostHandle = newHost.Handle;
                        newHostWasCreated = NativeMethods.IsWindow(newHostHandle);
                        newHostWasNoActivate = HasNoActivateStyle(newHostHandle);
                        newHostNeverBecameForeground =
                            NativeMethods.GetForegroundWindow() != newHostHandle;
                        runtimeState.OnHostRestored();
                    }
                }
            }

            recoveryTimer.Stop();
            recoveryMilliseconds = hostLostWasDetected
                ? recoveryTimer.Elapsed.TotalMilliseconds
                : 0;
            explorerWasAvailableAtEnd = TryGetShell(out _, out _);
        }
        finally
        {
            if (newHost is not null)
            {
                newHost.Close();
                newHost.Dispose();
            }

            if (initialHost is not null)
            {
                initialHost.Close();
                initialHost.Dispose();
            }

            lifecycleSink.Close();
            Pump(TimeSpan.FromMilliseconds(100));
            allProbeWindowsWereDestroyed =
                (initialHostHandle == 0 || !NativeMethods.IsWindow(initialHostHandle)) &&
                (newHostHandle == 0 || !NativeMethods.IsWindow(newHostHandle)) &&
                (lifecycleHandle == 0 || !NativeMethods.IsWindow(lifecycleHandle));
        }

        var recoveryWithinFiveSeconds =
            newHostWasCreated && recoveryMilliseconds <= 5000;
        var finalStateWasSafeIdle =
            runtimeState.DesktopAvailability == "DesktopAvailable" &&
            runtimeState.IntentGate == "Disarmed" &&
            runtimeState.KeyboardMode == "NoKeyboardCapture" &&
            runtimeState.SpaceState == "Idle";
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["initial-shell-available"] = initialShellWasAvailable,
            ["initial-host-created"] = initialHostWasCreated,
            ["host-lost-detected"] = hostLostWasDetected,
            ["initial-host-destroyed-after-loss"] = initialHostWasDestroyedAfterLoss,
            ["new-shell-generation-detected"] = newShellGenerationWasDetected,
            ["new-host-created"] = newHostWasCreated,
            ["new-host-noactivate"] = newHostWasNoActivate,
            ["new-host-never-foreground"] = newHostNeverBecameForeground,
            ["recovery-within-five-seconds"] = recoveryWithinFiveSeconds,
            ["input-capture-released"] = runtimeState.InputCaptureReleased,
            ["animation-callbacks-stopped"] = runtimeState.AnimationCallbacksStopped,
            ["media-resources-released"] = runtimeState.MediaResourcesReleased,
            ["final-state-safe-idle"] = finalStateWasSafeIdle,
            ["explorer-available-at-end"] = explorerWasAvailableAtEnd,
            ["all-probe-windows-destroyed"] = allProbeWindowsWereDestroyed,
        };

        return new ExplorerRecoveryEvidence(
            SchemaVersion: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.VersionString,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            InitialShellWasAvailable: initialShellWasAvailable,
            InitialHostWasCreated: initialHostWasCreated,
            HostLostWasDetected: hostLostWasDetected,
            InitialHostWasDestroyedAfterLoss: initialHostWasDestroyedAfterLoss,
            TaskbarCreatedWasReceived: taskbarCreatedWasReceived,
            NewShellGenerationWasDetected: newShellGenerationWasDetected,
            NewHostWasCreated: newHostWasCreated,
            NewHostWasNoActivate: newHostWasNoActivate,
            NewHostNeverBecameForeground: newHostNeverBecameForeground,
            RecoveryMilliseconds: recoveryMilliseconds,
            RecoveryWasWithinFiveSeconds: recoveryWithinFiveSeconds,
            InputCaptureWasReleased: runtimeState.InputCaptureReleased,
            AnimationCallbacksWereStopped: runtimeState.AnimationCallbacksStopped,
            MediaResourcesWereReleased: runtimeState.MediaResourcesReleased,
            FinalDesktopAvailability: runtimeState.DesktopAvailability,
            FinalIntentGate: runtimeState.IntentGate,
            FinalKeyboardMode: runtimeState.KeyboardMode,
            FinalSpaceState: runtimeState.SpaceState,
            FinalStateWasSafeIdle: finalStateWasSafeIdle,
            ExplorerWasAvailableAtEnd: explorerWasAvailableAtEnd,
            AllProbeWindowsWereDestroyed: allProbeWindowsWereDestroyed,
            Limitations:
            [
                "The public fallback host is proactively invalidated when the Shell process generation changes.",
                "TaskbarCreated is recorded as advisory evidence and is not an authoritative recovery gate.",
                "This probe models input, animation, and media shutdown flags; production resource owners do not exist yet.",
                "Enhanced WorkerW parent invalidation, graceful Shell exit, and long-running soak remain separate validation work.",
            ],
            FailedChecks: checks.Where(check => !check.Value).Select(check => check.Key).ToArray());
    }

    private static VisualHostForm CreateHost(Rectangle workArea, int generation)
    {
        var host = new VisualHostForm(workArea, generation);
        var handle = host.Handle;
        host.Show();
        NativeMethods.SetWindowPos(
            handle,
            HwndBottom,
            host.Bounds.X,
            host.Bounds.Y,
            host.Bounds.Width,
            host.Bounds.Height,
            SwpNoActivate | SwpShowWindow);
        Pump(TimeSpan.FromMilliseconds(100));
        return host;
    }

    private static bool HasNoActivateStyle(nint window)
    {
        var style = unchecked(
            (ulong)NativeMethods.GetWindowLongPtrW(window, GwlExStyle).ToInt64());
        return (style & WsExNoActivate) != 0;
    }

    private static bool WaitForShell(
        uint previousProcessId,
        TimeSpan timeout,
        out nint shellWindow,
        out uint processId)
    {
        shellWindow = 0;
        processId = 0;
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            Application.DoEvents();
            if (TryGetShell(out var currentWindow, out var currentProcessId) &&
                currentProcessId != previousProcessId)
            {
                shellWindow = currentWindow;
                processId = currentProcessId;
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    private static bool TryGetShell(out nint shellWindow, out uint processId)
    {
        shellWindow = NativeMethods.GetShellWindow();
        processId = 0;
        return shellWindow != 0 &&
            NativeMethods.GetWindowThreadProcessId(shellWindow, out processId) != 0 &&
            processId != 0;
    }

    private static void WriteReadySignal(string readyPath, uint shellProcessId)
    {
        var fullPath = Path.GetFullPath(readyPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var signal = new ReadySignal(true, shellProcessId);
        var json = JsonSerializer.Serialize(signal, ExplorerRecoveryJsonContext.Default.ReadySignal);
        File.WriteAllText(fullPath, json + Environment.NewLine);
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            Application.DoEvents();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    private static void Pump(TimeSpan duration)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < duration)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }
}
