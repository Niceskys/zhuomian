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
        using var host = new FallbackDesktopHost(log, null, clock);
        host.Start(visible: false);
        host.Dispatcher.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-hidden-test");
        string id = EngineeringFixture.ClickId.ToString("D");
        var hit = new SurfaceHit(id);
        host.Dispatcher.DispatchPointer(PointerAction.Down, hit);
        host.Dispatcher.DispatchPointer(PointerAction.Up, hit);
        for (int frame = 0; frame <= 12; frame++)
        {
            clock.NowMilliseconds = frame * 20;
            host.RenderHiddenTestFrame(true);
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
    /// <summary>Actual hidden HWND checks; never injects mouse input, changes Shell or captures the desktop.</summary>
    public static void Run(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        CheckHiddenMotion(log);
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
        log(FormattableString.Invariant($"native_smoke hidden=true windows_build={Environment.OSVersion.Version.Build} dpi={host.Dpi} startup_ms={elapsed.Elapsed.TotalMilliseconds:F3} hwnd_valid=true noactivate=true topmost=false mouseactivate=3 foreground_unchanged=true hooks={host.ActiveHookCount}"));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        log(FormattableString.Invariant($"hidden_snapshot private_bytes={process.PrivateMemorySize64} working_set_bytes={process.WorkingSet64} handles={process.HandleCount} threads={process.Threads.Count} owned_windows=1 hooks={host.ActiveHookCount} evidence=CI_diagnostic_not_product_idle"));
        host.Dispose();
        if (IsWindow(window) || host.ActiveHookCount != 0 || host.Dispatcher.Controller.PointerCaptured || host.Dispatcher.Controller.FocusOwner is not null)
        { throw new InvalidOperationException("native_smoke_cleanup_failed"); }
        log("native_smoke_cleanup hwnd_destroyed=true hooks=0 pointer_capture=false focus_owner=false");
    }
}
