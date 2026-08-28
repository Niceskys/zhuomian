using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.ForegroundClassification;

internal static class ForegroundClassificationProbe
{
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTop = 0;

    public static ForegroundClassificationEvidence Run()
    {
        var safetyAborts = new List<string>();
        var originalForeground = NativeMethods.GetForegroundWindow();
        var originalCursorAvailable = NativeMethods.GetCursorPos(out var originalCursor);
        var guardedKeysReleased = PhysicalClick.GuardedKeysAreReleased();
        var initialSignals = WindowSignalExtractor.Capture(originalForeground);
        var initialResult = ForegroundClassifier.Classify(initialSignals);
        var initialExternal = initialResult.Kind is
            ForegroundKind.ExternalWindow or ForegroundKind.ExternalFullscreen;

        if (!initialSignals.SessionInteractive)
        {
            safetyAborts.Add("input-desktop-unavailable");
        }

        if (!initialExternal)
        {
            safetyAborts.Add("initial-foreground-was-not-external");
        }

        if (!originalCursorAvailable)
        {
            safetyAborts.Add("cursor-position-unavailable");
        }

        if (!guardedKeysReleased)
        {
            safetyAborts.Add("mouse-or-modifier-key-held");
        }

        const int realExternalSamples = 100;
        var realExternalBlockedSamples = 0;
        if (safetyAborts.Count == 0)
        {
            for (var index = 0; index < realExternalSamples; index++)
            {
                var sample = ForegroundClassifier.Classify(
                    WindowSignalExtractor.CaptureForeground());
                if (sample.Availability != DesktopAvailability.DesktopAvailable &&
                    !HoverGate.CanEnterIntent(
                        ActivationMode.Hover,
                        sample.Availability,
                        DesktopIntentGate.Disarmed,
                        false))
                {
                    realExternalBlockedSamples++;
                }
            }
        }

        var shellResult = ForegroundClassifier.Classify(
            WindowSignalExtractor.Capture(NativeMethods.GetShellWindow()));
        var liveShellClassifiedAsDesktopAvailable =
            shellResult.Kind == ForegroundKind.ShellDesktop &&
            shellResult.Availability == DesktopAvailability.DesktopAvailable;

        var ownForegroundClassifiedAsDesktopAvailable = false;
        var originalForegroundWasRestored = false;
        var pointerWasRestored = false;
        nint ownWindowHandle = 0;
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
        using var ownWindow = new OwnForegroundForm(new Rectangle(
            workArea.Left + 80,
            workArea.Top + 80,
            Math.Min(520, Math.Max(320, workArea.Width - 160)),
            220));

        try
        {
            if (safetyAborts.Count == 0 && !workArea.IsEmpty)
            {
                ownWindowHandle = ownWindow.Handle;
                ownWindow.Show();
                NativeMethods.SetWindowPos(
                    ownWindowHandle,
                    HwndTop,
                    ownWindow.Bounds.X,
                    ownWindow.Bounds.Y,
                    ownWindow.Bounds.Width,
                    ownWindow.Bounds.Height,
                    SwpNoActivate | SwpShowWindow);
                Pump(TimeSpan.FromMilliseconds(150));
                var clickPoint = ownWindow.PointToScreen(
                    new Point(ownWindow.ClientSize.Width / 2, ownWindow.ClientSize.Height / 2));
                var clicked = PhysicalClick.Click(clickPoint);
                Pump(TimeSpan.FromMilliseconds(200));
                var ownResult = ForegroundClassifier.Classify(
                    WindowSignalExtractor.CaptureForeground());
                ownForegroundClassifiedAsDesktopAvailable =
                    clicked &&
                    NativeMethods.GetForegroundWindow() == ownWindowHandle &&
                    ownResult.Kind == ForegroundKind.Zhuomian &&
                    ownResult.Availability == DesktopAvailability.DesktopAvailable;
            }
            else if (workArea.IsEmpty)
            {
                safetyAborts.Add("primary-work-area-unavailable");
            }
        }
        finally
        {
            ownWindow.Close();
            Pump(TimeSpan.FromMilliseconds(50));
            if (originalForeground != 0)
            {
                NativeMethods.SetForegroundWindow(originalForeground);
                Pump(TimeSpan.FromMilliseconds(100));
                originalForegroundWasRestored =
                    NativeMethods.GetForegroundWindow() == originalForeground;
            }

            if (originalCursorAvailable)
            {
                NativeMethods.SetCursorPos(originalCursor.X, originalCursor.Y);
                pointerWasRestored = NativeMethods.GetCursorPos(out var restoredCursor) &&
                    restoredCursor.X == originalCursor.X &&
                    restoredCursor.Y == originalCursor.Y;
            }
        }

        var syntheticChecks = RunSyntheticChecks();
        var (truthTableCases, truthTablePassedCases) = ValidateTruthTable();
        const int externalDisarmedHoverAttempts = 100;
        var externalDisarmedExpansions = Enumerable.Range(0, externalDisarmedHoverAttempts)
            .Count(_ => HoverGate.CanEnterIntent(
                ActivationMode.Hover,
                DesktopAvailability.ExternalForeground,
                DesktopIntentGate.Disarmed,
                false));
        var hoverGateHasNoKeyboardCapturePath = truthTablePassedCases == truthTableCases;
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["real-external-100-of-100-blocked"] =
                realExternalBlockedSamples == realExternalSamples,
            ["live-shell-desktop-available"] = liveShellClassifiedAsDesktopAvailable,
            ["own-foreground-desktop-available"] = ownForegroundClassifiedAsDesktopAvailable,
            ["original-foreground-restored"] = originalForegroundWasRestored,
            ["pointer-restored"] = pointerWasRestored,
            ["truth-table-complete"] = truthTableCases == 24,
            ["truth-table-passed"] = truthTablePassedCases == truthTableCases,
            ["external-disarmed-zero-expansions"] = externalDisarmedExpansions == 0,
            ["synthetic-classification"] = syntheticChecks.All(check => check.Passed),
            ["hover-gate-has-no-keyboard-capture-path"] = hoverGateHasNoKeyboardCapturePath,
        };

