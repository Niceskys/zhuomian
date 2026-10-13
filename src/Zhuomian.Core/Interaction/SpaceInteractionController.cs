namespace Zhuomian.Core.Interaction;

/// <summary>
/// Domain-only, single-dispatcher controller. Post all input observed in a tick before Drain;
/// Drain gives safety/input precedence over due timers. No Windows handles, animation, layout,
/// persistence or real resource execution. Every asynchronous completion is generation guarded.
/// </summary>
public sealed class SpaceInteractionController
{
    private sealed class SpaceRuntime(SpaceActivationPolicy policy)
    {
        internal SpaceActivationPolicy Policy { get; } = policy;
        internal SpaceVisualState State;
        internal InteractionActivity Activities;
        internal bool Inside;
        internal long HoverToken;
        internal long CollapseToken;
        internal long TransitionToken;
    }

    private sealed record Queued(SpaceInteractionEvent Event, long Epoch, long Sequence);
    private sealed record Timer(string? SpaceId, bool Collapse, long Due, long Epoch);
    private sealed record Click(string SpaceId, string? ItemId, long GestureId, long Epoch);
    private readonly Dictionary<string, SpaceRuntime> _spaces = new(StringComparer.Ordinal);
    private readonly HashSet<string> _invalidItems = new(StringComparer.Ordinal);
    private readonly List<Queued> _queue = [];
    private readonly Dictionary<long, Timer> _timers = [];
    private readonly List<InteractionOutcome> _outcomes = [];
    private readonly IInteractionClock _clock;
    private readonly IInteractionScheduler _scheduler;
    private readonly IInteractionLauncher _launcher;
    private readonly int _collapseDelayMilliseconds;
    private long _token;
    private long _epoch;
    private long _sequence;
    private long _lastGestureId;
    private long _armedToken;
    private long _lastDrainTime = -1;
    private bool _draining;
    private bool _itemClickTick;
    private bool _pendingHover;
    private bool _hostAvailable = true;
    private bool _dragActive;
    private bool _imeActive;
    private bool _modalOpen;
    private Click? _click;
    private string? _pendingFocus;
    private string? _foregroundIdentity;

    public SpaceInteractionController(
        IReadOnlyDictionary<string, SpaceActivationPolicy> spaces,
        IInteractionClock clock,
        IInteractionScheduler scheduler,
        IInteractionLauncher launcher,
        int collapseDelayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(spaces);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentOutOfRangeException.ThrowIfNegative(collapseDelayMilliseconds);
        foreach (var (id, policy) in spaces)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(id);
            ArgumentNullException.ThrowIfNull(policy);
            _spaces.Add(id, new(policy));
        }

