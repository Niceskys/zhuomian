using System.Diagnostics;
using System.Runtime.Versioning;
using Zhuomian.Core.Interaction;
using static Zhuomian.Platform.Windows.NativeMethods;

namespace Zhuomian.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class NativeSmoke
{
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
        bool refused = false;
        try { failedStart.Start(false); }
        catch (InvalidOperationException) { refused = failedStart.Window == 0 && failedStart.ActiveHookCount == 0; }
        Require(refused, "initial_query_failure");
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
        }) { IsBackground = true };
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
        Require(!host.DeadlineTimerArmed && host.PendingCompletionCount == 0, "idle_timer");
        log(FormattableString.Invariant($"hidden_snapshot private_bytes={process.PrivateMemorySize64} working_set_bytes={process.WorkingSet64} handles={process.HandleCount} threads={process.Threads.Count} owned_windows=1 hooks={host.ActiveHookCount} session_registered={host.SessionRegistered} idle_timers=0 gdi_objects={GetGuiResources(process.Handle, 0)} evidence=CI_diagnostic_not_product_idle"));
        host.Dispose();
        if (IsWindow(window) || host.ActiveHookCount != 0 || host.Dispatcher.Controller.PointerCaptured || host.Dispatcher.Controller.FocusOwner is not null)
        { throw new InvalidOperationException("native_smoke_cleanup_failed"); }
        Require(!host.SessionRegistered && !host.DeadlineTimerArmed, "native_smoke_session_timer_release");
        log("native_smoke_cleanup hwnd_destroyed=true hooks=0 session=false timers=0 pointer_capture=false focus_owner=false");
    }
}
