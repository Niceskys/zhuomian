using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zhuomian.Core.Interaction;
using Zhuomian.Platform.Windows;
using static Zhuomian.Platform.Windows.NativeMethods;

namespace Zhuomian.Rendering.Wpf;

/// <summary>Actual hidden HWND and offscreen WPF evidence, never global desktop input.</summary>
public static class WpfSmoke
{
    private static readonly int[] Capacities = [6, 8, 12];
    private sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    private sealed class SubmissionFailure(IHostRenderer inner) : IHostRenderer
    {
        internal bool Fail;
        public nint Create(int left, int top, int width, int height, Func<nint, uint, nuint, nint, nint?> callback) => inner.Create(left, top, width, height, callback);
        public void Submit(SceneSnapshot scene)
        {
            if (Fail) { throw new InvalidOperationException("injected_renderer_submit_failure"); }
            inner.Submit(scene);
        }
        public void Reset() => inner.Reset();
        public int Run() => inner.Run();
        public void RequestStop(int exitCode) => inner.RequestStop(exitCode);
        public void Dispose() => inner.Dispose();
    }
    private static void Require(bool condition, string code) { if (!condition) { throw new InvalidOperationException(code); } }
    public static void Run(Action<string> log, string? evidenceDirectory = null)
    {
        var clock = new Clock();
        nint foreground = GetForegroundWindow();
        using var renderer = new WpfHostRenderer();
        using var host = new FallbackDesktopHost(log, null, () => new(0, 0, 1200, 800), clock, renderer: renderer);
        host.Start(false);
        host.Dispatcher.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-wpf-smoke");
        host.RenderHiddenTestFrame(false);
        Require(host.Window == renderer.Handle && renderer.HasRootVisual && host.ActiveHookCount == 1 && host.SessionRegistered, "wpf_ownership");
        Require(((long)GetWindowLongPtrW(host.Window, -20) & 0x08000008) == 0x08000000, "wpf_style");
        Require(SendMessageW(host.Window, 0x21, 0, 0) == 3, "wpf_noactivate");
        // Reconcile the actual runner foreground before testing stable direct Preview presses.
        SendMessageW(host.Window, 0x200, 0, 0);
        clock.NowMilliseconds += SceneInputGuard.LagWindowMilliseconds + 1;
        host.RenderHiddenTestFrame(false);
        var idle = host.SubmittedScene!;
        Require(idle.Spaces.Select(s => s.Items.Length).Order().SequenceEqual(Capacities), "wpf_capacity");
        Require(idle.Spaces.All(s => !s.Expanded && s.CloseBounds is null), "wpf_idle_structure");
        CheckRegion(host, renderer);
        if (evidenceDirectory is not null) { renderer.SaveOffscreen(Path.Combine(evidenceDirectory, "idle-OFFSCREEN.png")); }
        foreach (var space in idle.Spaces)
        {
            foreach (var item in space.Items)
            {
                int before = host.Dispatcher.FakeLaunchCount;
                int x = (int)Math.Round((item.Bounds.X + 10) * idle.PixelsPerDip), y = (int)Math.Round((item.Bounds.Y + 10) * idle.PixelsPerDip);
                var hit = host.HitAt(x, y);
                Require(hit?.ItemId == item.Id, "wpf_item_identity");
                nint point = (y << 16) | (x & 0xffff);
                var beforeScene = host.SubmittedScene;
                SendMessageW(host.Window, 0x201, 1, point);
                Require(ReferenceEquals(beforeScene, host.SubmittedScene), "wpf_pinned_scene");
                SendMessageW(host.Window, 0x202, 0, point);
                Require(host.Dispatcher.FakeLaunchCount == before + 1 && host.Dispatcher.Controller.FocusOwner is null, "wpf_fake_click");
                SendMessageW(host.Window, 0x201, 1, point);
                SendMessageW(host.Window, 0x1F, 0, 0);
                SendMessageW(host.Window, 0x202, 0, point);
                Require(host.Dispatcher.FakeLaunchCount == before + 1, "wpf_cancel");
            }
        }
        idle = host.SubmittedScene!;
        // A new press can target the still-visible previous submission after safety reconciliation.
        // Pin that picture; a subsequent foreground identity change must cancel, not retarget it.
        host.Dispatcher.SetAvailability(DesktopAvailability.ExternalForeground, "external-before-down");
        var pinnedHit = new SurfaceHit(idle.Spaces[0].Id, idle.Spaces[0].Items[0].Id);
        host.Dispatcher.DispatchPointer(PointerAction.Down, pinnedHit);
        host.RenderHiddenTestFrame(true);
        Require(ReferenceEquals(idle, host.SubmittedScene), "wpf_reconciled_press_pin");
        host.Dispatcher.SetAvailability(DesktopAvailability.ExternalForeground, "external-after-down");
        host.RenderHiddenTestFrame(true);
        host.Dispatcher.DispatchPointer(PointerAction.Up, pinnedHit);
        Require(host.Dispatcher.FakeLaunchCount == 26 && !host.Dispatcher.HasGesture, "wpf_safety_cancel");
        host.Dispatcher.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-wpf-smoke");
        log("wpf_preview hidden=true distinct_items=26 fake_clicks=26 cancelled_clicks=26 pinned_scene=true");
        foreach (var id in new[] { EngineeringFixture.HoverId.ToString("D"), EngineeringFixture.ClickId.ToString("D") })
        {
            host.Dispatcher.DispatchPointer(PointerAction.Leave);
            var blank = new SurfaceHit(id);
            if (id == EngineeringFixture.HoverId.ToString("D"))
            {
                host.Dispatcher.DispatchPointer(PointerAction.Move, blank);
                clock.NowMilliseconds += 20;
            }
            else { host.Dispatcher.DispatchPointer(PointerAction.Down, blank); host.Dispatcher.DispatchPointer(PointerAction.Up, blank); }
            for (int frame = 0; frame <= 12; frame++)
            {
                host.RenderHiddenTestFrame(true); CheckRegion(host, renderer);
                if (frame == 6)
                {
                    var scene = host.SubmittedScene!;
                    var tile = scene.Spaces.Single(space => space.Id == id).Items.First();
                    var hit = scene.HitTest((int)Math.Round((tile.Bounds.X + 10) * scene.PixelsPerDip),
                        (int)Math.Round((tile.Bounds.Y + 10) * scene.PixelsPerDip));
                    Require(hit?.ItemId == tile.Id, "wpf_motion_item_hit");
                    int before = host.Dispatcher.FakeLaunchCount;
                    host.Dispatcher.DispatchPointer(PointerAction.Down, hit);
                    clock.NowMilliseconds += 10; host.RenderHiddenTestFrame(true);
                    Require(ReferenceEquals(scene, host.SubmittedScene) && !host.AnimationTimerArmed, "wpf_motion_press_pin");
                    host.Dispatcher.DispatchPointer(PointerAction.Up, hit);
                    Require(host.Dispatcher.FakeLaunchCount == before + 1, "wpf_motion_click_enabled");
                }
                clock.NowMilliseconds += 20;
            }
            clock.NowMilliseconds += SpaceDispatcher.MotionDurationMilliseconds;
            host.RenderHiddenTestFrame(true); CheckRegion(host, renderer);
            Require(!host.AnimationTimerArmed && host.SubmittedScene!.Spaces.Single(s => s.Id == id).Expanded, "wpf_expansion");
            if (evidenceDirectory is not null) { renderer.SaveOffscreen(Path.Combine(evidenceDirectory, id == EngineeringFixture.HoverId.ToString("D") ? "A-expanded-OFFSCREEN.png" : "B-expanded-OFFSCREEN.png")); }
            host.Dispatcher.Complete(new(id, host.Dispatcher.Controller.InputGeneration, host.Dispatcher.Controller.GetState(id).TransitionToken));
            host.Dispatcher.DispatchPointer(PointerAction.Down, new(id, Close: true));
            host.Dispatcher.DispatchPointer(PointerAction.Up, new(id, Close: true));
            host.RenderHiddenTestFrame(false);
            host.Dispatcher.Complete(new(id, host.Dispatcher.Controller.InputGeneration, host.Dispatcher.Controller.GetState(id).TransitionToken));
            host.RenderHiddenTestFrame(false); CheckRegion(host, renderer);
            Require(!host.AnimationTimerArmed && host.SubmittedScene!.Spaces.Single(s => s.Id == id).Bounds == ModernEngineeringFixture.SavedBounds(Guid.Parse(id)), "wpf_return");
        }
        if (evidenceDirectory is not null) { log("wpf_offscreen kind=existing_visual_tree_offscreen hidden=true live_window_screenshot=false presentation_UNVERIFIED"); }
        log("wpf_motion_click A_B=true synthetic_clock=true clicks_enabled=true pinned_submission=true presentation_UNVERIFIED");
        host.Dispatcher.HostLost(); host.RenderHiddenTestFrame(false);
        Require(!host.AnimationTimerArmed && !host.DeadlineTimerArmed && !host.Dispatcher.HasGesture && host.Dispatcher.Controller.KeyboardMode == KeyboardMode.NoKeyboardCapture, "wpf_hostlost");
        Require(GetForegroundWindow() == foreground, "wpf_foreground_changed");
        nint window = host.Window;
        // Exercise the actual WPF owner-thread message pump and its native close route.
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SendMessageW(window, 0x10, 0, 0)));
        Require(host.Run() == 0, "wpf_dispatcher_exit");
        host.Dispose();
        Require(!IsWindow(window) && renderer.Handle == 0 && !renderer.HasRootVisual && host.ActiveHookCount == 0 && !host.SessionRegistered, "wpf_cleanup");
        log($"wpf_renderer_cache created={renderer.Diagnostics.VisualCreated} reused={renderer.Diagnostics.VisualReused} removed={renderer.Diagnostics.VisualRemoved} live={renderer.Diagnostics.LiveVisuals} scenes={renderer.Diagnostics.SceneSubmitted} geometry_updates={renderer.Diagnostics.LayoutInvalidated} full_tree_populations={renderer.Diagnostics.FullTreeRebuild} last_submit_ticks={renderer.Diagnostics.FrameUpdateTicks} last_submit_allocated_bytes={renderer.Diagnostics.ManagedAllocation}");
        log("wpf_smoke hidden=true hwnd_single_owner=true region_hit_submitted_scene=true A_B=true reduced_motion=true idle_timers=0 hostlost=true foreground_unchanged=true source_visual_hook_session_released=true presentation_UNVERIFIED");
        CheckRollback(log);
        CheckFirstDown(log);
    }
    private static void CheckFirstDown(Action<string> log)
    {
        var clock = new Clock(); using var renderer = new WpfHostRenderer();
        using var host = new FallbackDesktopHost(log, null, () => new(0, 0, 1200, 800), clock, renderer: renderer);
        host.Start(false); SendMessageW(host.Window, 0x200, 0, 0);
        clock.NowMilliseconds = 300; host.RenderHiddenTestFrame(false);
        string id = EngineeringFixture.ClickId.ToString("D");
        host.Dispatcher.DispatchPointer(PointerAction.Down, new(id)); host.Dispatcher.DispatchPointer(PointerAction.Up, new(id));
        host.RenderHiddenTestFrame(true); clock.NowMilliseconds += 120; host.RenderHiddenTestFrame(true);
        var scene = host.SubmittedScene!; var item = scene.Spaces.Single(s => s.Id == id).Items.First();
        nint Point(SceneItem target) => ((int)((target.Bounds.Y + 10) * scene.PixelsPerDip) << 16) | ((int)((target.Bounds.X + 10) * scene.PixelsPerDip) & 0xffff);
        int before = host.Dispatcher.FakeLaunchCount;
        SendMessageW(host.Window, 0x201, 1, Point(item)); SendMessageW(host.Window, 0x202, 0, Point(item));
        Require(host.InputGuard.Conflicts > 0 && host.Dispatcher.FakeLaunchCount == before && !host.Dispatcher.HasGesture, "wpf_first_down_conflict");
        // A rejected Down may leave the process-owned HWND without its matching Up.
        // MouseLeave must release the rejection latch; a later Up cannot create a launch.
        SendMessageW(host.Window, 0x201, 1, Point(item));
        Require(host.RejectedPressActive, "wpf_rejected_press_latched");
        SendMessageW(host.Window, 0x2A3, 0, 0);
        Require(!host.RejectedPressActive && host.InputGuard.PinnedFrameId is null && !host.Dispatcher.HasGesture, "wpf_rejected_leave_clears_latch");
        SendMessageW(host.Window, 0x202, 0, Point(item));
        Require(host.Dispatcher.FakeLaunchCount == before, "wpf_rejected_leave_no_launch");
        var stable = scene.Spaces.Single(s => s.Id == ModernEngineeringFixture.StudyId.ToString("D")).Items.First();
        SendMessageW(host.Window, 0x201, 1, Point(stable));
        Require(host.Dispatcher.HasGesture && host.InputGuard.PinnedFrameId is not null && !host.AnimationTimerArmed, "wpf_first_down_stable_pin");
        var pinned = host.SubmittedScene; clock.NowMilliseconds += 10; host.RenderHiddenTestFrame(true);
        Require(ReferenceEquals(pinned, host.SubmittedScene), "wpf_guarded_pin_freezes_scene");
        SendMessageW(host.Window, 0x202, 0, Point(stable));
        Require(host.Dispatcher.FakeLaunchCount == before + 1, "wpf_guarded_launch_once");
        // Native cancellation must clear both the E1 gesture and the guard's pinned diagnostic.
        SendMessageW(host.Window, 0x201, 1, Point(stable));
        Require(host.Dispatcher.HasGesture && host.InputGuard.PinnedFrameId is not null, "wpf_cancel_start_pinned");
        SendMessageW(host.Window, 0x1F, 0, 0);
        Require(!host.Dispatcher.HasGesture && host.InputGuard.PinnedFrameId is null, "wpf_cancel_clears_pin");
        SendMessageW(host.Window, 0x202, 0, Point(stable));
        Require(host.Dispatcher.FakeLaunchCount == before + 1, "wpf_cancel_no_launch");
        SendMessageW(host.Window, 0x201, 1, Point(stable));
        Require(host.Dispatcher.HasGesture && host.InputGuard.PinnedFrameId is not null, "wpf_drag_start_pinned");
        var dragPoint = Point(stable) + 6;
        SendMessageW(host.Window, 0x200, 1, dragPoint);
        Require(!host.Dispatcher.HasGesture && host.InputGuard.PinnedFrameId is null, "wpf_drag_clears_pin");
        SendMessageW(host.Window, 0x202, 0, dragPoint);
        Require(host.Dispatcher.FakeLaunchCount == before + 1, "wpf_drag_no_launch");
        SendMessageW(host.Window, 0x201, 1, Point(stable));
        host.Dispatcher.SetAvailability(DesktopAvailability.ExternalForeground, "changed-after-press");
        SendMessageW(host.Window, 0x202, 0, Point(stable));
        Require(host.Dispatcher.FakeLaunchCount == before + 1, "wpf_guarded_stale_generation");
        host.Dispatcher.HostLost(); host.RenderHiddenTestFrame(false);
        Require(renderer.Diagnostics.LiveVisuals == 0 && renderer.SubmittedScene is null && host.InputGuard.RetainedScenes == 0, "wpf_hostlost_cache_clear");
        host.Dispatcher.HostRestored(); host.RenderHiddenTestFrame(false);
        Require(renderer.Diagnostics.LiveVisuals == 125, "wpf_cache_restore");
        log($"wpf_first_down hidden=true conflicts={host.InputGuard.Conflicts} accepted={host.InputGuard.Accepted} attempts={host.InputGuard.Attempts} generation_rejected={host.InputGuard.GenerationRejected} coverage_rejected={host.InputGuard.CoverageRejected} pinned=true old_generation_no_launch=true cache_hostlost_restore=true rejected_leave_resets=true native_cancel_and_drag_clear_pin=true presentation_UNVERIFIED");
    }
    private static void CheckRegion(FallbackDesktopHost host, WpfHostRenderer renderer)
    {
        Require(ReferenceEquals(host.SubmittedScene, renderer.SubmittedScene), "wpf_scene_identity");
        Require(renderer.MatchesSubmittedGeometry(host.SubmittedScene!), "wpf_visual_tree_geometry");
        nint region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            Require(region != 0 && GetWindowRgn(host.Window, region) != 0, "wpf_region");
            var scene = host.SubmittedScene!;
            foreach (var space in scene.Spaces)
            {
                int left = (int)Math.Round(space.Bounds.X * scene.PixelsPerDip), top = (int)Math.Round(space.Bounds.Y * scene.PixelsPerDip);
                int right = (int)Math.Round(space.Bounds.Right * scene.PixelsPerDip), bottom = (int)Math.Round(space.Bounds.Bottom * scene.PixelsPerDip);
                foreach (var (x, y) in new[] { (left, top), (right - 1, bottom - 1), (left - 1, top), (right, bottom) })
                { Require(PtInRegion(region, x, y) == (scene.HitTest(x, y) is not null), "wpf_region_hit"); }
            }
        }
        finally { if (region != 0) { DeleteObject(region); } }
    }
    private static void CheckRollback(Action<string> log)
    {
        foreach (string failure in new[] { "configure", "region", "submit", "workarea", "destroy" })
        {
            using var renderer = new WpfHostRenderer();
            using var submission = new SubmissionFailure(renderer);
            bool changed = false;
            using var host = new FallbackDesktopHost(log, () => { if (failure == "configure") { throw new InvalidOperationException("injected"); } },
                () => changed ? new(0, 0, 1000, 800) : new(0, 0, 1200, 800), renderer: submission);
            if (failure == "configure")
            {
                try { host.Start(false); throw new InvalidOperationException("rollback_not_exercised"); }
                catch (InvalidOperationException ex) when (ex.Message == "injected") { }
            }
            else
            {
                host.Start(false);
                nint window = host.Window;
                if (failure == "workarea") { changed = true; }
                else if (failure == "submit") { submission.Fail = true; }
                else if (failure == "destroy") { DestroyWindow(window); }
                else { host.FailNextHiddenFrame(failure); }
                if (failure != "destroy") { SendMessageW(window, 0x113, 2, 0); }
                Require(!IsWindow(window), "wpf_failure_window");
            }
            Require(renderer.Handle == 0 && host.ActiveHookCount == 0 && !host.SessionRegistered && !host.AnimationTimerArmed && !host.DeadlineTimerArmed, "wpf_rollback");
        }
        log("wpf_rollback configure_region_submit_workarea_external_destroy=5 resources_released=true");
    }
}
