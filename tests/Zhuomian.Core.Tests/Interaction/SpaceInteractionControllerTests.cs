using Zhuomian.Core.Interaction;

namespace Zhuomian.Core.Tests.Interaction;

public sealed class SpaceInteractionControllerTests
{
    private sealed class FakeClock : IInteractionClock
    {
        public long NowMilliseconds { get; set; }
    }

    // Deliberately capable of delivering cancelled deadlines, as a real dispatcher may do.
    private sealed class FakeScheduler : IInteractionScheduler
    {
        internal List<InteractionDeadline> Scheduled { get; } = [];
        private readonly DeterministicInteractionScheduler _inner = new();
        public void Schedule(InteractionDeadline deadline) { Scheduled.Add(deadline); _inner.Schedule(deadline); }
        public void Cancel(long token) => _inner.Cancel(token);
        public IReadOnlyList<InteractionDeadline> TakeDue(long nowMilliseconds) => _inner.TakeDue(nowMilliseconds);
        internal long? NextDeadline => _inner.NextDeadline;
    }

    private sealed class FakeLauncher : IInteractionLauncher
    {
        internal List<string> Calls { get; } = [];
        internal bool Succeeds { get; set; } = true;
        internal Action? OnLaunch { get; set; }
        public bool TryLaunch(string itemId) { Calls.Add(itemId); OnLaunch?.Invoke(); return Succeeds; }
    }

    private sealed class Harness(int hoverDelay = 20)
    {
        internal FakeClock Clock { get; } = new();
        internal FakeScheduler Scheduler { get; } = new();
        internal FakeLauncher Launcher { get; } = new();
        private SpaceInteractionController? _controller;
        internal SpaceInteractionController Controller => _controller ??= new(
            new Dictionary<string, SpaceActivationPolicy>
            {
                ["A"] = new(ActivationMode.Hover, hoverDelay),
                ["B"] = new(ActivationMode.Click),
                ["C"] = new(ActivationMode.Click),
            }, Clock, Scheduler, Launcher, collapseDelayMilliseconds: 30);

        internal IReadOnlyList<InteractionOutcome> Send(params SpaceInteractionEvent[] events)
        {
            foreach (var e in events)
            {
                Controller.Post(e.Kind is SpaceInteractionEventKind.TextInput or
                    SpaceInteractionEventKind.ImeStarted or SpaceInteractionEventKind.ImeEnded
                    ? e with { Token = Controller.KeyboardToken } : e);
            }
            return Controller.Drain();
        }