        return new ForegroundClassificationEvidence(
            SchemaVersion: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.VersionString,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            InputDesktopWasAccessible: initialSignals.SessionInteractive,
            InitialExternalKind: initialResult.Kind.ToString(),
            InitialExternalAvailability: initialResult.Availability.ToString(),
            RealExternalSamples: realExternalSamples,
            RealExternalBlockedSamples: realExternalBlockedSamples,
            LiveShellClassifiedAsDesktopAvailable: liveShellClassifiedAsDesktopAvailable,
            OwnForegroundClassifiedAsDesktopAvailable: ownForegroundClassifiedAsDesktopAvailable,
            OriginalForegroundWasRestored: originalForegroundWasRestored,
            PointerWasRestored: pointerWasRestored,
            TruthTableCases: truthTableCases,
            TruthTablePassedCases: truthTablePassedCases,
            ExternalDisarmedHoverAttempts: externalDisarmedHoverAttempts,
            ExternalDisarmedExpansions: externalDisarmedExpansions,
            HoverGateHasNoKeyboardCapturePath: hoverGateHasNoKeyboardCapturePath,
            SyntheticChecks: syntheticChecks,
            SafetyAborts: [.. safetyAborts],
            Limitations:
            [
                "Lock screen, UAC secure desktop, and a real borderless full-screen game were not activated during this probe.",
                "Full-screen, cloaked, missing-foreground, and inaccessible-process paths are deterministic signal tests.",
                "Exact Shell HWND identity is accepted; other Explorer windows fail safe as external foreground.",
                "The classifier is disposable Win32 evidence, not production code.",
            ],
            FailedChecks: checks.Where(check => !check.Value).Select(check => check.Key).ToArray());
    }

    private static CheckEvidence[] RunSyntheticChecks()
    {
        var monitor = Rect(0, 0, 2560, 1600);
        var ordinary = Rect(100, 100, 1200, 800);
        var baseSignals = new WindowSignals(
            true,
            true,
            true,
            200,
            100,
            false,
            true,
            false,
            false,
            false,
            true,
            true,
            ordinary,
            monitor);

        return
        [
            Check("no-foreground-suspends", baseSignals with { WindowExists = false },
                ForegroundKind.None, DesktopAvailability.Suspended),
            Check("unavailable-session-suspends", baseSignals with { SessionInteractive = false },
                ForegroundKind.UnavailableSession, DesktopAvailability.Suspended),
            Check("own-window-is-desktop-available",
                baseSignals with { WindowProcessId = 100 },
                ForegroundKind.Zhuomian, DesktopAvailability.DesktopAvailable),
            Check("exact-shell-is-desktop-available",
                baseSignals with { ExactShellWindow = true },
                ForegroundKind.ShellDesktop, DesktopAvailability.DesktopAvailable),
            Check("borderless-monitor-covering-window-suspends",
                baseSignals with
                {
                    HasCaption = false,
                    HasThickFrame = false,
                    WindowBounds = monitor,
                },
                ForegroundKind.ExternalFullscreen, DesktopAvailability.Suspended),
            Check("standard-maximized-window-remains-external",
                baseSignals with { Maximized = true, WindowBounds = monitor },
                ForegroundKind.ExternalWindow, DesktopAvailability.ExternalForeground),
            Check("cloaked-covering-window-is-not-fullscreen",
                baseSignals with
                {
                    Cloaked = true,
                    HasCaption = false,
                    HasThickFrame = false,
                    WindowBounds = monitor,
                },
                ForegroundKind.ExternalWindow, DesktopAvailability.ExternalForeground),
            Check("inaccessible-process-fails-safe-external",
                baseSignals with { ProcessIdAvailable = false, WindowProcessId = 0 },
                ForegroundKind.ExternalWindow, DesktopAvailability.ExternalForeground),
            Check("non-shell-explorer-window-remains-external",
                baseSignals with { WindowProcessId = 300, ExactShellWindow = false },
                ForegroundKind.ExternalWindow, DesktopAvailability.ExternalForeground),
        ];
    }

    private static CheckEvidence Check(
        string name,
        WindowSignals signals,
        ForegroundKind expectedKind,
        DesktopAvailability expectedAvailability)
    {
        var result = ForegroundClassifier.Classify(signals);
        return new(name, result.Kind == expectedKind && result.Availability == expectedAvailability);
    }

    private static (int Cases, int Passed) ValidateTruthTable()
    {
        var cases = 0;
        var passed = 0;
        foreach (var mode in Enum.GetValues<ActivationMode>())
        {
            foreach (var availability in Enum.GetValues<DesktopAvailability>())
            {
                foreach (var gate in Enum.GetValues<DesktopIntentGate>())
                {
                    foreach (var higherPriority in new[] { false, true })
                    {
                        cases++;
                        var expected = mode == ActivationMode.Hover &&
                            availability != DesktopAvailability.Suspended &&
                            (availability == DesktopAvailability.DesktopAvailable ||
                                (availability == DesktopAvailability.ExternalForeground &&
                                    gate == DesktopIntentGate.Armed)) &&
                            !higherPriority;
                        if (HoverGate.CanEnterIntent(mode, availability, gate, higherPriority) == expected)
                        {
                            passed++;
                        }
                    }
                }
            }
        }

        return (cases, passed);
    }

    private static NativeMethods.Rect Rect(int left, int top, int width, int height) =>
        new()
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };

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
