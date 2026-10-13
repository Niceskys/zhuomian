using Zhuomian.Core.Interaction;
using Zhuomian.Core.Launching;

namespace Zhuomian.Core.Tests.Launching;

public sealed class PreviewLaunchTests
{
    private sealed class Rig
    {
        internal LaunchFixture Fixture { get; } = new();
        internal PreviewLaunchAdapter Adapter { get; }
        internal SpaceInteractionController Controller { get; }
        internal static string Space => LaunchFixture.Space.ToString("D");
        internal static string Item => LaunchFixture.ItemId.ToString("D");
        internal Rig()
        {
            Adapter = new(Fixture.Service);
            Controller = new(Fixture.Workspace.GetInteractionPolicies(), Fixture.Clock, new DeterministicInteractionScheduler(), Adapter, 30);
            Adapter.Refresh(Fixture.Workspace, Controller);
        }
        internal void Down()
        {
            Adapter.Refresh(Fixture.Workspace, Controller);
            Adapter.Pin(Fixture.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, Controller.InputGeneration));
            Controller.Post(new(SpaceInteractionEventKind.PointerDown, Space, Item, GestureId: 1));
            Controller.Drain();
        }
        internal IReadOnlyList<InteractionOutcome> Up(int count = 1)
        {
            Adapter.Refresh(Fixture.Workspace, Controller);
            Controller.Post(new(SpaceInteractionEventKind.PointerUp, Space, Item, GestureId: 1, ClickCount: count));
            return Controller.Drain();
        }
    }

    [Fact]
    public void PreviewSingleClickSubmitsOnceWithoutExpandingAndDoubleClickFollowupIsConsumed()
    {
        var rig = new Rig();
        rig.Down();
        Assert.Contains(rig.Up(), result => result.Kind == InteractionOutcomeKind.LaunchSucceeded);
        rig.Up();
        rig.Controller.Post(new(SpaceInteractionEventKind.PointerDown, Rig.Space, Rig.Item, GestureId: 2, ClickCount: 2));
        rig.Controller.Post(new(SpaceInteractionEventKind.PointerUp, Rig.Space, Rig.Item, GestureId: 2, ClickCount: 2));
        rig.Controller.Drain();
        Assert.Equal(1, rig.Fixture.Executor.Calls);
        Assert.Null(rig.Controller.FocusOwner);
        Assert.False(rig.Adapter.TryLaunch(Rig.Item));
    }

    [Fact]
    public void ExistingExternalForegroundAllowsExplicitPreviewClick()
    {
        var rig = new Rig();
        rig.Controller.Post(new(SpaceInteractionEventKind.AvailabilityChanged, Availability: DesktopAvailability.ExternalForeground, ForegroundIdentity: "external"));
        rig.Controller.Drain();
        rig.Down();
        rig.Up();
        Assert.Equal(1, rig.Fixture.Executor.Calls);
        Assert.Null(rig.Controller.FocusOwner);
        Assert.Equal(DesktopIntentGate.Disarmed, rig.Controller.IntentGate);
    }

    [Theory]
    [InlineData(SpaceInteractionEventKind.HostLost)]
    [InlineData(SpaceInteractionEventKind.DragEnter)]
    [InlineData(SpaceInteractionEventKind.ModalOpened)]
    [InlineData(SpaceInteractionEventKind.ExternalInput)]
    [InlineData(SpaceInteractionEventKind.ItemInvalidated)]
    [InlineData(SpaceInteractionEventKind.PointerCancel)]
    public void SafetyOrCancellationBetweenDownAndUpCannotLaunchOrExpand(SpaceInteractionEventKind kind)
    {
        var rig = new Rig();
        rig.Down();
        rig.Controller.Post(new(kind, Rig.Space, Rig.Item, GestureId: 1));
        rig.Up();
        Assert.Equal(0, rig.Fixture.Executor.Calls);
        Assert.Null(rig.Controller.FocusOwner);
    }

    [Fact]
    public void NewForegroundIdentityAndSuspendedInvalidatePinnedRequest()
    {
        foreach (var availability in new[] { DesktopAvailability.ExternalForeground, DesktopAvailability.Suspended })
        {
            var rig = new Rig();
            rig.Down();
            rig.Controller.Post(new(SpaceInteractionEventKind.AvailabilityChanged, Availability: availability, ForegroundIdentity: "new"));
            rig.Up();
            Assert.Equal(0, rig.Fixture.Executor.Calls);
            Assert.Null(rig.Controller.FocusOwner);
        }
    }

    [Fact]
    public void DeletedReferenceAtPointerUpFailsWithoutTurningIntoBlankExpansion()
    {
        var rig = new Rig();
        rig.Down();
        rig.Fixture.Workspace = rig.Fixture.Workspace.RemoveReference(LaunchFixture.Space, LaunchFixture.ItemId);
        Assert.Contains(rig.Up(), result => result.Kind == InteractionOutcomeKind.LaunchFailed);
        Assert.Equal(LaunchStatus.Expired, rig.Adapter.LastResult.Status);
        Assert.Equal(0, rig.Fixture.Executor.Calls);
        Assert.Null(rig.Controller.FocusOwner);
    }

    [Fact]
    public void OldGenerationUpCannotConsumeNewContext()
    {
        var rig = new Rig();
        rig.Down();
        rig.Controller.Post(new(SpaceInteractionEventKind.ExternalInput));
        rig.Controller.Drain();
        rig.Controller.Post(new(SpaceInteractionEventKind.PointerUp, Rig.Space, Rig.Item, GestureId: 1, Generation: 0));
        rig.Controller.Drain();
        Assert.Equal(0, rig.Fixture.Executor.Calls);
        Assert.Null(rig.Controller.FocusOwner);
    }

    [Fact]
    public void BridgeNeverCreatesPermissionFromStringIdAndAmbiguousPinsFailClosed()
    {
        var rig = new Rig();
        Assert.False(rig.Adapter.TryLaunch(Rig.Item));
        rig.Adapter.Pin(rig.Fixture.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, 0));
        rig.Adapter.Pin(rig.Fixture.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, 0));
        Assert.False(rig.Adapter.TryLaunch(Rig.Item));
        rig.Adapter.Pin(rig.Fixture.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, 0));
        Assert.False(rig.Adapter.TryLaunch(Rig.Item)); // Ambiguity cannot be reset by another pin.
        Assert.Equal(0, rig.Fixture.Executor.Calls);
        rig.Adapter.Cancel();
        Assert.Equal(LaunchStatus.Cancelled, rig.Adapter.LastResult.Status);
    }

    [Fact]
    public void ActiveImePreventsDirectBridgeSubmission()
    {
        var rig = new Rig();
        rig.Controller.Post(new(SpaceInteractionEventKind.ExplicitExpand, Rig.Space));
        rig.Controller.Drain();
        long transition = rig.Controller.GetState(Rig.Space).TransitionToken;
        rig.Controller.Post(new(SpaceInteractionEventKind.TransitionCompleted, Rig.Space, Token: transition));
        rig.Controller.Drain();
        rig.Controller.Post(new(SpaceInteractionEventKind.SearchActivate, Rig.Space));
        rig.Controller.Drain();
        rig.Down();
        rig.Controller.Post(new(SpaceInteractionEventKind.ImeStarted, Rig.Space, Token: rig.Controller.KeyboardToken));
        rig.Controller.Drain();
        Assert.True(rig.Controller.ImeActive);
        Assert.False(rig.Adapter.TryLaunch(Rig.Item));
        Assert.Equal(0, rig.Fixture.Executor.Calls);
    }

    [Fact]
    public void ContextMenuGatePreventsBridgeEvenIfCalledDirectly()
    {
        var rig = new Rig();
        rig.Down();
        rig.Controller.Post(new(SpaceInteractionEventKind.ActivityChanged, Rig.Space, Activities: InteractionActivity.ContextMenuOpen));
        rig.Controller.Drain();
        Assert.False(rig.Adapter.TryLaunch(Rig.Item));
        Assert.Equal(0, rig.Fixture.Executor.Calls);
        Assert.Null(rig.Controller.FocusOwner);
    }
}
