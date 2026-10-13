using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;
using static Zhuomian.Ui.WpfCandidate.CandidateNative;

namespace Zhuomian.Ui.WpfCandidate;

/// <summary>Independent candidate HWND. Does NOT replace or embed in FallbackDesktopHost.</summary>
internal sealed class CandidateWindow : IDisposable
{
    private sealed class Clock : IInteractionClock { private readonly Stopwatch _watch = Stopwatch.StartNew(); public long NowMilliseconds => _watch.ElapsedMilliseconds; }
    private readonly IInteractionClock _clock;
    private readonly DeterministicInteractionScheduler _scheduler = new();
    private readonly EventCallback _events;
    private readonly Action<string> _log;
    private readonly Dispatcher _owner = Dispatcher.CurrentDispatcher;
    private HwndSource? _source;
    private nint _hook, _shell;
    private uint _shellProcess;
    private bool _visible, _started, _disposed, _session, _locked, _sleeping, _signalPending;
    private Point? _down;
    private CandidateNative.Rect _work;
    private MonitorWorkArea? _monitor;
    private AppearanceEnvironment _environment = new(reducedTransparency: true, reducedMotion: true);
    internal SpaceDispatcher Input { get; }
    internal CandidateSurface Surface { get; } = new();
    internal nint Window => _source?.Handle ?? 0;
    internal bool AnimationTimerArmed { get; private set; }
    internal bool DeadlineTimerArmed { get; private set; }
    internal int HookCount => _hook == 0 ? 0 : 1;
    internal bool SessionRegistered => _session;
    internal int ExitCode { get; private set; }
    internal double Scale => _monitor?.Density.PixelsPerDip ?? 1;
    internal CandidateWindow(Action<string> log, IInteractionClock? clock = null)
    {
        _log = log; _clock = clock ?? new Clock(); Input = new(_clock, _scheduler);
        _events = (_, _, _, _, _, _, _) =>
        {
            if (_disposed || !_started || _signalPending) { return; }
            _signalPending = true;
            _owner.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                _signalPending = false;
                if (_disposed || !_started) { return; }
                try { Reconcile(); Refresh(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Stop(1); }
            }));
        };
    }
    internal void Start(bool visible = true, Action? afterConfigure = null)
    {
        _owner.VerifyAccess(); ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started || _source is not null) { throw new InvalidOperationException("candidate_already_started"); }
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || !Environment.Is64BitProcess) { throw new InvalidOperationException("unsupported_environment"); }
        if (!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), -4)) { throw new InvalidOperationException("per_monitor_v2_required"); }
        int count = 0;
        if (!EnumDisplayMonitors(0, 0, (_, _, _, _) => { count++; return true; }, 0) || count != 1) { throw new InvalidOperationException("single_monitor_required"); }
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromWindow(0, 1), ref info)) { throw new InvalidOperationException("work_area_failed"); }
        _work = info.Work; _visible = visible;
        try
        {
            _source = new(new HwndSourceParameters("Zhuomian E2-3 WPF candidate")
            {
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080,
                WindowClassStyle = 8,
                PositionX = _work.Left,
                PositionY = _work.Top,
                Width = _work.Right - _work.Left,
                Height = _work.Bottom - _work.Top,
                UsesPerPixelOpacity = false,
                AcquireHwndFocusInMenuMode = false,
                HwndSourceHook = Message
            });
            if (Window == 0 || !IsWindow(Window)) { throw new InvalidOperationException("hwnd_creation_failed"); }
            double scale = GetDpiForWindow(Window) / 96d;
            if (scale <= 0) { throw new InvalidOperationException("dpi_failed"); }
            _monitor = new("candidate-single", new(0, 0, (_work.Right - _work.Left) / scale, (_work.Bottom - _work.Top) / scale), new(scale));
            Surface.Width = _monitor.Bounds.Width; Surface.Height = _monitor.Bounds.Height;
            _source.CompositionTarget.BackgroundColor = Colors.Black;
            _source.RootVisual = Surface;
            _session = WTSRegisterSessionNotification(Window, 0);
            if (!_session) { throw new InvalidOperationException("session_registration_failed"); }
            _hook = SetWinEventHook(3, 0x20, 0, _events, 0, 0, 0);
            if (_hook == 0) { throw new InvalidOperationException("event_registration_failed"); }
            RefreshPolicy(); afterConfigure?.Invoke(); _started = true;
            Input.HostRestored(); Reconcile(); Refresh();
            _log(FormattableString.Invariant($"wpf_started windows={Environment.OSVersion.Version.Build} dpi={scale * 96} tier={RenderCapability.Tier >> 16} independent=true visible={visible}"));
        }
        catch { Dispose(); throw; }
    }
    private void RefreshPolicy()
    {
        bool known = SystemParametersInfoW(0x1042, 0, out int animated, 0);
        bool high = SystemParameters.HighContrast;
        var background = SystemColors.WindowColor; var foreground = SystemColors.WindowTextColor;
        ReadablePalette? palette = null;
        if (high)
        {
            try { palette = new(new(background.R, background.G, background.B), new(foreground.R, foreground.G, foreground.B)); }
            catch (ArgumentException) { palette = new(new(0, 0, 0), new(255, 255, 255)); }
        }
        _environment = new(high, true, !known || animated == 0, palette);
        _log($"policy high_contrast={high} reduced_motion={!known || animated == 0} transparency=Solid");
    }
    private void Reconcile()
    {
        nint shell = GetShellWindow();
        if (GetWindowThreadProcessId(shell, out uint shellProcess) == 0) { shellProcess = 0; }
        if (shell != _shell || shellProcess != _shellProcess)
        {
            Input.HostLost(); _shell = shell; _shellProcess = shellProcess;
            if (shell != 0 && shellProcess != 0 && IsWindow(shell)) { Input.HostRestored(); }
        }
        nint desktop = OpenInputDesktop(0, false, 1);
        bool accessible = desktop != 0;
        if (desktop != 0) { CloseDesktop(desktop); }
        nint foreground = GetForegroundWindow();
        if (GetWindowThreadProcessId(foreground, out uint process) == 0) { process = 0; }
        var availability = DesktopAvailability.ExternalForeground;
        if (!accessible || _locked || _sleeping || !IsWindow(shell) || shellProcess == 0 || foreground == 0 || process == 0 || !IsWindow(foreground) || foreground == Window)
        { availability = DesktopAvailability.Suspended; }
        else if (foreground == shell) { availability = DesktopAvailability.DesktopAvailable; }
        else if (GetWindowRect(foreground, out var rect) && rect.Left <= _work.Left && rect.Top <= _work.Top && rect.Right >= _work.Right && rect.Bottom >= _work.Bottom &&
            ((long)GetWindowLongPtrW(foreground, -16) & 0x00C00000) == 0) { availability = DesktopAvailability.Suspended; }
        Input.SetAvailability(availability, foreground.ToString(CultureInfo.InvariantCulture) + ":" + process.ToString(CultureInfo.InvariantCulture));
        if (availability == DesktopAvailability.Suspended) { _down = null; }
        // Conservative independent candidate: always HWND_BOTTOM. It may be hidden by Shell.
        // No attempt to claim the wallpaper/icon band or duplicate #31's raising strategy.
        if (_visible) { ShowWindow(Window, availability == DesktopAvailability.Suspended ? 0 : 4); }
        if (!SetWindowPos(Window, 1, 0, 0, 0, 0, 0x13)) { throw new InvalidOperationException("zorder_failed"); }
    }
    internal void Refresh(AppearanceEnvironment? hiddenEnvironment = null)
    {
        _owner.VerifyAccess();
        if (!_started || _disposed || _monitor is null) { return; }
        if (hiddenEnvironment is not null && _visible) { throw new InvalidOperationException("hidden_override_only"); }
        var environment = hiddenEnvironment ?? _environment;
        bool motion = CandidateScene.Resolve(EngineeringFixture.ClickId, true, environment).AllowMotion;
        var completed = Input.RenderTargets(_monitor, motion);
        var scene = CandidateScene.Create(Input, environment, Scale);
        ApplyRegion(scene); Surface.Present(scene); Surface.UpdateLayout();
        KillTimer(Window, 1); KillTimer(Window, 2);
        AnimationTimerArmed = motion && Input.HasActiveAnimation;
        DeadlineTimerArmed = _scheduler.NextDeadline is not null;
        if (AnimationTimerArmed && SetTimer(Window, 2, 16, 0) == 0) { throw new InvalidOperationException("motion_timer_failed"); }
        if (_scheduler.NextDeadline is { } due && SetTimer(Window, 1, (uint)Math.Clamp(due - _clock.NowMilliseconds, 1, int.MaxValue), 0) == 0)
        { throw new InvalidOperationException("deadline_timer_failed"); }
        foreach (var completion in completed)
        {
            _owner.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (_disposed || !_started) { return; }
                try { Reconcile(); Input.Complete(completion); Refresh(); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Stop(1); }
            }));
        }
    }
    private void ApplyRegion(CandidateScene scene)
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) { throw new InvalidOperationException("region_failed"); }
        try
        {
            foreach (var space in scene.Spaces)
            {
                var b = space.Bounds; int diameter = (int)Math.Round(space.Appearance.Effective.CornerRadius * Scale * 2);
                nint part = CreateRoundRectRgn((int)Math.Round(b.Left * Scale), (int)Math.Round(b.Top * Scale),
                    (int)Math.Round(b.Right * Scale) + 1, (int)Math.Round(b.Bottom * Scale) + 1, diameter, diameter);
                if (part == 0) { throw new InvalidOperationException("region_part_failed"); }
                try { if (CombineRgn(region, region, part, 2) == 0) { throw new InvalidOperationException("region_combine_failed"); } }
                finally { DeleteObject(part); }
            }
            if (SetWindowRgn(Window, region, true) == 0) { throw new InvalidOperationException("region_apply_failed"); }
            region = 0; // User32 owns successful Region.
        }
        finally { if (region != 0) { DeleteObject(region); } }
    }
    internal SurfaceHit? HitAt(int x, int y)
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) { throw new InvalidOperationException("hit_region_failed"); }
        try
        {
            if (GetWindowRgn(Window, region) == 0) { throw new InvalidOperationException("hit_region_read_failed"); }
            return PtInRegion(region, x, y) ? Surface.Scene?.Hit(x / Scale, y / Scale) : null;
        }
        finally { DeleteObject(region); }
    }
    private nint Message(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x21) { handled = true; return 3; }
        // Prevent WPF keyboard/text processing on this visual-only surface.
        if (message is >= 0x100 and <= 0x109 || message == 0x87) { handled = true; return 0; }
        if (!_started || _disposed) { return 0; }
        try
        {
            switch (message)
            {
                case 0x200:
                case 0x201:
                case 0x202:
                case 0x203:
                    handled = true; Reconcile();
                    int x = (short)((long)lParam & 0xffff), y = (short)(((long)lParam >> 16) & 0xffff);
                    var tracking = new TrackMouse { Size = (uint)Marshal.SizeOf<TrackMouse>(), Flags = 2, Window = hwnd };
                    if (!TrackMouseEvent(ref tracking)) { throw new InvalidOperationException("tracking_failed"); }
                    var hit = HitAt(x, y);
                    if (message == 0x201) { _down = new(x, y); }
                    if (message == 0x200 && _down is { } down && (Math.Abs(x - down.X) > 4 || Math.Abs(y - down.Y) > 4))
                    { Input.DispatchPointer(PointerAction.Cancel); _down = null; }
                    Input.DispatchPointer(message switch { 0x201 => PointerAction.Down, 0x202 => PointerAction.Up, 0x203 => PointerAction.DoubleClick, _ => PointerAction.Move }, hit,
                        message == 0x200 && ((long)wParam & 0x13) != 0);
                    if (message is 0x202 or 0x203) { _down = null; }
                    Surface.Hover = hit; Refresh(); if (Input.ExitRequested) { Stop(0); }
                    break;
                case 0x2A3:
                case 0x1F:
                case 0x215:
                    handled = true; Input.DispatchPointer(PointerAction.Leave); Input.DispatchPointer(PointerAction.Cancel);
                    _down = null; Surface.Hover = null; Refresh(); break;
                case 0x113:
                    if ((long)wParam is 1 or 2) { handled = true; KillTimer(hwnd, (nuint)wParam); Reconcile(); Refresh(); }
                    break;
                case 0x2B1:
                    _locked = (long)wParam is 7 or 3 or 4 || ((long)wParam != 8 && _locked); Reconcile(); Refresh(); break;
                case 0x218:
                    if ((long)wParam == 4) { _sleeping = true; }
                    if ((long)wParam is 7 or 18) { _sleeping = false; }
                    Reconcile(); Refresh(); break;
                case 0x15:
                case 0x1A:
                case 0x31A:
                    RefreshPolicy(); Reconcile(); Refresh(); break;
                case 0x7E: case 0x2E0: handled = true; Stop(2); break;
                case 0x10: handled = true; Stop(0); break;
                case 0x02: Stop(1); break;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { handled = true; Stop(1); }
        return 0;
    }
    private void Stop(int exitCode)
    {
        ExitCode = exitCode; Dispose();
        if (_visible) { _owner.BeginInvokeShutdown(DispatcherPriority.Send); }
    }
    public void Dispose()
    {
        _owner.VerifyAccess(); if (_disposed) { return; }
        _disposed = true; _started = false; Input.HostLost();
        nint window = Window;
        if (window != 0)
        {
            KillTimer(window, 1); KillTimer(window, 2);
            if (GetCapture() == window) { ReleaseCapture(); }
        }
        AnimationTimerArmed = false; DeadlineTimerArmed = false;
        bool hookReleased = _hook == 0 || UnhookWinEvent(_hook); _hook = 0;
        bool sessionReleased = !_session || WTSUnRegisterSessionNotification(window); _session = false;
        Surface.Release();
        if (_source is not null) { _source.RootVisual = null; _source.RemoveHook(Message); _source.Dispose(); _source = null; }
        _log($"wpf_closed destroyed={!IsWindow(window)} hook_released={hookReleased} session_released={sessionReleased} timers=0");
        if (!hookReleased || !sessionReleased || (window != 0 && IsWindow(window))) { ExitCode = 1; }
    }
}
