using System.Diagnostics;
using System.Runtime.Versioning;
using Zhuomian.Core.Interaction;
using static Zhuomian.Platform.Windows.NativeMethods;

namespace Zhuomian.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class NativeSmoke
{
    private sealed class TestClock : IInteractionClock { public long NowMilliseconds { get; set; } }
    internal static void CheckHiddenMotion(Action<string> log)
    {
        var clock = new TestClock();
        nint foreground = GetForegroundWindow();
        using var host = new FallbackDesktopHost(log, null, clock: clock);
        host.Start(visible: false);
        host.Dispatcher.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-hidden-test");
        string id = EngineeringFixture.ClickId.ToString("D");
        var hit = new SurfaceHit(id);
        host.Dispatcher.DispatchPointer(PointerAction.Down, hit);
        host.Dispatcher.DispatchPointer(PointerAction.Up, hit);
        using var resources = Process.GetCurrentProcess();
        uint gdiBefore = GetGuiResources(resources.Handle, 0);
        int maximumTimers = 0;
        for (int frame = 0; frame <= 12; frame++)
        {
            clock.NowMilliseconds = frame * 20;
            host.RenderHiddenTestFrame(true);
            maximumTimers = Math.Max(maximumTimers, (host.DeadlineTimerArmed ? 1 : 0) + (host.AnimationTimerArmed ? 1 : 0));
            CheckRegion(host);
            var bounds = host.Dispatcher.VisualBounds[id];
            log(FormattableString.Invariant($"hidden_frame synthetic_ms={clock.NowMilliseconds} space={id} x={bounds.X:F3} y={bounds.Y:F3} width={bounds.Width:F3} height={bounds.Height:F3} token={host.Dispatcher.Controller.GetState(id).TransitionToken}"));
        }
        if (host.AnimationTimerArmed || host.Dispatcher.HasActiveAnimation || host.PaintCount == 0 || GetForegroundWindow() != foreground)
        { throw new InvalidOperationException("hidden_motion_frame_failed"); }
        host.Dispatcher.Complete(new(id, host.Dispatcher.Controller.InputGeneration, host.Dispatcher.Controller.GetState(id).TransitionToken));
        var close = new SurfaceHit(id, Close: true);
        host.Dispatcher.DispatchPointer(PointerAction.Down, close);
        host.Dispatcher.DispatchPointer(PointerAction.Up, close);
        host.RenderHiddenTestFrame(true);
        clock.NowMilliseconds += 80; host.RenderHiddenTestFrame(true); CheckRegion(host);
        var displayed = host.Dispatcher.VisualBounds[id];
        host.Dispatcher.DispatchPointer(PointerAction.Down, hit); host.Dispatcher.DispatchPointer(PointerAction.Up, hit);
        host.RenderHiddenTestFrame(true);
        if (host.Dispatcher.VisualBounds[id] != displayed) { throw new InvalidOperationException("hidden_motion_reverse_jump"); }
        host.RenderHiddenTestFrame(false); CheckRegion(host);
        if (host.AnimationTimerArmed) { throw new InvalidOperationException("hidden_reduced_motion_timer"); }
        host.Dispatcher.HostLost(); host.RenderHiddenTestFrame(true);
        if (host.AnimationTimerArmed || host.Dispatcher.HasActiveAnimation) { throw new InvalidOperationException("hidden_hostlost_motion"); }
        nint window = host.Window; host.Dispose();
        if (IsWindow(window) || host.ActiveHookCount != 0) { throw new InvalidOperationException("hidden_motion_release"); }
        uint gdiAfter = GetGuiResources(resources.Handle, 0);
        Require(gdiAfter <= gdiBefore, "motion_gdi_release");
        log($"native_motion_resources paint_count={host.PaintCount} synthetic_frames=13 timer_callbacks={host.AnimationFrameCount} gdi_before={gdiBefore} gdi_after={gdiAfter} active_timer_max={maximumTimers} idle_timers=0");
        log("native_motion hidden=true synthetic_clock=true frames=13 region_hit_consistent=true paint_executed=true reverse_continuous=true reduced_motion_timer=0 hostlost_timer=0 foreground_unchanged=true desktop_fluency_UNVERIFIED");
    }
    private static void CheckRegion(FallbackDesktopHost host)
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) { throw new InvalidOperationException("hidden_region_create"); }
        try
        {
            if (GetWindowRgn(host.Window, region) == 0) { throw new InvalidOperationException("hidden_region_missing"); }
            double scale = host.Dpi / 96d;
            foreach (var bounds in host.Dispatcher.VisualBounds.Values)
            {
                int left = (int)Math.Round(bounds.X * scale), top = (int)Math.Round(bounds.Y * scale);
                int right = (int)Math.Round(bounds.Right * scale), bottom = (int)Math.Round(bounds.Bottom * scale);
                foreach (var (x, y) in new[] { (left, top), (right - 1, bottom - 1), (left - 1, top), (right, bottom - 1) })
                {
                    if (PtInRegion(region, x, y) != (host.HitAt(x, y) is not null))
                    { throw new InvalidOperationException("hidden_region_hit_mismatch"); }
                }
            }
        }
        finally { DeleteObject(region); }
    }
    internal static void CheckMotionSafety(Action<string> log)
    {
        uint shell = RegisterWindowMessageW("TaskbarCreated");
        int cases = 0;
        foreach (uint message in new uint[] { 0x1A, 0x7E, 0x2E0, shell, 0x200, 0x113, 0x8002, 0x8001, 0x113 })
        {
            foreach (string failure in new[] { "changed", "unavailable", "exception" })
            {
                bool invalid = false;
                WorkAreaSnapshot? Query() => !invalid ? new(0, 0, 1200, 800) : failure switch
                {
                    "changed" => new(0, 40, 1200, 800),
                    "unavailable" => null,
                    _ => throw new IOException("injected_query_failure")
                };
                var clock = new TestClock();
                using var host = new FallbackDesktopHost(log, null, Query, clock);
                host.Start(false); nint window = host.Window;
                string b = EngineeringFixture.ClickId.ToString("D");
                host.Dispatcher.DispatchPointer(PointerAction.Down, new(b)); host.Dispatcher.DispatchPointer(PointerAction.Up, new(b));
                host.RenderHiddenTestFrame(false); // Completion 1 is deliberately delayed across reversal.
                host.Dispatcher.DispatchPointer(PointerAction.Down, new(b, Close: true)); host.Dispatcher.DispatchPointer(PointerAction.Up, new(b, Close: true));
                host.Dispatcher.Controller.Post(new(SpaceInteractionEventKind.VisibleDesktopClick)); host.Dispatcher.Controller.Drain();
                host.RenderHiddenTestFrame(true);
                Require(host.AnimationTimerArmed && host.DeadlineTimerArmed && host.PendingCompletionCount > 0, "motion_work_area_setup");
                invalid = true;
                nuint parameter = message == 0x8002 ? 1u : message == 0x113 ? (cases < 24 ? 1u : 2u) : 0u;
                SendMessageW(window, message, parameter, 0);
                Require(!IsWindow(window) && !host.ClassRegistered && host.ActiveHookCount == 0 && !host.SessionRegistered &&
                    !host.AnimationTimerArmed && !host.DeadlineTimerArmed && host.PendingCompletionCount == 0 &&
                    !host.Dispatcher.HasActiveAnimation && !host.Dispatcher.Controller.PointerCaptured && host.ExitCode == 2 &&
                    host.StopReason == (failure == "changed" ? "work_area_changed_restart_required" : "work_area_query_failed"), "motion_work_area_release");
                SendMessageW(window, 0x113, 2, 0); Require(!host.AnimationTimerArmed, "late_motion_timer");
                while (PeekMessageW(out _, 0, 0x12, 0x12, 1)) { }
                cases++;
            }
        }
        foreach (string stage in new[] { "region", "paint", "paint_callback" })
        {
            using var failed = new FallbackDesktopHost(_ => { }); failed.Start(false); nint window = failed.Window;
            failed.FailNextHiddenFrame(stage); SendMessageW(window, 0x113, 2, 0);
            Require(!IsWindow(window) && failed.StopReason == "native_callback_failed" && !failed.AnimationTimerArmed &&
                !failed.DeadlineTimerArmed && failed.ActiveHookCount == 0 && !failed.SessionRegistered && !failed.ClassRegistered,
                "frame_failure_release");
            while (PeekMessageW(out _, 0, 0x12, 0x12, 1)) { }
        }
        var overlapClock = new TestClock();
        using (var overlap = new FallbackDesktopHost(log, null, () => new(0, 0, 800, 600), overlapClock))
        {
            overlap.Start(false); string b = EngineeringFixture.ClickId.ToString("D");
            overlap.Dispatcher.DispatchPointer(PointerAction.Down, new(b)); overlap.Dispatcher.DispatchPointer(PointerAction.Up, new(b));
            overlap.RenderHiddenTestFrame(true); overlapClock.NowMilliseconds = 240; overlap.RenderHiddenTestFrame(true);
            double scale = overlap.Dpi / 96d;
            Require(overlap.HitAt((int)Math.Round(200 * scale), (int)Math.Round(200 * scale))?.SpaceId == b &&
                overlap.LastPaintOrder.Count == 2 && overlap.LastPaintOrder.Distinct().Count() == 2 && overlap.LastPaintOrder[^1] == b,
                "single_paint_focus_hit_stack");
            CheckRegion(overlap);
        }
        // Settings messages exercise actual policy refresh; no system setting is written.
        bool? preference = false;
        using var policy = new FallbackDesktopHost(log, null, clock: new TestClock(), motionPreferenceQuery: () => preference);
        policy.Start(false); string id = EngineeringFixture.ClickId.ToString("D");
        var item = new SurfaceHit(id, EngineeringFixture.ItemId.ToString("D"));
        policy.Dispatcher.DispatchPointer(PointerAction.Down, new(id)); policy.Dispatcher.DispatchPointer(PointerAction.Up, new(id));
        policy.RefreshHiddenTestFrame(); Require(!policy.AnimationTimerArmed, "startup_reduced_motion");
        preference = true; SendMessageW(policy.Window, 0x1A, 0, 0);
        // Actual high contrast may independently prohibit motion; hidden override tests continuous frames separately.
        policy.RenderHiddenTestFrame(true); policy.Dispatcher.DispatchPointer(PointerAction.Down, item);
        policy.RenderHiddenTestFrame(true); var pinned = policy.Dispatcher.VisualBounds[id];
        policy.SetHiddenMotionPolicy(null); preference = false; SendMessageW(policy.Window, 0x1A, 0, 0);
        Require(!policy.AnimationTimerArmed && policy.Dispatcher.VisualBounds[id] == pinned, "settings_pinned_motion");
        policy.Dispatcher.DispatchPointer(PointerAction.Cancel); policy.Dispatcher.DispatchPointer(PointerAction.Up, item);
        policy.RefreshHiddenTestFrame(); Require(!policy.AnimationTimerArmed && policy.Dispatcher.FakeLaunchCount == 0, "settings_cancel_safe");
        preference = null; SendMessageW(policy.Window, 0x1A, 0, 0); Require(!policy.AnimationTimerArmed, "unknown_motion_policy");
        preference = true; SendMessageW(policy.Window, 0x1A, 0, 0);
        policy.Dispose(); Require(!policy.AnimationTimerArmed && !policy.DeadlineTimerArmed && !policy.ClassRegistered, "policy_release");
        log($"native_motion_safety hidden=true synthetic_clock_query_input=true work_area_cases={cases} timer_ids=1,2 independent_release=true late_timer_rejected=true region_paint_failure_shutdown=pass settings_refresh=pass preview_pin=pass unknown_reduced=pass");
    }
    private sealed class FrozenClock : IInteractionClock { public long NowMilliseconds => 0; }
    internal static void CheckWorkAreaShutdown(Action<string> log)
    {
        uint shellMessage = RegisterWindowMessageW("TaskbarCreated");
        foreach (uint message in new uint[] { 0x1A, 0x7E, 0x2E0, shellMessage, 0x200, 0x113, 0x8002, 0x8001 })
        {
            foreach (string failure in new[] { "changed", "unavailable", "exception" })
            {
                var initial = new WorkAreaSnapshot(0, 0, 1200, 800);
                bool fail = false;
                WorkAreaSnapshot? Query() => !fail ? initial : failure switch
                {
                    "changed" => initial with { Top = 40 },
                    "unavailable" => null,
                    _ => throw new InvalidOperationException("injected_query_failure")
                };
                using var host = new FallbackDesktopHost(log, null, Query, new FrozenClock());
                host.Start(false); nint window = host.Window;
                string b = EngineeringFixture.ClickId.ToString("D");
                host.Dispatcher.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-work-area");
                host.Dispatcher.DispatchPointer(PointerAction.Down, new(b));
                host.Dispatcher.DispatchPointer(PointerAction.Up, new(b));
                host.RefreshHiddenTestFrame(); // Queues completion 1 using real PostMessage.
                host.Dispatcher.Controller.Post(new(SpaceInteractionEventKind.VisibleDesktopClick));
                host.Dispatcher.Controller.Drain();
                host.Dispatcher.DispatchPointer(PointerAction.Down, new(b, EngineeringFixture.ItemId.ToString("D")));
                host.RefreshHiddenTestFrame();
                Require(host.DeadlineTimerArmed && host.PendingCompletionCount > 0 && host.Dispatcher.Controller.PointerCaptured,
                    "work_area_setup");
                fail = true;
                SendMessageW(window, message, message is 0x113 or 0x8002 ? 1u : 0u, 0);
                string reason = failure == "changed" ? "work_area_changed_restart_required" : "work_area_query_failed";
                Require(host.StopReason == reason && host.ExitCode == 2 && !IsWindow(window) && host.Window == 0 &&
                    host.ActiveHookCount == 0 && !host.SessionRegistered && !host.DeadlineTimerArmed && host.PendingCompletionCount == 0 &&
                    !host.Dispatcher.Controller.PointerCaptured && !host.Dispatcher.HasGesture &&
                    host.Dispatcher.Controller.FocusOwner is null && host.Dispatcher.Controller.IntentGate == DesktopIntentGate.Disarmed &&
                    host.Dispatcher.Controller.KeyboardMode == KeyboardMode.NoKeyboardCapture, "work_area_release");
                host.Dispatcher.DispatchPointer(PointerAction.Up, new(b, EngineeringFixture.ItemId.ToString("D")));
                Require(host.Dispatcher.FakeLaunchCount == 0, "work_area_late_input");
                // Isolated hidden smoke owns these quit messages; never remove a real application's quit request.
                while (PeekMessageW(out _, 0, 0x12, 0x12, 1)) { }
                log($"work_area_shutdown message={message:X} query={failure} reason={reason} hwnd_destroyed=true hooks=0 session=false timers=0 pending=0 capture=false fake=0");
            }
        }
        // Unchanged settings/Shell notifications must not request a restart.
        using var unchanged = new FallbackDesktopHost(log);
        unchanged.Start(false);
        SendMessageW(unchanged.Window, 0x1A, 0, 0); SendMessageW(unchanged.Window, shellMessage, 0, 0);
        Require(IsWindow(unchanged.Window) && unchanged.StopReason is null, "unchanged_work_area");
        unchanged.Dispose();
        // A new process/host accepts its newly queried snapshot instead of retaining the old coordinates.
        using var restarted = new FallbackDesktopHost(log, null, () => new(0, 40, 1200, 800));
        restarted.Start(false);
        Require(GetWindowRect(restarted.Window, out var rect) && rect.Top == 40 && rect.Bottom == 800, "restart_work_area");
        restarted.Dispose();
        using var failedStart = new FallbackDesktopHost(log, null, () => null);
        failedStart.Start(false);
        Require(failedStart.Window == 0 && failedStart.ActiveHookCount == 0 && !failedStart.ClassRegistered &&
            !failedStart.SessionRegistered && failedStart.StopReason == "work_area_query_failed" && failedStart.Run() == 2,
            "initial_query_failure");
        bool changed = false;
        using var failedLog = new FallbackDesktopHost(message =>
        {
            if (message == "work_area_changed_restart_required" || message.StartsWith("host_closed", StringComparison.Ordinal))
            { throw new IOException("injected_sink_failure"); }
        }, null,
            () => new(0, 0, 1200, changed ? 760 : 800));
        failedLog.Start(false); nint failedLogWindow = failedLog.Window; changed = true;
        SendMessageW(failedLogWindow, 0x1A, 0, 0);
        Require(!IsWindow(failedLogWindow) && failedLog.StopReason == "work_area_changed_restart_required" &&
            failedLog.ActiveHookCount == 0 && !failedLog.SessionRegistered && !failedLog.ClassRegistered,
            "logging_failure_cannot_block_release");
        while (PeekMessageW(out _, 0, 0x12, 0x12, 1)) { }
        log("work_area_logging_failure_release=pass");
        log("work_area_smoke hidden=true synthetic_query=true cases=24 unchanged_notifications=pass restart_snapshot=pass initial_query_failure=pass");
    }
    internal static void CheckRegionAndOwnership(Action<string> log)
    {
        using var host = new FallbackDesktopHost(log); host.Start(false);
        nint window = host.Window;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                foreach (Action action in new Action[] { () => host.Start(false), () => host.Run(), host.Dispose })
                {
                    bool rejected = false;
                    try { action(); } catch (InvalidOperationException) { rejected = true; }
                    Require(rejected, "owner_thread");
                }
            }
            catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true };
        worker.Start(); Require(worker.Join(TimeSpan.FromSeconds(5)) && failure is null, "owner_thread_guard");
        using var process = Process.GetCurrentProcess();
        for (int state = 0; state < 2; state++)
        {
            if (state == 1)
            {
                string b = EngineeringFixture.ClickId.ToString("D");
                host.Dispatcher.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-region");
                host.Dispatcher.DispatchPointer(PointerAction.Down, new(b)); host.Dispatcher.DispatchPointer(PointerAction.Up, new(b));
                host.RefreshHiddenTestFrame();
            }
            nint region = CreateRectRgn(0, 0, 0, 0);
            Require(region != 0, "region_create");
            try
            {
                Require(GetWindowRgn(window, region) != 0, "region_read");
                double scale = host.Dpi / 96d;
                foreach (var bounds in host.Dispatcher.VisualBounds.Values)
                {
                    int left = (int)Math.Round(bounds.X * scale), top = (int)Math.Round(bounds.Y * scale);
                    int right = (int)Math.Round(bounds.Right * scale), bottom = (int)Math.Round(bounds.Bottom * scale);
                    foreach (var (x, y) in new[] { (left, top), (right - 1, bottom - 1), (left - 1, top), (right, bottom - 1) })
                    { Require(PtInRegion(region, x, y) == (host.HitAt(x, y) is not null), "region_hit"); }
                }
            }
            finally { DeleteObject(region); }
        }
        SendMessageW(window, 0x0F, 0, 0); uint gdi = GetGuiResources(process.Handle, 0);
        for (int i = 0; i < 20; i++)
        {
            Require(InvalidateRect(window, 0, false) && UpdateWindow(window), "paint_update");
            SendMessageW(window, 0x0F, 0, 0);
        }
        uint after = GetGuiResources(process.Handle, 0);
        Require(host.PaintCount > 0 && gdi > 0 && after > 0 && after <= gdi, "gdi_paint_release");
        SendMessageW(window, 0x10, 0, 0);
        Require(!IsWindow(window) && host.ExitCode == 0 && host.StopReason == "user_exit" &&
            host.ActiveHookCount == 0 && !host.SessionRegistered && !host.DeadlineTimerArmed, "region_window_release");
        while (PeekMessageW(out _, 0, 0x12, 0x12, 1)) { }
        using var lost = new FallbackDesktopHost(log); lost.Start(false);
        string a = EngineeringFixture.HoverId.ToString("D");
        lost.Dispatcher.DispatchPointer(PointerAction.Down, new(a, EngineeringFixture.ItemId.ToString("D")));
        Require(DestroyWindow(lost.Window), "external_window_destroy");
        Require(lost.StopReason == "host_lost" && lost.Window == 0 && lost.ActiveHookCount == 0 &&
            !lost.SessionRegistered && !lost.Dispatcher.Controller.PointerCaptured, "external_host_lost_release");
        lost.Dispose(); // The external destruction callback has now unwound; release its class too.
        while (PeekMessageW(out _, 0, 0x12, 0x12, 1)) { }
        Require(!host.ClassRegistered && !lost.ClassRegistered, "class_release");
        log($"native_region hidden=true synthetic_input=true idle_and_expanded_edges=pass paint_count={host.PaintCount} gdi_before={gdi} gdi_after={after} owner_thread_guard=pass external_host_lost_release=pass classes=0");
    }
    private static void Require(bool value, string reason)
    {
        if (!value) { throw new InvalidOperationException(reason); }
    }
    /// <summary>Actual hidden HWND checks; never injects mouse input, changes Shell or captures the desktop.</summary>
    public static void Run(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        CheckHiddenMotion(log);
        CheckMotionSafety(log);
        CheckWorkAreaShutdown(log);
        CheckRegionAndOwnership(log);
        nint foreground = GetForegroundWindow();
        var elapsed = Stopwatch.StartNew();
        using var host = new FallbackDesktopHost(log);
        host.Start(visible: false);
        nint window = host.Window;
        nint anchor = ZOrderPlacement.SelectAnchor(window, window, true, DesktopAvailability.DesktopAvailable,
            callback => EnumWindows((handle, _) => callback(handle), 0), _ => false);
        if (anchor != 0 || !SetWindowPos(window, anchor, 0, 0, 0, 0, 0x13))
        { throw new InvalidOperationException("native_zorder_boundary_failed"); }
        log("native_zorder controlled_hidden_boundary_enumerated=true enumeration_completed=true normal_top=true not_Shell_foreground_or_WinD_evidence");
        long styles = (long)GetWindowLongPtrW(window, -20);
        if (!IsWindow(window) || (styles & 0x08000080) != 0x08000080 || (styles & 8) != 0 ||
            SendMessageW(window, 0x21, 0, 0) != 3 || GetForegroundWindow() != foreground)
        { throw new InvalidOperationException("native_smoke_failed"); }
        log(FormattableString.Invariant($"native_smoke hidden=true monitors=1 windows_build={Environment.OSVersion.Version.Build} dpi={host.Dpi} startup_ms={elapsed.Elapsed.TotalMilliseconds:F3} hwnd_valid=true noactivate=true topmost=false mouseactivate=3 foreground_unchanged=true hooks={host.ActiveHookCount}"));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        Require(!host.DeadlineTimerArmed && !host.AnimationTimerArmed && host.PendingCompletionCount == 0, "idle_timer");
        log(FormattableString.Invariant($"hidden_snapshot private_bytes={process.PrivateMemorySize64} working_set_bytes={process.WorkingSet64} handles={process.HandleCount} threads={process.Threads.Count} owned_windows=1 hooks={host.ActiveHookCount} session_registered={host.SessionRegistered} idle_timers=0 gdi_objects={GetGuiResources(process.Handle, 0)} evidence=CI_diagnostic_not_product_idle"));
        host.Dispose();
        if (IsWindow(window) || host.ActiveHookCount != 0 || host.Dispatcher.Controller.PointerCaptured || host.Dispatcher.Controller.FocusOwner is not null)
        { throw new InvalidOperationException("native_smoke_cleanup_failed"); }
        Require(!host.SessionRegistered && !host.DeadlineTimerArmed && !host.AnimationTimerArmed, "native_smoke_session_timer_release");
        log("native_smoke_cleanup hwnd_destroyed=true hooks=0 session=false timers=0 pointer_capture=false focus_owner=false");
    }
}