        _clock = clock;
        _scheduler = scheduler;
        _launcher = launcher;
        _collapseDelayMilliseconds = collapseDelayMilliseconds;
    }

    public long InputGeneration => _epoch;
    public DesktopAvailability Availability { get; private set; } = DesktopAvailability.DesktopAvailable;
    public DesktopIntentGate IntentGate { get; private set; }
    public KeyboardMode KeyboardMode { get; private set; }
    public string? FocusOwner { get; private set; }
    public string SearchText { get; private set; } = "";
    public long KeyboardToken { get; private set; }
    public bool ImeActive => _imeActive;
    public bool ModalOpen => _modalOpen;
    public bool DragActive => _dragActive;
    public bool HostAvailable => _hostAvailable;
    public bool PointerCaptured => _click is not null ||
        _spaces.Values.Any(s => s.Activities.HasFlag(InteractionActivity.PointerCaptured));

    public SpaceInteractionState GetState(string spaceId)
    {
        var s = _spaces[spaceId];
        return new(s.State, s.Activities, s.Inside, s.TransitionToken);
    }

    public void Post(SpaceInteractionEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        _queue.Add(new(input, input.Generation ?? _epoch, ++_sequence));
    }

    /// <summary>Returns consumed/failure feedback; callers need not infer a rejected click as blank activation.</summary>
    public IReadOnlyList<InteractionOutcome> Drain()
    {
        if (_draining)
        {
            throw new InvalidOperationException("Drain is not reentrant.");
        }

        long now = _clock.NowMilliseconds;
        if (now < _lastDrainTime)
        {
            throw new InvalidOperationException("The interaction clock must be monotonic.");
        }

        _lastDrainTime = now;
        _draining = true;
        try
        {
            _outcomes.Clear();
            CollectDue(now);
            while (_queue.Count > 0)
            {
                var batch = _queue.OrderBy(q => Priority(q.Event.Kind)).ThenBy(q => q.Sequence).ToArray();
                _queue.Clear();
                _itemClickTick = _click?.ItemId is not null || batch.Any(q =>
                    q.Epoch == _epoch && q.Event.Kind == SpaceInteractionEventKind.PointerDown &&
                    q.Event.ItemId is not null);
                foreach (var q in batch)
                {
                    if (q.Epoch == _epoch || (IsSafety(q.Event.Kind) && q.Event.Generation is null))
                    {
                        Handle(q.Event);
                    }
                }

                // In particular, a zero-delay Hover runs only after this tick's input has been consumed.
                CollectDue(now);
            }

            return _outcomes.ToArray();
        }
        finally
        {
            _itemClickTick = false;
            _draining = false;
        }
    }

    private void CollectDue(long now)
    {
        foreach (var due in _scheduler.TakeDue(now))
        {
            Post(new(SpaceInteractionEventKind.Deadline, Token: due.Token));
        }
    }

    private static bool IsSafety(SpaceInteractionEventKind kind) => kind is
        SpaceInteractionEventKind.AvailabilityChanged or SpaceInteractionEventKind.ExternalInput or
        SpaceInteractionEventKind.HostLost or SpaceInteractionEventKind.HostRestored or
        SpaceInteractionEventKind.ItemInvalidated or SpaceInteractionEventKind.SpaceRemoved;

    private static int Priority(SpaceInteractionEventKind kind) => kind switch
    {
        _ when IsSafety(kind) => 0,
        SpaceInteractionEventKind.DragEnter or SpaceInteractionEventKind.DragEnd => 1,
        SpaceInteractionEventKind.ActivityChanged or SpaceInteractionEventKind.ModalOpened or
            SpaceInteractionEventKind.ModalClosed or SpaceInteractionEventKind.Escape => 2,
        SpaceInteractionEventKind.SearchActivate or SpaceInteractionEventKind.TextInput or
            SpaceInteractionEventKind.ImeStarted or SpaceInteractionEventKind.ImeEnded => 3,
        SpaceInteractionEventKind.Deadline => 9,
        _ => 4,
    };

    private bool Safe => _hostAvailable && Availability != DesktopAvailability.Suspended;
    private bool InputBlocked => !Safe || _dragActive || _imeActive || _modalOpen ||
        _spaces.Values.Any(s => (s.Activities & (InteractionActivity.Dragging |
            InteractionActivity.PointerCaptured | InteractionActivity.ContextMenuOpen)) != 0);

    private bool CanHover(SpaceRuntime s) => !InputBlocked && !_itemClickTick && _click is null &&
        (Availability != DesktopAvailability.ExternalForeground ||
            (_timers.TryGetValue(_armedToken, out var armed) && armed.Due > _clock.NowMilliseconds)) &&
        InteractionSafety.CanHoverEnterIntent(new(Availability, IntentGate, KeyboardMode,
            s.State, s.Activities), s.Policy.Mode);

    private void Handle(SpaceInteractionEvent e)
    {
        _spaces.TryGetValue(e.SpaceId ?? "", out var s);
        switch (e.Kind)
        {
            case SpaceInteractionEventKind.AvailabilityChanged:
                if (e.Availability != Availability || e.ForegroundIdentity != _foregroundIdentity)
                {
                    Availability = e.Availability;
                    _foregroundIdentity = e.ForegroundIdentity;
                    Preempt(!Safe);
                }
                break;
            case SpaceInteractionEventKind.ExternalInput:
                Preempt(false);
                break;
            case SpaceInteractionEventKind.HostLost:
                _hostAvailable = false;
                Preempt(true);
                break;
            case SpaceInteractionEventKind.HostRestored:
                _hostAvailable = true;
                Preempt(true);
                break;
            case SpaceInteractionEventKind.DragEnter:
                if (Safe)
                {
                    _dragActive = true;
                    CancelClick();
                    CancelHoverAndCollapse();
                    _pendingFocus = null;
                }
                break;
            case SpaceInteractionEventKind.DragEnd:
                _dragActive = false;
                // Drop, reject and cancellation all require a fresh enter before Hover can restart.
                foreach (var space in _spaces.Values) { space.Inside = false; }
                break;
            case SpaceInteractionEventKind.PointerEnter when s is not null:
                if (!InputBlocked)
                {
                    s.Inside = true;
                    Cancel(ref s.CollapseToken);
                    if (s.State is SpaceVisualState.Idle or SpaceVisualState.Hover && CanHover(s))
                    {
                        s.State = SpaceVisualState.Hover;
                        Cancel(ref s.HoverToken);
                        s.HoverToken = Schedule(e.SpaceId, false, s.Policy.HoverDelayMilliseconds);
                    }
                }
                break;
            case SpaceInteractionEventKind.PointerLeave when s is not null:
                s.Inside = false;
                Cancel(ref s.HoverToken);
                if (s.State == SpaceVisualState.Hover) { s.State = SpaceVisualState.Idle; }
                ScheduleCollapse(e.SpaceId!, s);
                break;
            case SpaceInteractionEventKind.PointerDown when s is not null:
                if (!InputBlocked && s.State != SpaceVisualState.Returning && e.GestureId > _lastGestureId)
                {
                    _lastGestureId = e.GestureId;
                    CancelClick();
                    CancelHoverAndCollapse();
                    _pendingFocus = null;
                    if (e.ClickCount == 1 && (e.ItemId is null || !_invalidItems.Contains(e.ItemId)))
                    {
                        _click = new(e.SpaceId!, e.ItemId, e.GestureId, _epoch);
                    }
                }
                break;
            case SpaceInteractionEventKind.PointerUp:
                CompleteClick(e);
                break;
            case SpaceInteractionEventKind.PointerCancel:
                if (_click?.GestureId == e.GestureId)
                {
                    CancelClick();
                    if (FocusOwner is { } clickOwner) { ScheduleCollapse(clickOwner, _spaces[clickOwner]); }
                }
                break;
            case SpaceInteractionEventKind.VisibleDesktopClick:
                if (!InputBlocked)
                {
                    Disarm();
                    IntentGate = DesktopIntentGate.Armed;
                    _armedToken = Schedule(null, false, 2000);
                }
                break;
            case SpaceInteractionEventKind.ExplicitExpand when s is not null:
                if (!InputBlocked && _click is null && (s.Policy.Mode == ActivationMode.Click || CanHover(s)))
                {
                    RequestFocus(e.SpaceId!);
                }
                break;
            case SpaceInteractionEventKind.Close:
                CancelClick();
                _pendingFocus = null;
                if (e.SpaceId == FocusOwner) { BeginReturn(); }
                break;
            case SpaceInteractionEventKind.TransitionCompleted:
                CompleteTransition(e);
                break;
            case SpaceInteractionEventKind.ActivityChanged when s is not null && Safe:
                s.Activities = e.Activities;
                if (e.SpaceId == FocusOwner && KeyboardMode == KeyboardMode.ZhuomianKeyboardActive)
                {
                    s.Activities |= InteractionActivity.Searching;
                }
                if (InputBlocked) { CancelClick(); CancelHoverAndCollapse(); }
                ScheduleCollapse(e.SpaceId!, s);
                break;
            case SpaceInteractionEventKind.ModalOpened:
                if (Safe)
                {
                    _modalOpen = true;
                    CancelClick();
                    CancelHoverAndCollapse();
                }
                break;
            case SpaceInteractionEventKind.ModalClosed:
                _modalOpen = false;
                if (FocusOwner is { } modalOwner) { ScheduleCollapse(modalOwner, _spaces[modalOwner]); }
                break;
            case SpaceInteractionEventKind.SearchActivate:
                if (!InputBlocked && _click is null && e.SpaceId == FocusOwner &&
                    s?.State == SpaceVisualState.Focused)
                {
                    KeyboardMode = KeyboardMode.ZhuomianKeyboardActive;
                    KeyboardToken = ++_token;
                    s.Activities |= InteractionActivity.Searching;
                    Cancel(ref s.CollapseToken);
                }
                break;
            case SpaceInteractionEventKind.TextInput:
                if (Safe && !_modalOpen && !_dragActive && KeyboardMode == KeyboardMode.ZhuomianKeyboardActive && e.SpaceId == FocusOwner &&
                    e.Token != 0 && e.Token == KeyboardToken)
                {
                    SearchText = e.Text;
                }
                break;
            case SpaceInteractionEventKind.ImeStarted:
                if (Safe && !_modalOpen && !_dragActive && KeyboardMode == KeyboardMode.ZhuomianKeyboardActive && e.SpaceId == FocusOwner &&
                    e.Token != 0 && e.Token == KeyboardToken)
                {
                    _imeActive = true;
                    CancelHoverAndCollapse();
                }
                break;
            case SpaceInteractionEventKind.ImeEnded when e.SpaceId == FocusOwner &&
                e.Token != 0 && e.Token == KeyboardToken:
                _imeActive = false;
                if (FocusOwner is { } owner) { ScheduleCollapse(owner, _spaces[owner]); }
                break;
            case SpaceInteractionEventKind.Escape:
                Escape();
                break;
            case SpaceInteractionEventKind.ItemInvalidated when e.ItemId is not null:
                _invalidItems.Add(e.ItemId);
                if (_click?.ItemId == e.ItemId) { CancelClick(); }
                break;
            case SpaceInteractionEventKind.SpaceRemoved when s is not null:
                if (_click?.SpaceId == e.SpaceId) { CancelClick(); }
                if (FocusOwner == e.SpaceId) { ResetSpace(s); FocusOwner = null; ResetKeyboard(); }
                if (_pendingFocus == e.SpaceId) { _pendingFocus = null; }
                Cancel(ref s.HoverToken);
                Cancel(ref s.CollapseToken);
                _spaces.Remove(e.SpaceId!);
                break;
            case SpaceInteractionEventKind.Deadline:
                HandleDeadline(e.Token);
                break;
                // LayoutChanged deliberately cannot retarget a pinned click or modify saved bounds.
        }
    }

    private long Schedule(string? spaceId, bool collapse, int delay)
    {
        long token = ++_token;
        long due = checked(_clock.NowMilliseconds + delay);
        _timers.Add(token, new(spaceId, collapse, due, _epoch));
        _scheduler.Schedule(new(token, due));
        return token;
    }

    private void Cancel(ref long token)
    {
        _scheduler.Cancel(token);
        _timers.Remove(token);
        token = 0;
    }

    private void HandleDeadline(long token)
    {
        if (!_timers.TryGetValue(token, out var timer) || timer.Epoch != _epoch ||
            _clock.NowMilliseconds < timer.Due) { return; }
        _timers.Remove(token);
        _scheduler.Cancel(token);
        if (token == _armedToken) { Disarm(); return; }
        if (timer.SpaceId is null || !_spaces.TryGetValue(timer.SpaceId, out var s)) { return; }
        if (timer.Collapse && token == s.CollapseToken)
        {
            s.CollapseToken = 0;
            if (CanCollapse(s) && FocusOwner == timer.SpaceId) { BeginReturn(); }
        }
        else if (token == s.HoverToken)
        {
            s.HoverToken = 0;
            if (s.Inside && CanHover(s))
            {
                s.State = SpaceVisualState.Intent;
                RequestFocus(timer.SpaceId, fromHover: true);
            }
        }
    }

    private void CompleteClick(SpaceInteractionEvent e)
    {
        if (_click is not { } click || click.GestureId != e.GestureId) { return; }
        _click = null; // Consume before calling an adapter, including failure/reentrant posts.
        if (InputBlocked || click.Epoch != _epoch || !e.ValidClick || e.SpaceId != click.SpaceId ||
            e.ItemId != click.ItemId || e.ClickCount != 1 ||
            (click.ItemId is not null && _invalidItems.Contains(click.ItemId)))
        {
            _outcomes.Add(new(InteractionOutcomeKind.ClickInvalidated, click.ItemId));
            return;
        }

        if (click.ItemId is { } item)
        {
            bool launched = _launcher.TryLaunch(item);
            _outcomes.Add(new(launched ? InteractionOutcomeKind.LaunchSucceeded :
                InteractionOutcomeKind.LaunchFailed, item));
        }
        else if (_spaces[click.SpaceId].Policy.Mode == ActivationMode.Click)
        {
            RequestFocus(click.SpaceId);
        }

        if (FocusOwner is { } owner) { ScheduleCollapse(owner, _spaces[owner]); }
    }

    private void RequestFocus(string id, bool fromHover = false)
    {
        if (!Safe || InputBlocked || _itemClickTick || _click is not null) { return; }
        if (FocusOwner == id)
        {
            if (_spaces[id].State == SpaceVisualState.Returning) { _pendingFocus = id; _pendingHover = fromHover; }
            return;
        }

        CancelHoverAndCollapse();
        if (FocusOwner is not null)
        {
            _pendingFocus = id;
            _pendingHover = fromHover;
            BeginReturn();
            return;
        }

        _pendingFocus = null;
        FocusOwner = id;
        var s = _spaces[id];
        s.State = SpaceVisualState.Focusing;
        s.TransitionToken = ++_token;
        Disarm();
    }

    private void BeginReturn()
    {
        if (FocusOwner is not { } owner) { return; }
        var s = _spaces[owner];
        if (s.State == SpaceVisualState.Returning) { return; }
        Cancel(ref s.CollapseToken);
        Cancel(ref s.HoverToken);
        s.Activities = InteractionActivity.None;
        s.State = SpaceVisualState.Returning;
        s.TransitionToken = ++_token;
        ResetKeyboard();
    }

    private void CompleteTransition(SpaceInteractionEvent e)
    {
        if (e.SpaceId != FocusOwner || FocusOwner is null) { return; }
        var s = _spaces[FocusOwner];
        if (e.Token == 0 || e.Token != s.TransitionToken) { return; }
        if (s.State == SpaceVisualState.Focusing)
        {
            s.State = SpaceVisualState.Focused;
            s.TransitionToken = 0;
            ScheduleCollapse(FocusOwner, s);
        }
        else if (s.State == SpaceVisualState.Returning)
        {
            ResetSpace(s);
            FocusOwner = null;
            string? pending = _pendingFocus;
            _pendingFocus = null;
            if (pending is not null && _spaces.TryGetValue(pending, out var next) &&
                (!_pendingHover || (next.Inside && CanHover(next))))
            {
                RequestFocus(pending, _pendingHover);
            }
        }
    }

    private bool CanCollapse(SpaceRuntime s) => Safe && !_dragActive && !_imeActive && !_modalOpen && _click is null &&
        !s.Inside && s.Policy.Mode == ActivationMode.Hover && s.State == SpaceVisualState.Focused &&
        !InteractionSafety.SuppressesAutoCollapse(new(Availability, IntentGate, KeyboardMode, s.State, s.Activities));

    private void ScheduleCollapse(string id, SpaceRuntime s)
    {
        Cancel(ref s.CollapseToken);
        if (CanCollapse(s)) { s.CollapseToken = Schedule(id, true, _collapseDelayMilliseconds); }
    }

    private void Disarm()
    {
        IntentGate = DesktopIntentGate.Disarmed;
        Cancel(ref _armedToken);
    }

    private void CancelClick()
    {
        if (_click is { } click) { _outcomes.Add(new(InteractionOutcomeKind.ClickInvalidated, click.ItemId)); }
        _click = null;
    }

    private void CancelHoverAndCollapse()
    {
        foreach (var s in _spaces.Values)
        {
            Cancel(ref s.HoverToken);
            Cancel(ref s.CollapseToken);
            if (s.State is SpaceVisualState.Hover or SpaceVisualState.Intent) { s.State = SpaceVisualState.Idle; }
        }
    }

    private void ResetKeyboard()
    {
        KeyboardMode = KeyboardMode.NoKeyboardCapture;
        KeyboardToken = 0;
        SearchText = "";
        _imeActive = false;
        foreach (var s in _spaces.Values) { s.Activities &= ~InteractionActivity.Searching; }
    }

    private void ResetSpace(SpaceRuntime s)
    {
        Cancel(ref s.HoverToken);
        Cancel(ref s.CollapseToken);
        s.State = SpaceVisualState.Idle;
        s.Activities = InteractionActivity.None;
        s.Inside = false;
        s.TransitionToken = 0;
    }

    private void Preempt(bool immediate)
    {
        ++_epoch;
        Disarm();
        CancelClick();
        CancelHoverAndCollapse();
        _pendingFocus = null;
        _dragActive = false;
        _modalOpen = false;
        ResetKeyboard();
        foreach (var s in _spaces.Values) { s.Activities = InteractionActivity.None; s.Inside = false; }
        if (immediate)
        {
            foreach (var s in _spaces.Values) { ResetSpace(s); }
            FocusOwner = null;
        }
        else { BeginReturn(); }
    }

    private void Escape()
    {
        bool handled = true;
        if (_modalOpen)
        {
            _modalOpen = false;
            if (FocusOwner is { } modalOwner) { ScheduleCollapse(modalOwner, _spaces[modalOwner]); }
        }
        else if (_spaces.FirstOrDefault(pair => pair.Value.Activities.HasFlag(InteractionActivity.ContextMenuOpen)).Key is { } owner)
        {
            _spaces[owner].Activities &= ~InteractionActivity.ContextMenuOpen;
            ScheduleCollapse(owner, _spaces[owner]);
        }
        else if (_dragActive || PointerCaptured || _spaces.Values.Any(s => s.Activities.HasFlag(InteractionActivity.Dragging)))
        {
            _dragActive = false;
            CancelClick();
            foreach (var s in _spaces.Values)
            {
                s.Activities &= ~(InteractionActivity.Dragging | InteractionActivity.PointerCaptured);
                s.Inside = false;
            }
        }
        else if (_imeActive)
        {
            // With no higher-priority layer, the IME owns Esc until composition ends.
            handled = false;
        }
        else if (KeyboardMode == KeyboardMode.ZhuomianKeyboardActive)
        {
            if (SearchText.Length > 0) { SearchText = ""; }
            else
            {
                ResetKeyboard();
                if (FocusOwner is { } id) { ScheduleCollapse(id, _spaces[id]); }
            }
        }
        else if (FocusOwner is not null)
        {
            _pendingFocus = null;
            BeginReturn();
        }
        else { handled = false; }
        _outcomes.Add(new(handled ? InteractionOutcomeKind.EscapeConsumed : InteractionOutcomeKind.EscapeUnhandled));
    }
}