        internal void Advance(long milliseconds) { Clock.NowMilliseconds += milliseconds; Controller.Drain(); }
        internal SpaceInteractionState State(string id = "A") => Controller.GetState(id);
        internal void Complete(string id) => Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TransitionCompleted, id, Token: State(id).TransitionToken));
        internal void Expand(string id = "B") => Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ExplicitExpand, id));
        internal void Click(string space, string? item, long gesture = 1, int count = 1) => Send(
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, space, item, gesture, ClickCount: count),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, space, item, gesture, ClickCount: count));
        internal void External(string identity = "external-1") => Send(new SpaceInteractionEvent(SpaceInteractionEventKind.AvailabilityChanged, Availability: DesktopAvailability.ExternalForeground,
            ForegroundIdentity: identity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(120)]
    public void HoverHonorsConfiguredDeadlineAndNeverCapturesKeyboard(int delay)
    {
        var h = new Harness(delay);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        if (delay > 0)
        {
            h.Advance(delay - 1);
            Assert.Equal(SpaceVisualState.Hover, h.State().VisualState);
            h.Advance(1);
        }
        Assert.Equal(SpaceVisualState.Focusing, h.State().VisualState);
        Assert.Equal("A", h.Controller.FocusOwner);
        Assert.Equal(KeyboardMode.NoKeyboardCapture, h.Controller.KeyboardMode);
        h.Complete("A");
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        Assert.Empty(h.Launcher.Calls);
    }

    [Fact]
    public void PolicyDefaultsTo20AndRejectsOutOfRangeValues()
    {
        Assert.Equal(20, new SpaceActivationPolicy(ActivationMode.Hover).HoverDelayMilliseconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpaceActivationPolicy(ActivationMode.Hover, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpaceActivationPolicy(ActivationMode.Hover, 121));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpaceActivationPolicy((ActivationMode)99));
    }

    [Fact]
    public void PerSpaceModesDoNotImplicitlyActivateEachOther()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "B"));
        h.Advance(120);
        Assert.Equal(SpaceVisualState.Idle, h.State("B").VisualState);
        h.Click("A", null);
        Assert.Null(h.Controller.FocusOwner);
        h.Click("B", null, 2);
        Assert.Equal("B", h.Controller.FocusOwner);
        h.Complete("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, "B"));
        h.Advance(500);
        Assert.Equal(SpaceVisualState.Focused, h.State("B").VisualState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(120)]
    public void LeaveCancelsHoverEvenWhenDeadlineIsDueInSameTick(int delay)
    {
        var h = new Harness(delay);
        h.Controller.Post(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        h.Controller.Post(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, "A"));
        h.Controller.Drain();
        h.Advance(delay);
        Assert.Null(h.Controller.FocusOwner);
        Assert.Equal(SpaceVisualState.Idle, h.State().VisualState);
    }

    [Fact]
    public void ReentryUsesNewDeadlineAndRejectsCancelledOrEarlyTimers()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        var old = h.Scheduler.Scheduled[^1];
        h.Advance(10);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, "A"), new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        var current = h.Scheduler.Scheduled[^1];
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Deadline, Token: current.Token));
        h.Advance(10);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Deadline, Token: old.Token));
        Assert.Equal(SpaceVisualState.Hover, h.State().VisualState);
        h.Advance(10);
        Assert.Equal(SpaceVisualState.Focusing, h.State().VisualState);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(20, 19)]
    [InlineData(20, 20)]
    [InlineData(20, 21)]
    [InlineData(120, 120)]
    public void ItemClickWinsBeforeAtAndAfterHoverDeadline(int delay, int at)
    {
        var h = new Harness(delay);
        if (delay == 0) { h.Controller.Post(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A")); }
        else { h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A")); }
        h.Clock.NowMilliseconds = at;
        h.Click("A", "item-1");
        h.Advance(500);
        Assert.Equal("item-1", Assert.Single(h.Launcher.Calls));
        Assert.Null(h.Controller.FocusOwner);
    }

    [Fact]
    public void AlreadyFocusingItemStillLaunchesOnFirstClick()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        h.Advance(20);
        Assert.Equal(SpaceVisualState.Focusing, h.State().VisualState);
        h.Click("A", "item-1");
        Assert.Single(h.Launcher.Calls);
        h.Complete("A");
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("B")]
    public void PointerDownPinsItemAcrossLayoutChangesAndSuppressesHover(string space)
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, space),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, space, "item-1", 1));
        h.Advance(120);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.LayoutChanged, space));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, space, "item-1", 1));
        Assert.Equal("item-1", Assert.Single(h.Launcher.Calls));
        Assert.Null(h.Controller.FocusOwner);
        Assert.False(h.Controller.PointerCaptured);
    }

    [Fact]
    public void ItemPriorityConsumesCompetingBlankActivationAndZeroDelayEnter()
    {
        var h = new Harness(0);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ExplicitExpand, "B"),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "A", "item-1", 1),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "A", "item-1", 1),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        Assert.Single(h.Launcher.Calls);
        Assert.Null(h.Controller.FocusOwner);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("item-2")]
    public void ChangedUpHitCannotFallThroughToBlankOrAnotherItem(string? upItem)
    {
        var h = new Harness();
        var outcomes = h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", "item-1", 1),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", upItem, 1));
        Assert.Empty(h.Launcher.Calls);
        Assert.Null(h.Controller.FocusOwner);
        Assert.Contains(outcomes, o => o.Kind == InteractionOutcomeKind.ClickInvalidated);
    }

    [Fact]
    public void DoubleClickDuplicateMessagesAndReentrantLauncherCannotRepeatLaunch()
    {
        var h = new Harness();
        h.Launcher.OnLaunch = () => h.Controller.Post(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", "item-1", 1));
        h.Click("B", "item-1");
        h.Click("B", "item-1");
        h.Click("B", "item-1", 2, 2);
        Assert.Single(h.Launcher.Calls);
        h.Click("B", "item-1", 3);
        Assert.Equal(2, h.Launcher.Calls.Count); // Independent repeat clicks remain immediate.
    }

    [Fact]
    public void FailedLaunchIsConsumedAndProducesFailureFeedback()
    {
        var h = new Harness();
        h.Launcher.Succeeds = false;
        var feedback = h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", "item-1", 1),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", "item-1", 1));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", "item-1", 1));
        Assert.Single(h.Launcher.Calls);
        Assert.Contains(feedback, o => o.Kind == InteractionOutcomeKind.LaunchFailed);
        Assert.Null(h.Controller.FocusOwner);
    }

    [Theory]
    [InlineData(SpaceInteractionEventKind.PointerCancel)]
    [InlineData(SpaceInteractionEventKind.ItemInvalidated)]
    [InlineData(SpaceInteractionEventKind.SpaceRemoved)]
    [InlineData(SpaceInteractionEventKind.ExternalInput)]
    [InlineData(SpaceInteractionEventKind.HostLost)]
    [InlineData(SpaceInteractionEventKind.DragEnter)]
    public void InvalidatedClickNeverLaunchesOrActivatesSpace(SpaceInteractionEventKind invalidation)
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", "item-1", 1));
        var feedback = h.Send(new SpaceInteractionEvent(invalidation, "B", "item-1", 1),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", "item-1", 1));
        Assert.Empty(h.Launcher.Calls);
        Assert.Null(h.Controller.FocusOwner);
        Assert.Contains(feedback, o => o.Kind == InteractionOutcomeKind.ClickInvalidated);
    }

    [Fact]
    public void ExistingExternalForegroundAllowsPreviewButChangedIdentityInvalidatesIt()
    {
        var h = new Harness();
        h.External();
        h.Click("A", "item-1");
        Assert.Single(h.Launcher.Calls);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", "item-2", 2));
        h.External("external-2");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", "item-2", 2));
        Assert.Single(h.Launcher.Calls);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", "item-3", 3));
        h.External("external-2"); // An unchanged classification must not cancel the gesture.
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", "item-3", 3));
        Assert.Equal(2, h.Launcher.Calls.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(120)]
    public void ExternalDisarmedNeverAutoExpandsIn100Attempts(int delay)
    {
        var h = new Harness(delay);
        h.External();
        for (int i = 0; i < 100; i++)
        {
            h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
            h.Advance(120);
            Assert.Equal(SpaceVisualState.Idle, h.State().VisualState);
            h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, "A"));
        }
        Assert.Null(h.Controller.FocusOwner);
    }

    [Fact]
    public void ArmedAllowsHoverIsConsumedAndExpiresAt2000()
    {
        var h = new Harness();
        h.External();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.VisibleDesktopClick));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        h.Advance(20);
        Assert.Equal("A", h.Controller.FocusOwner);
        Assert.Equal(DesktopIntentGate.Disarmed, h.Controller.IntentGate);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Close, "A"));
        h.Complete("A");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.VisibleDesktopClick));
        h.Advance(1999);
        Assert.Equal(DesktopIntentGate.Armed, h.Controller.IntentGate);
        h.Advance(1);
        Assert.Equal(DesktopIntentGate.Disarmed, h.Controller.IntentGate);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        h.Advance(20);
        Assert.Null(h.Controller.FocusOwner);
    }

    [Fact]
    public void HoverCannotConsumeArmedAtItsExpiryAndOldExpiryCannotDisarmRenewal()
    {
        var h = new Harness();
        h.External();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.VisibleDesktopClick));
        long old = h.Scheduler.Scheduled[^1].Token;
        h.Advance(1980);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        h.Advance(20);
        Assert.Null(h.Controller.FocusOwner);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.VisibleDesktopClick));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Deadline, Token: old));
        Assert.Equal(DesktopIntentGate.Armed, h.Controller.IntentGate);
    }

    [Theory]
    [InlineData(SpaceInteractionEventKind.ExternalInput)]
    [InlineData(SpaceInteractionEventKind.HostLost)]
    [InlineData(SpaceInteractionEventKind.AvailabilityChanged)]
    public void SafetyDisarmsCancelsHoverAndRejectsQueuedClickInSameTick(SpaceInteractionEventKind kind)
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.VisibleDesktopClick), new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        long old = h.Scheduler.Scheduled[^1].Token;
        h.Clock.NowMilliseconds = 20;
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "A", "item-1", 1),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "A", "item-1", 1),
            new SpaceInteractionEvent(kind, Availability: DesktopAvailability.Suspended));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Deadline, Token: old));
        Assert.Equal(DesktopIntentGate.Disarmed, h.Controller.IntentGate);
        Assert.Null(h.Controller.FocusOwner);
        Assert.Empty(h.Launcher.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SafetyPreemptsBothFocusingAndFocused(bool complete)
    {
        var h = new Harness();
        h.Expand();
        long obsolete = h.State("B").TransitionToken;
        if (complete) { h.Complete("B"); }
        h.External();
        Assert.Equal(SpaceVisualState.Returning, h.State("B").VisualState);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TransitionCompleted, "B", Token: obsolete));
        Assert.Equal(SpaceVisualState.Returning, h.State("B").VisualState);
        h.Complete("B");
        Assert.Equal(SpaceVisualState.Idle, h.State("B").VisualState);
        Assert.Null(h.Controller.FocusOwner);
    }

    [Theory]
    [InlineData(SpaceInteractionEventKind.HostLost)]
    [InlineData(SpaceInteractionEventKind.AvailabilityChanged)]
    public void SuspensionOrHostLossClearsAllInputAndTokensAndRecoveryIsSafe(SpaceInteractionEventKind kind)
    {
        var h = new Harness();
        h.Expand(); h.Complete("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, "B"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TextInput, "B", Text: "search"),
            new SpaceInteractionEvent(SpaceInteractionEventKind.ImeStarted, "B"));
        h.Send(new SpaceInteractionEvent(kind, Availability: DesktopAvailability.Suspended));
        Assert.Null(h.Controller.FocusOwner);
        Assert.Equal(SpaceVisualState.Idle, h.State("B").VisualState);
        Assert.Equal(KeyboardMode.NoKeyboardCapture, h.Controller.KeyboardMode);
        Assert.False(h.Controller.PointerCaptured);
        Assert.False(h.Controller.ImeActive);
        Assert.Equal("", h.Controller.SearchText);
        h.Expand(); h.Click("B", "item-1");
        Assert.Null(h.Controller.FocusOwner);
        Assert.Empty(h.Launcher.Calls);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.HostRestored));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.AvailabilityChanged));
        Assert.Null(h.Controller.FocusOwner);
        Assert.Equal(DesktopIntentGate.Disarmed, h.Controller.IntentGate);
        Assert.Null(h.Scheduler.NextDeadline);
    }

    [Fact]
    public void SwitchingWaitsForReturnAndLatestRequestWinsIn500Switches()
    {
        var h = new Harness();
        h.Expand("B");
        for (int i = 0; i < 500; i++)
        {
            string previous = h.Controller.FocusOwner!;
            string next = previous == "B" ? "C" : "B";
            long stale = h.State(previous).TransitionToken;
            h.Expand(next);
            Assert.Equal(previous, h.Controller.FocusOwner);
            Assert.Equal(SpaceVisualState.Returning, h.State(previous).VisualState);
            h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TransitionCompleted, previous, Token: stale));
            Assert.Equal(previous, h.Controller.FocusOwner);
            h.Complete(previous);
            Assert.Equal(next, h.Controller.FocusOwner);
            Assert.Equal(SpaceVisualState.Idle, h.State(previous).VisualState);
            h.Complete(next);
            Assert.Equal(SpaceVisualState.Focused, h.State(next).VisualState);
        }
        h.Expand("A"); h.Expand("C");
        string owner = h.Controller.FocusOwner!;
        h.Complete(owner);
        Assert.Equal("C", h.Controller.FocusOwner);
    }

    [Fact]
    public void CloseDuringSwitchClearsPendingAndRepeatedCloseKeepsReturnToken()
    {
        var h = new Harness();
        h.Expand("B"); h.Expand("C");
        long token = h.State("B").TransitionToken;
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Close, "B"), new SpaceInteractionEvent(SpaceInteractionEventKind.Close, "B"));
        Assert.Equal(token, h.State("B").TransitionToken);
        h.Complete("B");
        Assert.Null(h.Controller.FocusOwner);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TransitionCompleted, "B", Token: token));
        Assert.Null(h.Controller.FocusOwner);
    }

    [Theory]
    [InlineData(SpaceInteractionEventKind.DragEnd)]
    [InlineData(SpaceInteractionEventKind.Escape)]
    public void ExternalDragCancelsHoverAndClickUntilFreshEnter(SpaceInteractionEventKind end)
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", "item-1", 1));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.DragEnter));
        h.Advance(120);
        h.Click("B", "item-1", 2); h.Expand();
        Assert.Empty(h.Launcher.Calls);
        Assert.Null(h.Controller.FocusOwner);
        h.Send(new SpaceInteractionEvent(end));
        h.Advance(120);
        Assert.Null(h.Controller.FocusOwner);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        h.Advance(19);
        Assert.Null(h.Controller.FocusOwner);
        h.Advance(1);
        Assert.Equal("A", h.Controller.FocusOwner);
    }

    [Fact]
    public void DragKeepsExistingFocusStableButSafetyStillPreempts()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A")); h.Advance(20); h.Complete("A");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.DragEnter), new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, "A"));
        h.Advance(200);
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.DragEnd)); h.Advance(200);
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        h.External();
        Assert.Equal(SpaceVisualState.Returning, h.State().VisualState);
    }

    [Fact]
    public void CollapseReentryAndStaleCompletionCannotChangeNewFocus()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A")); h.Advance(20); h.Complete("A");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, "A"));
        long old = h.Scheduler.Scheduled[^1].Token;
        h.Advance(20);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, "A"));
        h.Advance(10);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Deadline, Token: old));
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, "A")); h.Advance(30);
        long returnToken = h.State().TransitionToken;
        Assert.Equal(SpaceVisualState.Returning, h.State().VisualState);
        h.Complete("A"); h.Expand("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TransitionCompleted, "A", Token: returnToken));
        Assert.Equal("B", h.Controller.FocusOwner);
        Assert.Equal(SpaceVisualState.Idle, h.State().VisualState);
    }

    [Theory]
    [InlineData(InteractionActivity.Searching)]
    [InlineData(InteractionActivity.Scrolling)]
    [InlineData(InteractionActivity.ContextMenuOpen)]
    [InlineData(InteractionActivity.Dragging)]
    [InlineData(InteractionActivity.PointerCaptured)]
    public void EveryActivityPausesAutoCollapseAndClearingItRestartsDeadline(InteractionActivity activity)
    {
        var h = new Harness();
        h.Expand("A"); h.Complete("A");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ActivityChanged, "A", Activities: activity));
        h.Advance(100);
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ActivityChanged, "A", Activities: InteractionActivity.None));
        h.Advance(29);
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        h.Advance(1);
        Assert.Equal(SpaceVisualState.Returning, h.State().VisualState);
    }

    [Fact]
    public void SearchRequiresExplicitActivationAndImeKeepsFocusSafe()
    {
        var h = new Harness();
        h.Expand("A"); h.Complete("A");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TextInput, "A", Text: "ignored"),
            new SpaceInteractionEvent(SpaceInteractionEventKind.ImeStarted, "A"));
        Assert.Equal("", h.Controller.SearchText);
        Assert.False(h.Controller.ImeActive);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, "A"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TextInput, "A", Text: "中文"),
            new SpaceInteractionEvent(SpaceInteractionEventKind.ImeStarted, "A"));
        h.Advance(120);
        Assert.Equal("中文", h.Controller.SearchText);
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        var outcome = h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Contains(outcome, o => o.Kind == InteractionOutcomeKind.EscapeUnhandled);
        Assert.True(h.Controller.ImeActive);
        h.Click("B", "item-1"); h.Expand("B");
        Assert.Empty(h.Launcher.Calls);
        Assert.Equal("A", h.Controller.FocusOwner);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ImeEnded, "A"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Equal("", h.Controller.SearchText);
        Assert.Equal(KeyboardMode.ZhuomianKeyboardActive, h.Controller.KeyboardMode);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Equal(KeyboardMode.NoKeyboardCapture, h.Controller.KeyboardMode);
        h.Advance(30);
        Assert.Equal(SpaceVisualState.Returning, h.State().VisualState);
    }

    [Fact]
    public void EscapeUnwindsExactlyOneLayerAndDoesNotCaptureExternalKeys()
    {
        var h = new Harness();
        Assert.Contains(h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape)), o => o.Kind == InteractionOutcomeKind.EscapeUnhandled);
        h.Expand(); h.Complete("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, "B"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.TextInput, "B", Text: "query"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ActivityChanged, "B", Activities:
            InteractionActivity.Searching | InteractionActivity.ContextMenuOpen | InteractionActivity.Dragging));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.False(h.State("B").Activities.HasFlag(InteractionActivity.ContextMenuOpen));
        Assert.True(h.State("B").Activities.HasFlag(InteractionActivity.Dragging));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.False(h.State("B").Activities.HasFlag(InteractionActivity.Dragging));
        Assert.Equal("query", h.Controller.SearchText);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Equal("", h.Controller.SearchText);
        Assert.Equal(KeyboardMode.ZhuomianKeyboardActive, h.Controller.KeyboardMode);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Equal(KeyboardMode.NoKeyboardCapture, h.Controller.KeyboardMode);
        Assert.Equal(SpaceVisualState.Focused, h.State("B").VisualState);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Equal(SpaceVisualState.Returning, h.State("B").VisualState);
        h.Complete("B");
        Assert.Contains(h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape)), o => o.Kind == InteractionOutcomeKind.EscapeUnhandled);
    }

    [Fact]
    public void NoIdlePollingOrSavedLayoutMutationAndClockMustBeMonotonic()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.LayoutChanged, "A"));
        Assert.Null(h.Scheduler.NextDeadline);
        h.Advance(1000);
        Assert.Null(h.Scheduler.NextDeadline);
        h.Clock.NowMilliseconds = 999;
        Assert.Throws<InvalidOperationException>(() => h.Controller.Drain());
    }

    [Fact]
    public void StaleKeyboardAndImeEventsCannotModifyNewSearchSession()
    {
        var h = new Harness();
        h.Expand("B"); h.Complete("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, "B"));
        long old = h.Controller.KeyboardToken;
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Close, "B")); h.Complete("B");
        h.Expand("B"); h.Complete("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, "B"));
        long current = h.Controller.KeyboardToken;
        Assert.NotEqual(old, current);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ImeStarted, "B"));
        h.Controller.Post(new(SpaceInteractionEventKind.TextInput, "B", Token: old, Text: "stale"));
        h.Controller.Post(new(SpaceInteractionEventKind.ImeEnded, "B", Token: old));
        h.Controller.Drain();
        Assert.Equal("", h.Controller.SearchText);
        Assert.True(h.Controller.ImeActive);
        h.Controller.Post(new(SpaceInteractionEventKind.TextInput, "B", Token: current, Text: "current"));
        h.Controller.Drain();
        Assert.Equal("current", h.Controller.SearchText);
    }

    [Fact]
    public void ModalAndIdleContextMenuConsumeEscBeforeFocusOrClicks()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ActivityChanged, "A",
            Activities: InteractionActivity.ContextMenuOpen));
        h.Click("B", "item-1"); h.Expand();
        Assert.Empty(h.Launcher.Calls);
        Assert.Null(h.Controller.FocusOwner);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Equal(InteractionActivity.None, h.State().Activities);
        h.Expand(); h.Complete("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ModalOpened));
        h.Click("B", "item-1", 2);
        Assert.Empty(h.Launcher.Calls);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.False(h.Controller.ModalOpen);
        Assert.Equal(SpaceVisualState.Focused, h.State("B").VisualState);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.Equal(SpaceVisualState.Returning, h.State("B").VisualState);
    }

    [Fact]
    public void ModalEscPrecedesImeAndSearchCaptureCannotBeClearedByActivitySnapshot()
    {
        var h = new Harness();
        h.Expand("A"); h.Complete("A");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, "A"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ImeStarted, "A"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ModalOpened));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape));
        Assert.False(h.Controller.ModalOpen);
        Assert.True(h.Controller.ImeActive);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ImeEnded, "A"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ActivityChanged, "A"));
        h.Advance(100);
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        Assert.True(h.State().Activities.HasFlag(InteractionActivity.Searching));
    }

    [Fact]
    public void SameTickActivityAndImeSuppressDueCollapse()
    {
        var h = new Harness();
        h.Expand("A"); h.Complete("A");
        h.Clock.NowMilliseconds = 30;
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, "A"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ImeStarted, "A"));
        Assert.Equal(SpaceVisualState.Focused, h.State().VisualState);
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.AvailabilityChanged,
            Availability: DesktopAvailability.Suspended));
        Assert.False(h.Controller.ImeActive);
    }

    [Fact]
    public void CancelledOrRejectedBlankClickDoesNotExpandAndExplicitHoverUsesSafetyGate()
    {
        var h = new Harness();
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", GestureId: 1),
            new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", GestureId: 1, ValidClick: false));
        Assert.Null(h.Controller.FocusOwner);
        h.External(); h.Expand("A");
        Assert.Null(h.Controller.FocusOwner);
        h.Expand("B");
        Assert.Equal("B", h.Controller.FocusOwner);
    }

    [Fact]
    public void DelayedReturnCannotExpandOldSwitchAfterPreviewClick()
    {
        var h = new Harness();
        h.Expand("B"); h.Expand("C");
        h.Click("A", "item-1");
        Assert.Single(h.Launcher.Calls);
        h.Complete("B");
        Assert.Null(h.Controller.FocusOwner);
    }

    [Fact]
    public void CloseInvalidatesPendingClickAndCollapseResumesAfterFocusedItemClick()
    {
        var h = new Harness();
        h.Expand("B"); h.Complete("B");
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerDown, "B", "item-1", 1));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Close, "B"));
        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerUp, "B", "item-1", 1));
        Assert.Empty(h.Launcher.Calls);
        h.Complete("B"); h.Expand("A"); h.Complete("A");
        h.Click("A", "item-1", 2);
        h.Advance(30);
        Assert.Single(h.Launcher.Calls);
        Assert.Equal(SpaceVisualState.Returning, h.State().VisualState);
    }

    [Fact]
    public void DeterministicSchedulerOrdersCancelsAndDrainsWithoutPolling()
    {
        var scheduler = new DeterministicInteractionScheduler();
        Assert.Null(scheduler.NextDeadline);
        scheduler.Schedule(new(3, 20));
        scheduler.Schedule(new(2, 20));
        scheduler.Schedule(new(1, 10));
        scheduler.Cancel(1);
        Assert.Equal(20, scheduler.NextDeadline);
        Assert.Empty(scheduler.TakeDue(19));
        var due = scheduler.TakeDue(20);
        Assert.Equal(2, due.Count);
        Assert.Equal(2, due[0].Token);
        Assert.Equal(3, due[1].Token);
        Assert.Null(scheduler.NextDeadline);
        Assert.Empty(scheduler.TakeDue(1000));
    }

    [Theory]
    [InlineData(17)]
    [InlineData(731)]
    [InlineData(20261013)]
    public void GeneratedEventSequencesPreserveSafetyInvariantsAtEveryStep(int seed)
    {
        var h = new Harness();
        var random = new Random(seed);
        long gesture = 0;
        for (int i = 0; i < 3000; i++)
        {
            string id = random.Next(2) == 0 ? "A" : "B";
            switch (random.Next(18))
            {
                case 0: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerEnter, id)); break;
                case 1: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.PointerLeave, id)); break;
                case 2: h.Expand(id); break;
                case 3: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Close, id)); break;
                case 4: h.Complete(id); break;
                case 5: h.External("external-" + random.Next(3)); break;
                case 6: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.AvailabilityChanged)); break;
                case 7: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.AvailabilityChanged, Availability: DesktopAvailability.Suspended)); break;
                case 8: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.HostLost)); break;
                case 9: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.HostRestored)); break;
                case 10: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.DragEnter)); break;
                case 11: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.DragEnd)); break;
                case 12: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.SearchActivate, id)); h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ImeStarted, id)); break;
                case 13: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.ImeEnded, id)); break;
                case 14: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Escape)); break;
                case 15: h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.VisibleDesktopClick)); break;
                case 16:
                    int before = h.Launcher.Calls.Count;
                    h.Click(id, "item-1", ++gesture);
                    Assert.InRange(h.Launcher.Calls.Count - before, 0, 1);
                    break;
                case 17:
                    if (h.Scheduler.Scheduled.Count > 0)
                    {
                        h.Send(new SpaceInteractionEvent(SpaceInteractionEventKind.Deadline, Token: h.Scheduler.Scheduled[0].Token));
                    }
                    break;
            }
            h.Advance(random.Next(0, 40));
            var states = new[] { h.State("A"), h.State("B"), h.State("C") };
            Assert.InRange(states.Count(s => s.VisualState is SpaceVisualState.Focusing or SpaceVisualState.Focused or SpaceVisualState.Returning), 0, 1);
            Assert.Equal(h.Controller.FocusOwner is null ? 0 : 1,
                states.Count(s => s.VisualState is SpaceVisualState.Focusing or SpaceVisualState.Focused or SpaceVisualState.Returning));
            if (!h.Controller.HostAvailable || h.Controller.Availability == DesktopAvailability.Suspended)
            {
                Assert.All(states, s => Assert.Equal(SpaceVisualState.Idle, s.VisualState));
                Assert.Equal(KeyboardMode.NoKeyboardCapture, h.Controller.KeyboardMode);
                Assert.False(h.Controller.PointerCaptured);
            }
            if (h.Controller.KeyboardMode == KeyboardMode.NoKeyboardCapture)
            {
                Assert.Equal("", h.Controller.SearchText);
                Assert.False(h.Controller.ImeActive);
            }
            if (h.Controller.DragActive) { Assert.False(h.Controller.PointerCaptured); }
        }
    }
}
