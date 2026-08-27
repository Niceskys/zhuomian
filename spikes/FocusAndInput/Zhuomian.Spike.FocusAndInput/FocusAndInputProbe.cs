using System.Runtime.InteropServices;

namespace Zhuomian.Spike.FocusAndInput;

internal sealed class FocusAndInputProbe
{
    private const int GwlExStyle = -20;
    private const ulong WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTop = 0;
    private const string ProbeToken = "ZHUOMIAN42";

    public static FocusAndInputEvidence Run()
    {
        var safetyAborts = new List<string>();
        var originalForeground = NativeMethods.GetForegroundWindow();
        var externalForegroundAvailable = originalForeground != 0;
        var originalCursorAvailable = NativeMethods.GetCursorPos(out var originalCursor);
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
        var bounds = new Rectangle(
            workingArea.Left + 80,
            workingArea.Top + 80,
            Math.Min(560, Math.Max(320, workingArea.Width - 160)),
            240);

        var visualSurfaceShownWithoutActivation = false;
        var visualSurfaceClickPreservedForeground = false;
        var keyboardSurfaceInitiallyDidNotActivate = false;
        var explicitKeyboardClickAcquiredForeground = false;
        var keyboardSurfaceReceivedFocus = false;
        var guardedUnicodeInputWasDelivered = false;
        var probeTokenWasReceived = false;
        var escapeWasHandled = false;
        var keyboardSurfaceWasClosed = false;
        var originalForegroundWasRestored = false;
        var pointerWasRestored = false;
        var visualSurfaceRemainedNoActivate = false;
        var noProbeWindowsRemained = false;
        nint visualHandle = 0;
        nint keyboardHandle = 0;

        using var visualSurface = new VisualSurfaceForm(bounds);
        using var keyboardSurface = new KeyboardSurfaceForm(bounds);

        try
        {
            if (!externalForegroundAvailable)
            {
                safetyAborts.Add("no-external-foreground");
            }

            if (!originalCursorAvailable)
            {
                safetyAborts.Add("cursor-position-unavailable");
            }

            if (workingArea.IsEmpty)
            {
                safetyAborts.Add("primary-work-area-unavailable");
            }

            if (!InputInjector.AreGuardedKeysReleased())
            {
                safetyAborts.Add("mouse-or-modifier-key-held");
            }

            if (safetyAborts.Count == 0)
            {
                visualHandle = visualSurface.Handle;
                visualSurface.Show();
                NativeMethods.SetWindowPos(
                    visualHandle,
                    HwndTop,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    SwpNoActivate | SwpShowWindow);
                Pump(TimeSpan.FromMilliseconds(150));

                visualSurfaceShownWithoutActivation =
                    NativeMethods.GetForegroundWindow() == originalForeground;

                var visualClickPoint = visualSurface.PointToScreen(
                    new Point(visualSurface.ClientSize.Width / 2, visualSurface.ClientSize.Height / 2));
                var visualClickDelivered = InputInjector.Click(visualClickPoint);
                Pump(TimeSpan.FromMilliseconds(200));
                visualSurfaceClickPreservedForeground = visualClickDelivered &&
                    NativeMethods.GetForegroundWindow() == originalForeground;

                var visualStyle = unchecked(
                    (ulong)NativeMethods.GetWindowLongPtrW(visualHandle, GwlExStyle).ToInt64());
                visualSurfaceRemainedNoActivate = (visualStyle & WsExNoActivate) != 0;

                if (!visualSurfaceClickPreservedForeground)
                {
                    safetyAborts.Add("visual-surface-click-changed-foreground");
                }
            }

            if (safetyAborts.Count == 0)
            {
                visualSurface.Hide();
                keyboardHandle = keyboardSurface.Handle;
                keyboardSurface.Show();
                NativeMethods.SetWindowPos(
                    keyboardHandle,
                    HwndTop,
                    bounds.X,
                    bounds.Y,
                    bounds.Width,
                    bounds.Height,
                    SwpNoActivate | SwpShowWindow);
                Pump(TimeSpan.FromMilliseconds(150));

                keyboardSurfaceInitiallyDidNotActivate =
                    NativeMethods.GetForegroundWindow() == originalForeground;

                var inputClickPoint = keyboardSurface.InputBox.PointToScreen(
                    new Point(
                        keyboardSurface.InputBox.ClientSize.Width / 2,
                        keyboardSurface.InputBox.ClientSize.Height / 2));
                var keyboardClickDelivered = InputInjector.Click(inputClickPoint);
                Pump(TimeSpan.FromMilliseconds(250));

                explicitKeyboardClickAcquiredForeground = keyboardClickDelivered &&
                    NativeMethods.GetForegroundWindow() == keyboardHandle;
                keyboardSurfaceReceivedFocus = keyboardSurface.InputBox.Focused;

                if (!explicitKeyboardClickAcquiredForeground || !keyboardSurfaceReceivedFocus)
                {
                    safetyAborts.Add("explicit-keyboard-click-did-not-acquire-focus");
                }
            }

            if (safetyAborts.Count == 0)
            {
                guardedUnicodeInputWasDelivered = InputInjector.SendUnicode(ProbeToken);
                Pump(TimeSpan.FromMilliseconds(200));
                probeTokenWasReceived = string.Equals(
                    keyboardSurface.InputBox.Text,
                    ProbeToken,
                    StringComparison.Ordinal);

                if (!guardedUnicodeInputWasDelivered || !probeTokenWasReceived)
                {
                    safetyAborts.Add("guarded-input-delivery-failed");
                }
            }

            if (safetyAborts.Count == 0)
            {
                var escapeDelivered = InputInjector.SendEscape();
                Pump(TimeSpan.FromMilliseconds(250));
                escapeWasHandled = escapeDelivered && keyboardSurface.EscapeHandled;
                keyboardSurfaceWasClosed = !NativeMethods.IsWindow(keyboardHandle);
            }
        }
        finally
        {
            if (keyboardHandle != 0 && NativeMethods.IsWindow(keyboardHandle))
            {
                keyboardSurface.Close();
            }

            if (visualHandle != 0 && NativeMethods.IsWindow(visualHandle))
            {
                visualSurface.Close();
            }

            Pump(TimeSpan.FromMilliseconds(100));

            if (externalForegroundAvailable)
            {
                NativeMethods.SetForegroundWindow(originalForeground);
                Pump(TimeSpan.FromMilliseconds(150));
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

            noProbeWindowsRemained =
                (visualHandle == 0 || !NativeMethods.IsWindow(visualHandle)) &&
                (keyboardHandle == 0 || !NativeMethods.IsWindow(keyboardHandle));
        }

        return CreateEvidence();

        FocusAndInputEvidence CreateEvidence()
        {
            var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["external-foreground-available"] = externalForegroundAvailable,
                ["visual-show-no-activate"] = visualSurfaceShownWithoutActivation,
                ["visual-click-preserved-foreground"] = visualSurfaceClickPreservedForeground,
                ["keyboard-show-no-activate"] = keyboardSurfaceInitiallyDidNotActivate,
                ["explicit-click-acquired-foreground"] = explicitKeyboardClickAcquiredForeground,
                ["keyboard-surface-focus"] = keyboardSurfaceReceivedFocus,
                ["guarded-unicode-input-delivered"] = guardedUnicodeInputWasDelivered,
                ["probe-token-received"] = probeTokenWasReceived,
                ["escape-handled"] = escapeWasHandled,
                ["keyboard-surface-closed"] = keyboardSurfaceWasClosed,
                ["original-foreground-restored"] = originalForegroundWasRestored,
                ["pointer-restored"] = pointerWasRestored,
                ["visual-surface-remained-no-activate"] = visualSurfaceRemainedNoActivate,
                ["no-probe-windows-remained"] = noProbeWindowsRemained,
            };

            return new FocusAndInputEvidence(
                SchemaVersion: 1,
                TimestampUtc: DateTimeOffset.UtcNow,
                OsVersion: Environment.OSVersion.VersionString,
                ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
                ExternalForegroundWasAvailable: externalForegroundAvailable,
                VisualSurfaceShownWithoutActivation: visualSurfaceShownWithoutActivation,
                VisualSurfacePhysicalClickPreservedForeground: visualSurfaceClickPreservedForeground,
                KeyboardSurfaceInitiallyDidNotActivate: keyboardSurfaceInitiallyDidNotActivate,
                ExplicitKeyboardClickAcquiredForeground: explicitKeyboardClickAcquiredForeground,
                KeyboardSurfaceReceivedFocus: keyboardSurfaceReceivedFocus,
                GuardedUnicodeInputWasDelivered: guardedUnicodeInputWasDelivered,
                ProbeTokenWasReceived: probeTokenWasReceived,
                EscapeWasHandled: escapeWasHandled,
                KeyboardSurfaceWasClosed: keyboardSurfaceWasClosed,
                OriginalForegroundWasRestored: originalForegroundWasRestored,
                PointerWasRestored: pointerWasRestored,
                VisualSurfaceRemainedNoActivate: visualSurfaceRemainedNoActivate,
                NoProbeWindowsRemained: noProbeWindowsRemained,
                InputToken: ProbeToken,
                SafetyAborts: [.. safetyAborts],
                Limitations:
                [
                    "SendInput is subject to UIPI and was tested only at the current integrity level.",
                    "The probe uses a separate WinForms keyboard surface, not the final WinUI implementation.",
                    "IME, Narrator, touch, high contrast, and multiple monitors are not covered by this probe.",
                ],
                FailedChecks: checks.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray());
        }
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
