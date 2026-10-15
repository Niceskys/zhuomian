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
                var hit = idle.HitTest((int)Math.Round((item.Bounds.X + 10) * idle.PixelsPerDip), (int)Math.Round((item.Bounds.Y + 10) * idle.PixelsPerDip));
                Require(hit?.ItemId == item.Id, "wpf_item_identity");
                host.Dispatcher.DispatchPointer(PointerAction.Down, hit);
                host.RenderHiddenTestFrame(true);
                Require(ReferenceEquals(idle, host.SubmittedScene), "wpf_pinned_scene");
                host.Dispatcher.DispatchPointer(PointerAction.Up, hit);
                Require(host.Dispatcher.FakeLaunchCount == before + 1 && host.Dispatcher.Controller.FocusOwner is null, "wpf_fake_click");
                host.Dispatcher.DispatchPointer(PointerAction.Down, hit);
                host.Dispatcher.DispatchPointer(PointerAction.Cancel);
                host.Dispatcher.DispatchPointer(PointerAction.Up, hit);
                Require(host.Dispatcher.FakeLaunchCount == before + 1, "wpf_cancel");
            }
        }
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
                host.RenderHiddenTestFrame(true); CheckRegion(host, renderer); clock.NowMilliseconds += 20;
            }
            Require(host.SubmittedScene!.Spaces.Single(s => s.Id == id).Expanded, "wpf_expansion");
            if (evidenceDirectory is not null) { renderer.SaveOffscreen(Path.Combine(evidenceDirectory, id == EngineeringFixture.HoverId.ToString("D") ? "A-expanded-OFFSCREEN.png" : "B-expanded-OFFSCREEN.png")); }
            host.Dispatcher.Complete(new(id, host.Dispatcher.Controller.InputGeneration, host.Dispatcher.Controller.GetState(id).TransitionToken));
            host.Dispatcher.DispatchPointer(PointerAction.Down, new(id, Close: true));
            host.Dispatcher.DispatchPointer(PointerAction.Up, new(id, Close: true));
            host.RenderHiddenTestFrame(false);
            host.Dispatcher.Complete(new(id, host.Dispatcher.Controller.InputGeneration, host.Dispatcher.Controller.GetState(id).TransitionToken));
            host.RenderHiddenTestFrame(false); CheckRegion(host, renderer);
            Require(!host.AnimationTimerArmed && host.SubmittedScene!.Spaces.Single(s => s.Id == id).Bounds == ModernEngineeringFixture.SavedBounds(Guid.Parse(id)), "wpf_return");
        }
        host.Dispatcher.HostLost(); host.RenderHiddenTestFrame(false);
        Require(!host.AnimationTimerArmed && !host.DeadlineTimerArmed && !host.Dispatcher.HasGesture && host.Dispatcher.Controller.KeyboardMode == KeyboardMode.NoKeyboardCapture, "wpf_hostlost");
        Require(GetForegroundWindow() == foreground, "wpf_foreground_changed");
        nint window = host.Window;
        host.Dispose();
        Require(!IsWindow(window) && renderer.Handle == 0 && !renderer.HasRootVisual && host.ActiveHookCount == 0 && !host.SessionRegistered, "wpf_cleanup");
        log("wpf_smoke hidden=true hwnd_single_owner=true region_hit_submitted_scene=true A_B=true reduced_motion=true idle_timers=0 hostlost=true foreground_unchanged=true source_visual_hook_session_released=true presentation_UNVERIFIED");
        CheckRollback(log);
    }
    private static void CheckRegion(FallbackDesktopHost host, WpfHostRenderer renderer)
    {
        Require(ReferenceEquals(host.SubmittedScene, renderer.SubmittedScene), "wpf_scene_identity");
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
        foreach (string failure in new[] { "configure", "region", "paint", "workarea", "destroy" })
        {
            using var renderer = new WpfHostRenderer();
            bool changed = false;
            using var host = new FallbackDesktopHost(log, () => { if (failure == "configure") { throw new InvalidOperationException("injected"); } },
                () => changed ? new(0, 0, 1000, 800) : new(0, 0, 1200, 800), renderer: renderer);
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
