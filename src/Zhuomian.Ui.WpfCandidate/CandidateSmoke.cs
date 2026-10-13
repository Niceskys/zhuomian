using System.Windows.Media;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Platform.Windows;
using static Zhuomian.Ui.WpfCandidate.CandidateNative;

namespace Zhuomian.Ui.WpfCandidate;

internal static class CandidateSmoke
{
    internal sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    internal static void Require(bool value, string code) { if (!value) { throw new InvalidOperationException(code); } }
    internal static void Run(Action<string> log, string? outputDirectory = null)
    {
        nint oldDpi = SetThreadDpiAwarenessContext(-4);
        try
        {
            nint foreground = GetForegroundWindow();
            var clock = new Clock();
            using var window = new CandidateWindow(log, clock);
            window.Start(false); nint hwnd = window.Window;
            Require(IsWindow(hwnd), "create");
            long style = (long)GetWindowLongPtrW(hwnd, -20);
            Require((style & 0x08000080) == 0x08000080 && (style & 8) == 0, "styles");
            Require(SendMessageW(hwnd, 0x21, 0, 0) == 3, "mouseactivate");
            Require(SendMessageW(hwnd, 0x87, 0, 0) == 0, "no_dialog_keyboard_request");
            Require(window.HookCount == 1 && window.SessionRegistered, "native_ownership");
            Require(window.Surface.Scene!.Spaces.Length == 2, "two_spaces");
            string a = EngineeringFixture.HoverId.ToString("D"), b = EngineeringFixture.ClickId.ToString("D");
            var motion = new AppearanceEnvironment(reducedTransparency: true);
            var reduced = new AppearanceEnvironment(reducedTransparency: true, reducedMotion: true);
            var high = new AppearanceEnvironment(true, true, true, new(new(0, 0, 0), new(255, 255, 255)));
            window.Input.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-shell");
            window.Refresh(reduced);
            var tile = window.Surface.Scene.Spaces[0].Cells[0];
            var hit = window.HitAt((int)((tile.X + 20) * window.Scale), (int)((tile.Y + 20) * window.Scale));
            Require(hit?.ItemId == EngineeringFixture.ItemId.ToString("D"), "preview_hit");
            window.Input.DispatchPointer(PointerAction.Down, hit); window.Input.DispatchPointer(PointerAction.Up, hit);
            Require(window.Input.FakeLaunchCount == 1 && window.Input.Controller.FocusOwner is null, "preview_once");
            Capture(window, outputDirectory, "idle-offscreen.png");
            Click(window.Input, new(b)); window.Refresh(motion);
            var start = window.Input.VisualBounds[b]; clock.NowMilliseconds += 100; window.Refresh(motion);
            var middle = window.Input.VisualBounds[b];
            Require(middle.Width > start.Width && middle.Y > start.Y && window.AnimationTimerArmed, "continuous_b");
            Require(window.HitAt((int)((middle.X + 20) * window.Scale), (int)((middle.Y + 20) * window.Scale))?.SpaceId == b, "intermediate_region_hit");
            log(FormattableString.Invariant($"synthetic_b_frame x={middle.X} y={middle.Y} width={middle.Width} height={middle.Height}"));
            Click(window.Input, new(b, Close: true)); window.Refresh(motion);
            Require(window.Input.VisualBounds[b] == middle, "reverse_current_frame");
            window.Refresh(reduced); Require(!window.AnimationTimerArmed, "reduced_timer");
            window.Input.Complete(new(b, window.Input.Controller.InputGeneration, window.Input.Controller.GetState(b).TransitionToken));
            window.Refresh(reduced);
            window.Input.DispatchPointer(PointerAction.Move, new(a)); clock.NowMilliseconds += 20;
            window.Refresh(motion); clock.NowMilliseconds += 100; window.Refresh(motion);
            Require(window.Input.VisualBounds[a].Width > EngineeringFixture.SavedBounds(EngineeringFixture.HoverId).Width, "continuous_a");
            window.Refresh(reduced);
            window.Input.Complete(new(a, window.Input.Controller.InputGeneration, window.Input.Controller.GetState(a).TransitionToken));
            window.Refresh(reduced); Capture(window, outputDirectory, "expanded-offscreen.png");
            window.Refresh(high);
            Require(window.Surface.Scene.Spaces.All(space => space.Appearance.ActualMaterial == SurfaceMaterial.Solid && !space.Appearance.AllowMotion && space.Appearance.Effective.Opacity == 1), "high_contrast_policy");
            Capture(window, outputDirectory, "high-contrast-offscreen.png");
            Require(window.Input.Controller.KeyboardMode == KeyboardMode.NoKeyboardCapture, "no_keyboard");
            window.Input.HostLost(); window.Refresh(reduced);
            Require(!window.AnimationTimerArmed && !window.DeadlineTimerArmed, "lost_timers");
            window.Dispose(); window.Dispose();
            Require(!IsWindow(hwnd) && window.HookCount == 0 && !window.SessionRegistered && window.Surface.Scene is null && window.ExitCode == 0, "teardown");
            Require(GetForegroundWindow() == foreground, "foreground_preserved");
            using var failed = new CandidateWindow(log);
            bool rolledBack = false;
            try { failed.Start(false, () => throw new InvalidOperationException("injected_configuration_failure")); }
            catch (InvalidOperationException) { rolledBack = failed.Window == 0 && failed.HookCount == 0 && !failed.SessionRegistered; }
            Require(rolledBack, "configuration_rollback");
            log($"wpf_smoke_pass windows={Environment.OSVersion.Version.Build} tier={RenderCapability.Tier >> 16} hidden=true synthetic_input=true screen_capture=false product_performance=UNVERIFIED");
        }
        finally { if (oldDpi != 0) { SetThreadDpiAwarenessContext(oldDpi); } }
    }
    private static void Click(SpaceDispatcher input, SurfaceHit hit)
    { input.DispatchPointer(PointerAction.Down, hit); input.DispatchPointer(PointerAction.Up, hit); }
    private static void Capture(CandidateWindow window, string? directory, string name)
    {
        if (directory is null) { return; }
        window.Surface.SaveOffscreenRender(System.IO.Path.Combine(directory, name));
        Require(window.Surface.PaintCount > 0, "wpf_paint");
    }
}
