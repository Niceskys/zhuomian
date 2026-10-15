using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Platform.Windows;

public enum PointerAction { Move, Leave, Down, Up, Cancel, DoubleClick }
public readonly record struct SurfaceHit(string SpaceId, string? ItemId = null, bool Close = false, bool Exit = false);
public readonly record struct VisualCompletion(string SpaceId, long Generation, long Token);

/// <summary>Single-owner input adapter. All native callbacks marshal onto its owning message thread.</summary>
public sealed class SpaceDispatcher : IInteractionLauncher
{
    private sealed class InputFirstScheduler(DeterministicInteractionScheduler scheduler) : IInteractionScheduler
    {
        internal bool DeferDue;
        public void Schedule(InteractionDeadline deadline) => scheduler.Schedule(deadline);
        public void Cancel(long token) => scheduler.Cancel(token);
        public IReadOnlyList<InteractionDeadline> TakeDue(long nowMilliseconds) => DeferDue ? [] : scheduler.TakeDue(nowMilliseconds);
    }
    private sealed record Motion(VisualCompletion Completion, LogicalRect From, LogicalRect Target, long Started, bool Finished = false);
    // Geometry ownership only. E1 remains the sole authority for logical transitions and Focus.
    private readonly Dictionary<string, Motion> _motions = new(StringComparer.Ordinal);
    private readonly IInteractionClock _clock;
    private long? _motionPauseStarted;
    public const int MotionDurationMilliseconds = 240; // Engineering parameter, awaiting real acceptance.
    public bool HasActiveAnimation => Controller.HostAvailable && Controller.Availability != DesktopAvailability.Suspended && !HasGesture && !Controller.PointerCaptured && !Controller.DragActive && !_dragSuppressed && _motions.Values.Any(motion => !motion.Finished);
    private readonly InputFirstScheduler _inputScheduler;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Workspace _workspace;
    private readonly Dictionary<string, LogicalRect> _visual = new(StringComparer.Ordinal);
    private string? _inside;
    private SurfaceHit? _pressed;
    private long _gesture;
    private long _pressedGeneration;
    private bool _dragSuppressed;
    public SpaceDispatcher(IInteractionClock clock, DeterministicInteractionScheduler scheduler)
    {
        _clock = clock;
        _workspace = EngineeringFixture.Create();
        _inputScheduler = new(scheduler);
        Controller = new(_workspace.GetInteractionPolicies(), clock, _inputScheduler, this, collapseDelayMilliseconds: 120);
        foreach (var space in _workspace.Spaces.Values) { _visual.Add(space.Id.ToString("D"), EngineeringFixture.SavedBounds(space.Id)); }
    }
    public SpaceInteractionController Controller { get; }
    public Workspace Workspace => _workspace;
    public IReadOnlyDictionary<string, LogicalRect> VisualBounds => _visual;
    public int FakeLaunchCount { get; private set; }
    public bool ExitRequested { get; private set; }
    public bool HasGesture => _pressed is not null;
    public bool TryLaunch(string itemId)
    {
        VerifyThread();
        if (itemId != EngineeringFixture.ItemId.ToString("D")) { return false; }
        FakeLaunchCount++;
        return true;
    }
    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) { throw new InvalidOperationException("dispatcher_thread_mismatch"); }
    }
    public void SetAvailability(DesktopAvailability availability, string? identity)
    {
        VerifyThread();
        Controller.Post(new(SpaceInteractionEventKind.AvailabilityChanged, Availability: availability, ForegroundIdentity: identity));
        // Reconcile safety immediately, but keep due Hover behind this native message's actual input.
        _inputScheduler.DeferDue = true;
        try { Controller.Drain(); }
        finally { _inputScheduler.DeferDue = false; }
        if (_pressed is not null && _pressedGeneration != Controller.InputGeneration) { CancelGesture(); }
    }
    public void HostLost()
    {
        VerifyThread();
        Controller.Post(new(SpaceInteractionEventKind.HostLost));
        Controller.Drain();
        _motions.Clear();
        _motionPauseStarted = null;
        _inside = null;
        _pressed = null;
        _dragSuppressed = false;
        foreach (var space in _workspace.Spaces.Values) { _visual[space.Id.ToString("D")] = EngineeringFixture.SavedBounds(space.Id); }
    }
    public void HostRestored()
    {
        VerifyThread();
        Controller.Post(new(SpaceInteractionEventKind.HostRestored));
        Controller.Drain();
    }
    public void DispatchPointer(PointerAction action, SurfaceHit? hit = null, bool externalButtonHeld = false)
    {
        VerifyThread();
        if (!Controller.HostAvailable) { return; }
        if (externalButtonHeld && _pressed is null && !_dragSuppressed)
        {
            _dragSuppressed = true;
            Controller.Post(new(SpaceInteractionEventKind.DragEnter));
        }
        else if (!externalButtonHeld && _dragSuppressed)
        {
            _dragSuppressed = false;
            Controller.Post(new(SpaceInteractionEventKind.DragEnd));
        }
        string? next = action == PointerAction.Leave ? null : hit?.SpaceId;
        if (next != _inside)
        {
            if (_inside is { } old) { Controller.Post(new(SpaceInteractionEventKind.PointerLeave, old)); }
            _inside = next;
            if (next is not null) { Controller.Post(new(SpaceInteractionEventKind.PointerEnter, next)); }
        }
        switch (action)
        {
            case PointerAction.Down when hit is { } down:
                _pressed = down;
                _pressedGeneration = Controller.InputGeneration;
                Controller.Post(new(SpaceInteractionEventKind.PointerDown, down.SpaceId, down.ItemId, ++_gesture));
                break;
            case PointerAction.Up when _pressed is { } pressed:
                bool valid = hit == pressed && _pressedGeneration == Controller.InputGeneration;
                if (valid && pressed.Exit) { ExitRequested = true; }
                else if (valid && pressed.Close) { Controller.Post(new(SpaceInteractionEventKind.Close, pressed.SpaceId)); }
                else
                {
                    Controller.Post(new(SpaceInteractionEventKind.PointerUp, pressed.SpaceId, pressed.ItemId,
                        _gesture, ValidClick: valid, Generation: _pressedGeneration));
                    // Explicit intent only on a successful blank click; never from Preview or hover.
                    if (valid && pressed.ItemId is null) { Controller.Post(new(SpaceInteractionEventKind.VisibleDesktopClick)); }
                }
                _pressed = null;
                if (pressed.Close || pressed.Exit) { Controller.Post(new(SpaceInteractionEventKind.PointerCancel, GestureId: _gesture)); }
                break;
            case PointerAction.Leave:
            case PointerAction.Cancel:
            case PointerAction.DoubleClick:
                CancelGesture();
                break;
        }
        Controller.Drain();
    }
    private void CancelGesture()
    {
        _pressed = null;
        Controller.Post(new(SpaceInteractionEventKind.PointerCancel, GestureId: _gesture));
        Controller.Drain();
    }
    /// <summary>Samples one frame. Retargets start at the last displayed bounds, not a hidden future sample.
    /// Reduced Motion completes immediately; pinned clicks and external drag freeze ordinary motion.</summary>
    public IReadOnlyList<VisualCompletion> RenderTargets(MonitorWorkArea monitor, bool allowMotion = false)
    {
        VerifyThread();
        Controller.Drain();
        long now = _clock.NowMilliseconds;
        bool paused = HasGesture || Controller.PointerCaptured || Controller.DragActive || _dragSuppressed;
        if (paused) { _motionPauseStarted ??= now; }
        else if (_motionPauseStarted is not null)
        {
            foreach (var id in _motions.Keys.ToArray())
            {
                var motion = _motions[id];
                _motions[id] = motion with { From = _visual[id], Started = now };
            }
            _motionPauseStarted = null;
        }
        var completions = new List<VisualCompletion>();
        foreach (var space in _workspace.Spaces.Values)
        {
            string id = space.Id.ToString("D");
            var state = Controller.GetState(id);
            bool expand = state.VisualState is SpaceVisualState.Focusing or SpaceVisualState.Focused;
            var plan = SpaceLayoutEngine.Calculate(space, EngineeringFixture.SavedBounds(space.Id), monitor,
                EngineeringFixture.Expansion, expand ? LayoutTransitionDirection.Expand : LayoutTransitionDirection.Return, _visual[id]);
            if (state.VisualState is not (SpaceVisualState.Focusing or SpaceVisualState.Returning))
            {
                _motions.Remove(id);
                _visual[id] = plan.TargetBounds;
                continue;
            }
            var completion = new VisualCompletion(id, Controller.InputGeneration, state.TransitionToken);
            if (!allowMotion || !Controller.HostAvailable || Controller.Availability == DesktopAvailability.Suspended)
            {
                // Accessibility changes must not move a pinned click. No frame timer runs while held;
                // complete the nonanimated endpoint on release. Safety resets have already gone Idle.
                if (paused && Controller.HostAvailable && Controller.Availability != DesktopAvailability.Suspended) { continue; }
                _visual[id] = plan.TargetBounds;
                // Keep endpoint ownership until E1 consumes completion; restoring motion policy
                // must not start a zero-distance trajectory while that message is delayed.
                _motions[id] = new(completion, plan.TargetBounds, plan.TargetBounds, now, Finished: true);
                completions.Add(completion);
                continue;
            }
            bool retarget = !_motions.TryGetValue(id, out var motion) || motion.Completion != completion || motion.Target != plan.TargetBounds;
            if (retarget)
            {
                motion = new(completion, _visual[id], plan.TargetBounds, now, Finished: _visual[id] == plan.TargetBounds);
                _motions[id] = motion;
            }
            if (motion!.Finished) { completions.Add(completion); continue; }
            if (paused)
            {
                // Safety retargets still win, but held input must not move its pinned Item.
                continue;
            }
            double progress = Math.Clamp((now - motion!.Started) / (double)MotionDurationMilliseconds, 0, 1);
            _visual[id] = Interpolate(motion.From, motion.Target, progress);
            if (progress == 1)
            {
                _motions[id] = motion with { Finished = true };
                completions.Add(completion);
            }
        }
        return completions;
    }
    private static LogicalRect Interpolate(LogicalRect from, LogicalRect target, double progress)
    {
        if (progress == 1) { return target; }
        double eased = progress * progress * (3 - 2 * progress);
        return new(from.X + (target.X - from.X) * eased, from.Y + (target.Y - from.Y) * eased,
            from.Width + (target.Width - from.Width) * eased, from.Height + (target.Height - from.Height) * eased);
    }
    public void Complete(VisualCompletion completion)
    {
        VerifyThread();
        Controller.Post(new(SpaceInteractionEventKind.TransitionCompleted, completion.SpaceId,
            Token: completion.Token, Generation: completion.Generation));
        Controller.Drain();
    }
}
