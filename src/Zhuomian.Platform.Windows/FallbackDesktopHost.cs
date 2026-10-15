using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using static Zhuomian.Platform.Windows.NativeMethods;

namespace Zhuomian.Platform.Windows;

/// <summary>Restricted single-monitor Windows 11 engineering candidate, not accepted product hosting.</summary>
[SupportedOSPlatform("windows")]
public sealed class FallbackDesktopHost : IDisposable
{
    private sealed class Clock : IInteractionClock { private readonly Stopwatch _watch = Stopwatch.StartNew(); public long NowMilliseconds => _watch.ElapsedMilliseconds; }
    private const uint SignalMessage = 0x8001;
    private const uint CompletionMessage = 0x8002;
    private const uint HostStyle = 0x80000000;
    private const uint HostExtendedStyle = 0x08000080;
    private readonly string _className = "Zhuomian.E2." + Guid.NewGuid().ToString("N");
    private readonly WindowProcedure _procedure;
    private readonly EventProcedure _eventProcedure;
    private readonly nint _instance = GetModuleHandleW(null);
    private readonly HostLease _lease;
    private readonly IInteractionClock _clock;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Func<WorkAreaSnapshot?> _workAreaQuery;
    private readonly Func<bool?> _motionPreferenceQuery;
    private readonly bool _testMotionQuery;
    private WorkAreaGuard? _workAreaGuard;
    private readonly DeterministicInteractionScheduler _scheduler = new();
    private readonly Dictionary<int, VisualCompletion> _completions = [];
    private readonly Action<string> _log;
    private nint _window, _hook, _shell, _externallyDestroying;
    private uint _shellProcess, _taskbarMessage;
    private Rect _work;
    private MonitorWorkArea? _monitor;
    private bool _registered, _sessionRegistered, _ready, _disposed, _signalPending, _locked, _sleeping;
    private int _completionId;
    private bool _allowVisible;
    private int _exitCode;
    private string? _lastState;
    private readonly Action? _afterConfigure;
    private Point? _downPoint;
    private AppearanceEnvironment _appearanceEnvironment = new(reducedTransparency: true, reducedMotion: true);
    public FallbackDesktopHost(Action<string> log) : this(log, null) { }
    internal FallbackDesktopHost(Action<string> log, Action? afterConfigure, Func<WorkAreaSnapshot?>? workAreaQuery = null, IInteractionClock? clock = null, Func<bool?>? motionPreferenceQuery = null)
    {
        _clock = clock ?? new Clock();
        _motionPreferenceQuery = motionPreferenceQuery ?? ReadNativeAnimationPreference;
        _testMotionQuery = motionPreferenceQuery is not null;
        _afterConfigure = afterConfigure;
        _workAreaQuery = workAreaQuery ?? ReadNativeWorkArea;
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _procedure = WindowCallback;
        _eventProcedure = EventCallback;
        _lease = new(ReleaseWindow);
        Dispatcher = new(_clock, _scheduler);
    }
    private void Log(string message)
    {
        // A broken diagnostic sink cannot prevent fail-closed teardown or escape a native callback.
        try { _log(message); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
    private void StopFromCallback(string reason)
    {
        try { Stop(reason, 1); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _ready = false; _exitCode = 1; PostQuitMessage(1);
        }
    }
    public SpaceDispatcher Dispatcher { get; }
    public nint Window => _window;
    public int ActiveHookCount => _hook == 0 ? 0 : 1;
    internal bool AnimationTimerArmed { get; private set; }
    internal int AnimationFrameCount { get; private set; }
    private bool? _hiddenMotionPolicy;
    private string? _hiddenFrameFailure;
    internal void SetHiddenMotionPolicy(bool? allowMotion)
    {
        VerifyThread();
        if (_allowVisible) { throw new InvalidOperationException("hidden_test_only"); }
        _hiddenMotionPolicy = allowMotion;
    }
    internal void RenderHiddenTestFrame(bool allowMotion)
    {
        SetHiddenMotionPolicy(allowMotion);
        if (CheckWorkArea()) { UpdateVisual(); }
    }
    internal void FailNextHiddenFrame(string stage)
    {
        VerifyThread();
        if (_allowVisible) { throw new InvalidOperationException("hidden_test_only"); }
        _hiddenFrameFailure = stage;
    }
    internal bool DeadlineTimerArmed { get; private set; }
    internal bool SessionRegistered => _sessionRegistered;
    internal int PendingCompletionCount => _completions.Count;
    internal bool ClassRegistered => _registered;
    internal int ExitCode => _exitCode;
    internal string? StopReason { get; private set; }
    internal IReadOnlyList<string> LastPaintOrder { get; private set; } = [];
    internal int PaintCount { get; private set; }
    internal void RefreshHiddenTestFrame()
    {
        VerifyThread();
        if (_allowVisible) { throw new InvalidOperationException("hidden_test_only"); }
        UpdateVisual(false);
    }
    internal SurfaceHit? HitAt(int x, int y) => _ready ? HitTest(x, y) : null;
    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) { throw new InvalidOperationException("host_thread_mismatch"); }
    }
    public uint Dpi => _window == 0 ? 0 : GetDpiForWindow(_window);
    public void Start(bool visible = true)
    {
        VerifyThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (visible && (_testMotionQuery || _hiddenMotionPolicy is not null || _hiddenFrameFailure is not null)) { throw new InvalidOperationException("hidden_test_only"); }
        _allowVisible = visible;
        if (_registered || _window != 0) { throw new InvalidOperationException("host_already_started"); }
        if (!Environment.Is64BitProcess || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { throw new InvalidOperationException("unsupported_environment"); }
        SetProcessDpiAwarenessContext(-4);
        if (!AreDpiAwarenessContextsEqual(GetThreadDpiAwarenessContext(), -4)) { throw new InvalidOperationException("dpi_context_rejected"); }
        int count = 0;
        if (!EnumDisplayMonitors(0, 0, (_, _, _, _) => { count++; return true; }, 0) || count != 1) { throw new InvalidOperationException("single_monitor_required"); }
        if (WorkAreaGuard.Read(_workAreaQuery) is not { } initial)
        {
            _exitCode = 2; StopReason = "work_area_query_failed"; Log(StopReason);
            Dispose(); return;
        }
        _work = new(initial.Left, initial.Top, initial.Right, initial.Bottom);
        _workAreaGuard = new(initial, _workAreaQuery);
        try
        {
            var wc = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(),
                Style = 8,
                Procedure = _procedure,
                Instance = _instance,
                ClassName = _className,
                Cursor = LoadCursorW(0, 32512)
            };
            if (RegisterClassExW(wc) == 0) { throw new InvalidOperationException("class_registration_failed"); }
            _registered = true;
            _lease.Create(() => CreateWindowExW(HostExtendedStyle, _className, "Zhuomian E2-I2 engineering fixture",
                HostStyle, _work.Left, _work.Top, _work.Right - _work.Left, _work.Bottom - _work.Top, 0, 0, _instance, 0), Configure);
            _ready = true;
            Dispatcher.HostRestored();
            if (!Reconcile()) { return; }
            UpdateVisual();
            if (visible) { ShowWindow(_window, 4); PlaceBehindApplications(); }
            Log(FormattableString.Invariant($"host_started windows={Environment.OSVersion.Version.Build} monitors=1 dpi={Dpi} material=Solid motion_allowed={!_appearanceEnvironment.ReducedMotion} animation=continuous_geometry_candidate_or_reduced keyboard=disabled"));
        }
        catch { Dispose(); throw; }
    }
    private void Configure(nint window)
    {
        _window = window;
        uint dpi = GetDpiForWindow(window);
        if (dpi == 0) { throw new InvalidOperationException("dpi_unavailable"); }
        double scale = dpi / 96d;
        _monitor = new("engineering-primary", new(0, 0, (_work.Right - _work.Left) / scale, (_work.Bottom - _work.Top) / scale), new(scale));
        if (!SetWindowPos(window, 1, 0, 0, 0, 0, 0x13)) { throw new InvalidOperationException("initial_zorder_failed"); }
        _taskbarMessage = RegisterWindowMessageW("TaskbarCreated");
        if (_taskbarMessage == 0) { throw new InvalidOperationException("shell_notification_failed"); }
        _sessionRegistered = WTSRegisterSessionNotification(window, 0);
        if (!_sessionRegistered) { throw new InvalidOperationException("session_notification_failed"); }
        _hook = SetWinEventHook(3, 0x20, 0, _eventProcedure, 0, 0, 0);
        if (_hook == 0) { throw new InvalidOperationException("foreground_hook_failed"); }
        RefreshAppearance();
        _afterConfigure?.Invoke();
    }
    public int Run()
    {
        VerifyThread();
        if (!_ready) { return _disposed ? _exitCode : throw new InvalidOperationException("host_not_started"); }
        int result;
        while ((result = GetMessageW(out var message, 0, 0, 0)) > 0)
        {
            TranslateMessage(message);
            DispatchMessageW(message);
        }
        if (result < 0) { throw new InvalidOperationException("message_loop_failed"); }
        return _exitCode;
    }
    private void EventCallback(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time)
    {
        // Out-of-context WinEvent callbacks run on the registering message thread. No keyboard hook.
        try
        {
            if (!_ready || _disposed || _signalPending || eventId is not (3 or 0x16 or 0x20)) { return; }
            _signalPending = true;
            if (!PostMessageW(_window, SignalMessage, 0, 0)) { _signalPending = false; Stop("signal_dispatch_failed", 1); }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { StopFromCallback("native_event_callback_failed"); }
    }
    private nint WindowCallback(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == 0x21) { return 3; } // WM_MOUSEACTIVATE -> MA_NOACTIVATE, click is not eaten.
            if (message == 0x14) { return 1; } // Opaque invalidated region is fully painted.
            if (message == 0x0F) { PaintWindow(window); return 0; }
            if (!_ready) { return DefWindowProcW(window, message, wParam, lParam); }
            if (message == _taskbarMessage || message == SignalMessage)
            {
                _signalPending = false;
                if (!Reconcile()) { return 0; }
                UpdateVisual(); PlaceBehindApplications(); return 0;
            }
            switch (message)
            {
                case CompletionMessage:
                    if (_completions.Remove((int)wParam, out var completion))
                    {
                        if (!Reconcile()) { return 0; }
                        Dispatcher.Complete(completion); UpdateVisual();
                    }
                    return 0;
                case 0x113: // Independent deadline and motion timers; kill before re-evaluating authoritative state.
                    if (wParam is not (1 or 2)) { return 0; }
                    KillTimer(window, wParam);
                    if (wParam == 1) { DeadlineTimerArmed = false; }
                    else { AnimationTimerArmed = false; AnimationFrameCount++; }
                    if (!Reconcile()) { return 0; }
                    UpdateVisual(); return 0;
                case 0x200:
                case 0x201:
                case 0x202:
                case 0x203:
                    if (!Reconcile()) { return 0; }
                    int x = (short)((long)lParam & 0xffff), y = (short)(((long)lParam >> 16) & 0xffff);
                    var track = new TrackMouse { Size = (uint)Marshal.SizeOf<TrackMouse>(), Flags = 2, Window = window };
                    if (!TrackMouseEvent(ref track)) { Stop("pointer_tracking_failed", 1); return 0; }
                    var hit = HitTest(x, y);
                    bool held = (wParam & 0x13) != 0;
                    if (message == 0x201) { _downPoint = new Point { X = x, Y = y }; }
                    if (message == 0x200 && _downPoint is { } down && (Math.Abs(x - down.X) > 4 || Math.Abs(y - down.Y) > 4))
                    {
                        Dispatcher.DispatchPointer(PointerAction.Cancel); _downPoint = null;
                    }
                    Dispatcher.DispatchPointer(message switch { 0x201 => PointerAction.Down, 0x202 => PointerAction.Up, 0x203 => PointerAction.DoubleClick, _ => PointerAction.Move }, hit,
                        externalButtonHeld: message == 0x200 && held);
                    if (message is 0x202 or 0x203) { _downPoint = null; }
                    UpdateVisual();
                    if (Dispatcher.ExitRequested) { Stop("user_exit", 0); }
                    return 0;
                case 0x2A3: // No native capture: leaving cancels the pinned click safely.
                    if (!Reconcile()) { return 0; }
                    Dispatcher.DispatchPointer(PointerAction.Leave);
                    Dispatcher.DispatchPointer(PointerAction.Cancel);
                    _downPoint = null;
                    UpdateVisual(); return 0;
                case 0x1F:
                case 0x215:
                    if (!Reconcile()) { return 0; }
                    Dispatcher.DispatchPointer(PointerAction.Cancel); _downPoint = null; UpdateVisual(); return 0;
                case 0x2B1:
                    _locked = wParam is 7 or 3 or 4 || (wParam != 8 && _locked);
                    if (Reconcile()) { UpdateVisual(); }
                    return 0;
                case 0x218:
                    if (wParam == 4) { _sleeping = true; }
                    if (wParam is 7 or 18) { _sleeping = false; }
                    if (Reconcile()) { UpdateVisual(); }
                    return 1;
                case 0x15:
                case 0x1A:
                    RefreshAppearance(); if (Reconcile()) { UpdateVisual(); }
                    return 0;
                case 0x7E:
                case 0x2E0:
                    // Recreating a changed topology/DPI is not yet accepted. Fail closed, never reuse stale hit coordinates.
                    if (CheckWorkArea()) { Stop("display_changed_restart_required", 2); }
                    return 0;
                case 0x10:
                    Stop("user_exit", 0); return 0;
                case 0x02:
                    _externallyDestroying = window;
                    Stop("host_lost", 1); return 0;
            }
            return DefWindowProcW(window, message, wParam, lParam);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Do not propagate managed exceptions or private exception payloads across an unmanaged callback.
            StopFromCallback("native_callback_failed"); return 0;
        }
    }
    private static WorkAreaSnapshot? ReadNativeWorkArea()
    {
        int count = 0;
        if (!EnumDisplayMonitors(0, 0, (_, _, _, _) => { count++; return true; }, 0) || count != 1) { return null; }
        var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromPoint(default, 1), ref info)) { return null; }
        return new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }
    private bool CheckWorkArea()
    {
        if (!_ready) { return false; }
        var status = _workAreaGuard!.Check();
        if (status == WorkAreaStatus.Unchanged) { return true; }
        Stop(status == WorkAreaStatus.Changed ? "work_area_changed_restart_required" : "work_area_query_failed", 2);
        return false;
    }
    private bool Reconcile()
    {
        // Recheck on native input/timer/completion as well as settings/Shell notifications.
        // An input queued before a settings notification must not consume stale coordinates.
        if (!CheckWorkArea()) { return false; }
        if (!IsWindow(_window)) { Stop("host_invalid", 1); return false; }
        nint shell = GetShellWindow();
        if (GetWindowThreadProcessId(shell, out uint shellProcess) == 0) { shellProcess = 0; }
        if (shell != _shell || shellProcess != _shellProcess)
        {
            LoseInput(); _shell = shell; _shellProcess = shellProcess;
            if (shell != 0 && IsWindow(shell) && shellProcess != 0) { Dispatcher.HostRestored(); Log("shell_generation_changed safe=Idle_Disarmed_NoKeyboardCapture"); }
        }
        nint input = OpenInputDesktop(0, false, 1);
        bool accessible = input != 0;
        if (input != 0) { CloseDesktop(input); }
        nint foreground = GetForegroundWindow();
        if (GetWindowThreadProcessId(foreground, out uint process) == 0) { process = 0; }
        DesktopAvailability availability = DesktopAvailability.ExternalForeground;
        if (!accessible || _locked || _sleeping || shell == 0 || shellProcess == 0 || foreground == 0 || process == 0 || !IsWindow(foreground)) { availability = DesktopAvailability.Suspended; }
        else if (foreground == shell) { availability = DesktopAvailability.DesktopAvailable; }
        else if (foreground == _window) { availability = DesktopAvailability.Suspended; } // Host must never be foreground.
        else if (GetWindowRect(foreground, out var rect) && rect.Left <= _work.Left && rect.Top <= _work.Top &&
            rect.Right >= _work.Right && rect.Bottom >= _work.Bottom && ((long)GetWindowLongPtrW(foreground, -16) & 0x00C00000) == 0)
        { availability = DesktopAvailability.Suspended; }
        Dispatcher.SetAvailability(availability, foreground.ToString(CultureInfo.InvariantCulture) + ":" + process.ToString(CultureInfo.InvariantCulture));
        if (availability == DesktopAvailability.Suspended) { _completions.Clear(); _downPoint = null; }
        if (_allowVisible)
        {
            if (availability == DesktopAvailability.Suspended) { ShowWindow(_window, 0); }
            else if (!IsWindowVisible(_window)) { ShowWindow(_window, 4); }
        }
        PlaceBehindApplications();
        return true;
    }
    private void LoseInput()
    {
        Dispatcher.HostLost(); _completions.Clear(); _downPoint = null; DeadlineTimerArmed = false; AnimationTimerArmed = false;
        if (_window != 0) { KillTimer(_window, 1); KillTimer(_window, 2); }
        if (_window != 0 && GetCapture() == _window) { ReleaseCapture(); }
    }
    private void PlaceBehindApplications()
    {
        if (!_ready || !IsWindow(_window)) { return; }
        nint anchor = ZOrderPlacement.SelectAnchor(_window, _shell, Dispatcher.Controller.HostAvailable,
            Dispatcher.Controller.Availability, callback => EnumWindows((window, _) => callback(window), 0),
            window => ((long)GetWindowLongPtrW(window, -20) & 8) == 0 && IsWindowVisible(window) && !IsIconic(window) &&
                (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) != 0 || cloaked == 0));
        if (!SetWindowPos(_window, anchor, 0, 0, 0, 0, 0x13)) { throw new InvalidOperationException("zorder_failed"); }
    }
    private void UpdateVisual(bool? motionOverride = null)
    {
        if (!_ready || _monitor is null) { return; }
        if (!Dispatcher.Controller.HostAvailable || Dispatcher.Controller.Availability == DesktopAvailability.Suspended) { _completions.Clear(); }
        bool allowMotion = motionOverride ?? _hiddenMotionPolicy ?? EngineeringFixture.Resolve(EngineeringFixture.ClickId, true, _appearanceEnvironment).AllowMotion;
        var completions = Dispatcher.RenderTargets(_monitor, allowMotion);
        if (_hiddenFrameFailure == "region") { _hiddenFrameFailure = null; throw new InvalidOperationException("injected_region_failure"); }
        ApplyRegion();
        string stateSummary = string.Join(";", Dispatcher.Workspace.Spaces.Keys.Order().Select(id =>
            id.ToString("D") + "=" + Dispatcher.Controller.GetState(id.ToString("D")).VisualState)) +
            FormattableString.Invariant($";availability={Dispatcher.Controller.Availability};gate={Dispatcher.Controller.IntentGate};fake={Dispatcher.FakeLaunchCount};generation={Dispatcher.Controller.InputGeneration}");
        if (stateSummary != _lastState) { Log("state_changed " + stateSummary); _lastState = stateSummary; }
        if (_hiddenFrameFailure == "paint") { _hiddenFrameFailure = null; throw new InvalidOperationException("injected_paint_failure"); }
        int painted = PaintCount;
        if (!InvalidateRect(_window, 0, false) || !UpdateWindow(_window)) { throw new InvalidOperationException("frame_paint_failed"); }
        // Hidden HWNDs may not receive UpdateWindow painting. Exercise the same GDI path synchronously.
        if (_ready && PaintCount == painted) { SendMessageW(_window, 0x0F, 0, 0); }
        if (!_ready) { return; } // A nested paint callback may have failed closed.
        KillTimer(_window, 2); AnimationTimerArmed = false;
        if (Dispatcher.HasActiveAnimation && allowMotion)
        {
            if (SetTimer(_window, 2, 16, 0) == 0) { throw new InvalidOperationException("motion_schedule_failed"); }
            AnimationTimerArmed = true;
        }
        foreach (var completion in completions)
        {
            if (_completions.ContainsValue(completion)) { continue; }
            int id = checked(++_completionId);
            _completions.Add(id, completion);
            if (!PostMessageW(_window, CompletionMessage, (nuint)id, 0)) { throw new InvalidOperationException("completion_dispatch_failed"); }
        }
        KillTimer(_window, 1); DeadlineTimerArmed = false;
        if (_scheduler.NextDeadline is { } due)
        {
            uint wait = (uint)Math.Clamp(due - _clock.NowMilliseconds, 1, int.MaxValue);
            if (SetTimer(_window, 1, wait, 0) == 0) { throw new InvalidOperationException("deadline_schedule_failed"); }
            DeadlineTimerArmed = true;
        }
    }
    private Rect Pixels(LogicalRect rect)
    {
        double scale = _monitor!.Density.PixelsPerDip;
        return new((int)Math.Round(rect.X * scale), (int)Math.Round(rect.Y * scale), (int)Math.Round(rect.Right * scale), (int)Math.Round(rect.Bottom * scale));
    }
    private void ApplyRegion()
    {
        nint region = CreateRectRgn(0, 0, 0, 0);
        if (region == 0) { throw new InvalidOperationException("region_create_failed"); }
        try
        {
            foreach (var bounds in Dispatcher.VisualBounds.Values)
            {
                var r = Pixels(bounds);
                nint part = CreateRectRgn(r.Left, r.Top, r.Right, r.Bottom);
                if (part == 0) { throw new InvalidOperationException("region_create_failed"); }
                try { if (CombineRgn(region, region, part, 2) == 0) { throw new InvalidOperationException("region_combine_failed"); } }
                finally { DeleteObject(part); }
            }
            if (SetWindowRgn(_window, region, true) == 0) { throw new InvalidOperationException("region_apply_failed"); }
            region = 0; // User32 owns the region only after successful transfer.
        }
        finally { if (region != 0) { DeleteObject(region); } }
    }
    private SurfaceHit? HitTest(int x, int y)
    {
        double scale = _monitor!.Density.PixelsPerDip;
        double lx = x / scale, ly = y / scale;
        // Paint and hit test have identical Focus-first stacking and geometry.
        foreach (var pair in OrderedSpaces().Reverse())
        {
            var b = pair.Value;
            var pixels = Pixels(b);
            if (x < pixels.Left || x >= pixels.Right || y < pixels.Top || y >= pixels.Bottom) { continue; }
            double dx = lx - b.X, dy = ly - b.Y;
            if (dy < 34 && dx >= b.Width - 48) { return new(pair.Key, Exit: true); }
            if (dy < 34 && dx >= b.Width - 96) { return new(pair.Key, Close: true); }
            if (dx >= 16 && dx < b.Width - 16 && dy >= 54 && dy < 96) { return new(pair.Key, EngineeringFixture.ItemId.ToString("D")); }
            return new(pair.Key);
        }
        return null;
    }
    private IEnumerable<KeyValuePair<string, LogicalRect>> OrderedSpaces() => Dispatcher.VisualBounds.OrderBy(pair => pair.Key == Dispatcher.Controller.FocusOwner ? 1 : 0).ThenBy(pair => pair.Key, StringComparer.Ordinal);
    private static bool? ReadNativeAnimationPreference() => ReadSystemBoolean(0x1042, 0, out bool enabled, 0) ? enabled : null;
    private void RefreshAppearance()
    {
        var contrast = new HighContrast { Size = (uint)Marshal.SizeOf<HighContrast>() };
        bool known = SystemParametersInfoW(0x42, contrast.Size, ref contrast, 0);
        bool high = !known || (contrast.Flags & 1) != 0;
        ReadablePalette? palette = null;
        if (high)
        {
            try { palette = new(Color(GetSysColor(5)), Color(GetSysColor(8))); }
            catch (ArgumentException) { palette = new(new(0, 0, 0), new(255, 255, 255)); }
        }
        // SPI_GETCLIENTAREAANIMATION reads a BOOL; unknown settings and high contrast prohibit motion.
        bool? preference = MotionPreference.Read(_motionPreferenceQuery);
        _appearanceEnvironment = new(high, reducedTransparency: true, reducedMotion: !MotionPreference.Allow(preference, high), highContrastPalette: palette);
        Log($"motion_policy spi_known={preference is not null} client_animation={preference == true} high_contrast={high} reduced_motion={_appearanceEnvironment.ReducedMotion} parameters=engineering_unaccepted");
    }
    private static RgbColor Color(uint color) => new((int)(color & 255), (int)((color >> 8) & 255), (int)((color >> 16) & 255));
    private static uint ColorRef(RgbColor color) => (uint)(color.Red | color.Green << 8 | color.Blue << 16);
    private static void Fill(nint dc, Rect rect, RgbColor color)
    {
        nint brush = CreateSolidBrush(ColorRef(color));
        if (brush == 0) { throw new InvalidOperationException("brush_create_failed"); }
        try { if (FillRect(dc, rect, brush) == 0) { throw new InvalidOperationException("paint_failed"); } }
        finally { DeleteObject(brush); }
    }
    private void PaintWindow(nint window)
    {
        nint dc = BeginPaint(window, out var paint);
        if (dc == 0) { return; }
        try
        {
            if (_monitor is null) { return; }
            PaintCount++;
            nint font = CreateFontW(-(int)Math.Round(14 * _monitor.Density.PixelsPerDip), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            if (font == 0) { throw new InvalidOperationException("font_create_failed"); }
            nint previous = SelectObject(dc, font);
            try
            {
                if (SetBkMode(dc, 1) == 0) { throw new InvalidOperationException("text_background_failed"); }
                var frame = OrderedSpaces().ToArray();
                LastPaintOrder = frame.Select(pair => pair.Key).ToArray();
                foreach (var (id, bounds) in frame)
                {
                    var state = Dispatcher.Controller.GetState(id);
                    var appearance = EngineeringFixture.Resolve(Guid.Parse(id), state.VisualState is SpaceVisualState.Focusing or SpaceVisualState.Focused, _appearanceEnvironment);
                    var outer = Pixels(bounds);
                    Fill(dc, outer, appearance.ContentPalette.Foreground);
                    var inner = new Rect(outer.Left + 2, outer.Top + 2, outer.Right - 2, outer.Bottom - 2);
                    Fill(dc, inner, appearance.Effective.Tint);
                    // Resolver-required content backing is honored independently of decorative material.
                    Fill(dc, Pixels(new(bounds.X + 8, bounds.Y + 8, Math.Max(0, bounds.Width - 16), Math.Max(0, bounds.Height - 16))), appearance.ContentPalette.Background);
                    if (SetTextColor(dc, ColorRef(appearance.ContentPalette.Foreground)) == uint.MaxValue) { throw new InvalidOperationException("text_color_failed"); }
                    Text(dc, bounds, 12, 8, bounds.Width - 112, 28, Dispatcher.Workspace.Spaces[Guid.Parse(id)].Name);
                    Text(dc, bounds, bounds.Width - 94, 8, 46, 28, "Back");
                    Text(dc, bounds, bounds.Width - 46, 8, 44, 28, "Exit");
                    Text(dc, bounds, 16, 54, bounds.Width - 32, 42, "[ FAKE Preview / counter only ]");
                    Text(dc, bounds, 16, 104, bounds.Width - 32, bounds.Height - 112,
                        FormattableString.Invariant($"{state.VisualState} / {Dispatcher.Controller.IntentGate}\nFake clicks: {Dispatcher.FakeLaunchCount}\nSolid / NoActivate / Engineering motion\nMay obscure desktop icons.\nSearch, launch, drop disabled."));
                }
            }
            finally { SelectObject(dc, previous); DeleteObject(font); }
        }
        finally { EndPaint(window, paint); }
    }
    private void Text(nint dc, LogicalRect bounds, double x, double y, double width, double height, string text)
    {
        var rect = Pixels(new(bounds.X + x, bounds.Y + y, Math.Max(0, width), Math.Max(0, height)));
        if (rect.Bottom > rect.Top && rect.Right > rect.Left && DrawTextW(dc, text, text.Length, ref rect, 0x10 | 0x800) == 0) { throw new InvalidOperationException("text_paint_failed"); } // word wrap, literal ampersands.
    }
    private void Stop(string reason, int exitCode)
    {
        if (!_ready) { return; }
        _exitCode = exitCode; StopReason = reason; _ready = false;
        Log(reason);
        // Release on the message thread immediately; do not wait for a later input or Run iteration.
        try { Dispose(); }
        finally { PostQuitMessage(exitCode); }
    }
    private void ReleaseWindow(nint window)
    {
        // Also executes on configure rollback, before HostLease transfers ownership.
        bool unhooked = _hook == 0 || UnhookWinEvent(_hook);
        if (unhooked) { _hook = 0; }
        bool sessionReleased = !_sessionRegistered || WTSUnRegisterSessionNotification(window);
        if (sessionReleased) { _sessionRegistered = false; }
        if (window != _externallyDestroying && IsWindow(window) && !DestroyWindow(window)) { throw new InvalidOperationException("host_destroy_failed"); }
        _window = 0;
        if (!unhooked || !sessionReleased) { throw new InvalidOperationException("native_rollback_release_failed"); }
    }
    private void ReleaseClass()
    {
        // An external WM_DESTROY must finish before its window class can be unregistered.
        // The owning Run/using scope calls Dispose again after that callback unwinds.
        if (!_registered || (_externallyDestroying != 0 && IsWindow(_externallyDestroying))) { return; }
        if (UnregisterClassW(_className, _instance)) { _registered = false; }
        else { Log("class_release_failed"); }
    }
    public void Dispose()
    {
        VerifyThread();
        if (_disposed) { ReleaseClass(); return; }
        _disposed = true; _ready = false;
        LoseInput();
        bool unhooked = _hook == 0 || UnhookWinEvent(_hook);
        if (unhooked) { _hook = 0; }
        bool sessionReleased = !_sessionRegistered || WTSUnRegisterSessionNotification(_window);
        _sessionRegistered = false;
        try { _lease.Dispose(); }
        finally
        {
            ReleaseClass();
            Log($"host_closed windows_remaining={((IsWindow(_window) || IsWindow(_externallyDestroying)) ? 1 : 0)} hooks_remaining={ActiveHookCount} timers={((DeadlineTimerArmed ? 1 : 0) + (AnimationTimerArmed ? 1 : 0))} session_registered={_sessionRegistered} unhook_ok={unhooked} session_release_ok={sessionReleased}");
        }
        if (!unhooked || !sessionReleased) { throw new InvalidOperationException("native_release_failed"); }
    }
}
