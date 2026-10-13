using Zhuomian.Core.Interaction;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.WorkspaceModel;

public sealed class InteractionCompatibilityTests
{
    private sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    // Domain fixture resolves latest catalog IDs only. Real launch safety is a separate future adapter.
    private sealed class CatalogLauncher(Workspace workspace) : IInteractionLauncher
    {
        internal Workspace Workspace { get; set; } = workspace;
        internal List<Guid> Calls { get; } = [];
        public bool TryLaunch(string itemId)
        {
            if (!Guid.TryParseExact(itemId, "D", out var id) || !Workspace.ItemStore.Items.ContainsKey(id)) { return false; }
            Calls.Add(id);
            return true;
        }
    }
    private static readonly Guid W = new("00000001-0000-0000-0000-000000000000");
    private static readonly Guid A = new("00000002-0000-0000-0000-000000000000");
    private static readonly Guid B = new("00000003-0000-0000-0000-000000000000");
    private static readonly Guid I = new("00000004-0000-0000-0000-000000000000");
    private static Workspace Create(int delay) => Workspace.Create(W).CreateSpace(A, "A", new(ActivationMode.Hover, delay))
        .CreateSpace(B, "B", new(ActivationMode.Click)).AddItemToSpace(A, new UrlItem(I, new("Fixture"), new("https://example.test/"))).AddReference(B, I);

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(120)]
    public void DomainPoliciesFeedExistingControllerWithExactDelayAndStableIds(int delay)
    {
        var workspace = Create(delay);
        var clock = new Clock();
        var launcher = new CatalogLauncher(workspace);
        var controller = new SpaceInteractionController(workspace.GetInteractionPolicies(), clock, new DeterministicInteractionScheduler(), launcher, 30);
        string a = A.ToString("D");
        string b = B.ToString("D");
        controller.Post(new(SpaceInteractionEventKind.PointerEnter, b));
        controller.Post(new(SpaceInteractionEventKind.PointerEnter, a));
        controller.Drain();
        if (delay > 0)
        {
            clock.NowMilliseconds = delay - 1;
            controller.Drain();
            Assert.Null(controller.FocusOwner);
        }
        clock.NowMilliseconds = delay;
        controller.Drain();
        Assert.Equal(a, controller.FocusOwner);
        Assert.Equal(SpaceVisualState.Idle, controller.GetState(b).VisualState);
        Assert.Equal(KeyboardMode.NoKeyboardCapture, controller.KeyboardMode);
        controller.Post(new(SpaceInteractionEventKind.PointerDown, b, I.ToString("D"), GestureId: 1));
        controller.Post(new(SpaceInteractionEventKind.PointerUp, b, I.ToString("D"), GestureId: 1));
        controller.Drain();
        Assert.Equal(I, Assert.Single(launcher.Calls));
        Assert.Equal(4, workspace.Revision);
    }

    [Fact]
    public void DeleteSpaceAndGlobalRemovalInvalidatePinnedClickThroughExistingEvents()
    {
        var workspace = Create(20);
        var launcher = new CatalogLauncher(workspace);
        var controller = new SpaceInteractionController(workspace.GetInteractionPolicies(), new Clock(), new DeterministicInteractionScheduler(), launcher, 30);
        string b = B.ToString("D");
        string item = I.ToString("D");
        controller.Post(new(SpaceInteractionEventKind.PointerDown, b, item, GestureId: 1));
        controller.Drain();
        launcher.Workspace = workspace.RemoveItemGlobally(I, GlobalRemovalConfirmation.Confirmed).DeleteSpace(B);
        controller.Post(new(SpaceInteractionEventKind.ItemInvalidated, ItemId: item));
        controller.Post(new(SpaceInteractionEventKind.SpaceRemoved, b));
        controller.Post(new(SpaceInteractionEventKind.PointerUp, b, item, GestureId: 1));
        var outcomes = controller.Drain();
        Assert.Empty(launcher.Calls);
        Assert.Contains(outcomes, o => o.Kind == InteractionOutcomeKind.ClickInvalidated);
        Assert.Single(launcher.Workspace.GetInteractionPolicies());
        Assert.Same(workspace.ItemStore.Items[I], launcher.Workspace.ItemStore.RemovedItems[I]);
    }

    [Fact]
    public void UnbindingOneSpaceDoesNotGloballyInvalidateSharedItem()
    {
        var workspace = Create(20).RemoveReference(A, I);
        var launcher = new CatalogLauncher(workspace);
        var controller = new SpaceInteractionController(workspace.GetInteractionPolicies(), new Clock(), new DeterministicInteractionScheduler(), launcher, 30);
        string b = B.ToString("D");
        controller.Post(new(SpaceInteractionEventKind.PointerDown, b, I.ToString("D"), GestureId: 1));
        controller.Post(new(SpaceInteractionEventKind.PointerUp, b, I.ToString("D"), GestureId: 1));
        controller.Drain();
        Assert.Equal(I, Assert.Single(launcher.Calls));
        Assert.Empty(workspace.GetUsableItemIds(A));
        Assert.Equal(new[] { I }, workspace.GetUsableItemIds(B));
    }
}
