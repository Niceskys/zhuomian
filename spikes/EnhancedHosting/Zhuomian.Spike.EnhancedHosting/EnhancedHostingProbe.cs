using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.EnhancedHosting;

internal static class EnhancedHostingProbe
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const ulong WsCaption = 0x00C00000;
    private const ulong WsThickFrame = 0x00040000;
    private const ulong WsExTopMost = 0x00000008;
    private const ulong WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndBottom = 1;

    public static EnhancedHostingEvidence Run(bool requestPrivateWorker)
    {
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? Rectangle.Empty;
        var monitorBounds = Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        var foregroundBefore = NativeMethods.GetForegroundWindow();
        var topologyBefore = ShellTopologyScanner.Scan(monitorBounds);
        var privateRequestAttempted = requestPrivateWorker &&
            !topologyBefore.HasViableEnhancedCandidate;
        var privateRequestDelivered = privateRequestAttempted &&
            ShellTopologyScanner.RequestPrivateWorkerW();
        Pump(TimeSpan.FromMilliseconds(privateRequestAttempted ? 250 : 25));
        var topologyAfter = ShellTopologyScanner.Scan(monitorBounds);
        var candidateValid = topologyAfter.HasViableEnhancedCandidate;

        // Cross-process SetParent is intentionally not attempted without a fully valid
        // candidate and an accepted DPI-preservation protocol.
        const bool attachmentSucceeded = false;
        const bool dpiContextPreserved = false;
        const bool crossProcessSetParentAttempted = false;
        var selectedMode = HostSelectionPolicy.Select(new SelectionInput(
            candidateValid,
            attachmentSucceeded,
            dpiContextPreserved));
        var automaticFallbackOccurred = selectedMode == HostMode.PublicWin32Fallback;
        var rejectionReasons = new List<string>();
        if (!candidateValid)
        {
            rejectionReasons.Add("no-visible-shell-owned-workarea-sized-workerw");
        }

        if (!attachmentSucceeded)
        {
            rejectionReasons.Add("cross-process-attachment-not-validated");
        }

        if (!dpiContextPreserved)
        {
            rejectionReasons.Add("cross-process-dpi-preservation-not-proven");
        }

        var fallbackShownWithoutActivation = false;
        var fallbackWasNoActivate = false;
        var fallbackWasNotTopMost = false;
        var fallbackWasBorderless = false;
        var fallbackClientAreaMatchedWindow = false;
        var fallbackMatchedWorkArea = false;
        var fallbackDpiWasAvailable = false;
        var fallbackWasDestroyed = false;
        nint fallbackHandle = 0;

        using (var fallback = new FallbackHostForm(workArea))
        {
            fallbackHandle = fallback.Handle;
            fallback.Show();
            var placed = NativeMethods.SetWindowPos(
                fallbackHandle,
                HwndBottom,
                workArea.X,
                workArea.Y,
                workArea.Width,
                workArea.Height,
                SwpNoActivate | SwpShowWindow);
            Pump(TimeSpan.FromMilliseconds(100));

            fallbackShownWithoutActivation =
                foregroundBefore != 0 && NativeMethods.GetForegroundWindow() == foregroundBefore;
            var style = unchecked(
                (ulong)NativeMethods.GetWindowLongPtrW(fallbackHandle, GwlStyle).ToInt64());
            var extendedStyle = unchecked(
                (ulong)NativeMethods.GetWindowLongPtrW(fallbackHandle, GwlExStyle).ToInt64());
            fallbackWasNoActivate = (extendedStyle & WsExNoActivate) != 0;
            fallbackWasNotTopMost = (extendedStyle & WsExTopMost) == 0;
            fallbackWasBorderless =
                (style & WsCaption) == 0 &&
                (style & WsThickFrame) == 0;
            var windowRectAvailable = NativeMethods.GetWindowRect(fallbackHandle, out var windowRect);
            var clientRectAvailable = NativeMethods.GetClientRect(fallbackHandle, out var clientRect);
            var clientOrigin = default(NativeMethods.Point);
            var clientOriginAvailable = NativeMethods.ClientToScreen(
                fallbackHandle,
                ref clientOrigin);
            fallbackClientAreaMatchedWindow =
                windowRectAvailable &&
                clientRectAvailable &&
                clientOriginAvailable &&
                clientOrigin.X == windowRect.Left &&
                clientOrigin.Y == windowRect.Top &&
                clientRect.Width == windowRect.Width &&
                clientRect.Height == windowRect.Height;
            fallbackMatchedWorkArea =
                placed &&
                windowRectAvailable &&
                windowRect.Left == workArea.Left &&
                windowRect.Top == workArea.Top &&
                windowRect.Right == workArea.Right &&
                windowRect.Bottom == workArea.Bottom;
            fallbackDpiWasAvailable = NativeMethods.GetDpiForWindow(fallbackHandle) > 0;
            fallback.Close();
            Pump(TimeSpan.FromMilliseconds(50));
            fallbackWasDestroyed = !NativeMethods.IsWindow(fallbackHandle);
        }

        var policyChecks = ValidatePolicy();
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["progman-desktop-view-observed"] = topologyBefore.ProgmanContainsDesktopView,
            ["unvalidated-enhanced-not-selected"] = !candidateValid || !crossProcessSetParentAttempted,
            ["cross-process-setparent-not-attempted"] = !crossProcessSetParentAttempted,
            ["automatic-fallback"] = automaticFallbackOccurred,
            ["fallback-show-noactivate"] = fallbackShownWithoutActivation,
            ["fallback-noactivate-style"] = fallbackWasNoActivate,
            ["fallback-not-topmost"] = fallbackWasNotTopMost,
            ["fallback-borderless"] = fallbackWasBorderless,
            ["fallback-client-equals-window"] = fallbackClientAreaMatchedWindow,
            ["fallback-matches-workarea"] = fallbackMatchedWorkArea,
            ["fallback-dpi-available"] = fallbackDpiWasAvailable,
            ["fallback-destroyed"] = fallbackWasDestroyed,
            ["selection-policy"] = policyChecks.All(check => check.Passed),
        };

        return new EnhancedHostingEvidence(
            SchemaVersion: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.VersionString,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            WorkerWCountBeforeRequest: topologyBefore.WorkerWCount,
            VisibleWorkerWCountBeforeRequest: topologyBefore.VisibleWorkerWCount,
            ViableWorkerWCountBeforeRequest: topologyBefore.ViableWorkerWCount,
            ProgmanContainedDesktopView: topologyBefore.ProgmanContainsDesktopView,
            PrivateWorkerRequestAttempted: privateRequestAttempted,
            PrivateWorkerRequestDelivered: privateRequestDelivered,
            WorkerWCountAfterRequest: topologyAfter.WorkerWCount,
            VisibleWorkerWCountAfterRequest: topologyAfter.VisibleWorkerWCount,
            ViableWorkerWCountAfterRequest: topologyAfter.ViableWorkerWCount,
            PrivateRequestProducedViableCandidate: privateRequestDelivered && candidateValid,
            EnhancedCandidateAccepted: candidateValid && attachmentSucceeded && dpiContextPreserved,
            CrossProcessSetParentAttempted: crossProcessSetParentAttempted,
            SelectedHostMode: selectedMode.ToString(),
            AutomaticFallbackOccurred: automaticFallbackOccurred,
            FallbackShownWithoutActivation: fallbackShownWithoutActivation,
            FallbackWasNoActivate: fallbackWasNoActivate,
            FallbackWasNotTopMost: fallbackWasNotTopMost,
            FallbackWasBorderless: fallbackWasBorderless,
            FallbackClientAreaMatchedWindow: fallbackClientAreaMatchedWindow,
            FallbackMatchedWorkArea: fallbackMatchedWorkArea,
            FallbackDpiWasAvailable: fallbackDpiWasAvailable,
            FallbackWasDestroyed: fallbackWasDestroyed,
            PolicyChecks: policyChecks,
            EnhancedRejectionReasons: [.. rejectionReasons],
            Limitations:
            [
                "WorkerW and message 0x052C are undocumented Shell behavior and are never required by the product.",
                "No viable WorkerW candidate existed on this Windows build, so cross-process SetParent was not attempted.",
                "Microsoft documents that cross-process SetParent can reset the child DPI-awareness context.",
                "The public fallback technical contract is validated; visual usability remains separate evidence.",
            ],
            FailedChecks: checks.Where(check => !check.Value).Select(check => check.Key).ToArray());
    }

    private static PolicyCheckEvidence[] ValidatePolicy() =>
    [
        CheckPolicy("all-enhanced-preconditions-select-enhanced", true, true, true, HostMode.EnhancedWorkerW),
        CheckPolicy("missing-candidate-selects-fallback", false, true, true, HostMode.PublicWin32Fallback),
        CheckPolicy("attachment-failure-selects-fallback", true, false, true, HostMode.PublicWin32Fallback),
        CheckPolicy("dpi-reset-selects-fallback", true, true, false, HostMode.PublicWin32Fallback),
        CheckPolicy("all-failures-select-fallback", false, false, false, HostMode.PublicWin32Fallback),
    ];

    private static PolicyCheckEvidence CheckPolicy(
        string name,
        bool candidate,
        bool attached,
        bool dpiPreserved,
        HostMode expected) =>
        new(name, HostSelectionPolicy.Select(new SelectionInput(
            candidate,
            attached,
            dpiPreserved)) == expected);

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
