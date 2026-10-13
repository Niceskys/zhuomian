using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.Layout;

public sealed class LayoutInteractionCompatibilityTests
{
    private sealed class Clock : IInteractionClock { public long NowMilliseconds => 0; }
    private sealed class Launcher : IInteractionLauncher
    {
        internal List<string> Calls { get; } = [];
        public bool TryLaunch(string itemId) { Calls.Add(itemId); return true; }
    }
    private static readonly Guid W = new("00000001-0000-0000-0000-000000000000");
    private static readonly Guid S = new("00000002-0000-0000-0000-000000000000");
    private static readonly Guid I = new("00000003-0000-0000-0000-000000000000");

    [Fact]
    public void ReturningReactivationUsesCurrentVisualAndRejectsOldCompletion()
    {
        var workspace = Workspace.Create(W).CreateSpace(S, "Fixture", new(ActivationMode.Click));
        var controller = new SpaceInteractionController(workspace.GetInteractionPolicies(), new Clock(), new DeterministicInteractionScheduler(), new Launcher(), 30);
        string id = S.ToString("D");
        var user = new LogicalRect(10, 10, 100, 80);
        var monitor = new MonitorWorkArea("monitor", new(-1000, -500, 2000, 1000), new(1.5));
        var constraints = new ExpansionConstraints(2, new(10, 10), new(1000, 1000));
        controller.Post(new(SpaceInteractionEventKind.ExplicitExpand, id));
        controller.Drain();
        controller.Post(new(SpaceInteractionEventKind.Close, id));
        controller.Drain();
        long old = controller.GetState(id).TransitionToken;
        var current = new LogicalRect(-40, -40, 160, 120);
        var returning = SpaceLayoutEngine.Calculate(workspace.Spaces[S], user, monitor, constraints, LayoutTransitionDirection.Return, current);
        Assert.Equal(user, returning.TargetBounds);
        controller.Post(new(SpaceInteractionEventKind.ExplicitExpand, id));
        controller.Drain();
        long active = controller.GetState(id).TransitionToken;
        var reverse = SpaceLayoutEngine.Calculate(workspace.Spaces[S], user, monitor, constraints, LayoutTransitionDirection.Expand, current);
        Assert.Same(current, reverse.FromBounds);
        Assert.Same(user, reverse.UserBounds);
        Assert.NotEqual(old, active);
        controller.Post(new(SpaceInteractionEventKind.TransitionCompleted, id, Token: old));
        controller.Drain();
        Assert.Equal(SpaceVisualState.Focusing, controller.GetState(id).VisualState);
        controller.Post(new(SpaceInteractionEventKind.TransitionCompleted, id, Token: active));
        controller.Drain();
        Assert.Equal(SpaceVisualState.Focused, controller.GetState(id).VisualState);
        Assert.Equal(KeyboardMode.NoKeyboardCapture, controller.KeyboardMode);
        Assert.Equal(1, workspace.Revision);
    }

    [Fact]
    public void ResizeDuringPinnedPreviewClickDoesNotRetargetOrExpand()
    {
        var workspace = Workspace.Create(W).CreateSpace(S, "Fixture", new(ActivationMode.Hover, 0))
            .AddItemToSpace(S, new UrlItem(I, new("Fixture"), new("https://example.test")));
        var launcher = new Launcher();
        var controller = new SpaceInteractionController(workspace.GetInteractionPolicies(), new Clock(), new DeterministicInteractionScheduler(), launcher, 30);
        var parameters = new GridParameters(new(20, 20), 5, 5, new(10, 10, 10, 10), 10, 20);
        var content = SpaceContentLayout.Calculate(workspace, S, new(0, 0, 100, 100), parameters);
        string id = S.ToString("D");
        string item = content.GetPreviewItemId(0).ToString("D");
        controller.Post(new(SpaceInteractionEventKind.PointerDown, id, item, GestureId: 1));
        controller.Drain();
        var resized = SpaceContentLayout.Calculate(workspace, S, new(0, 0, 150, 100), parameters);
        controller.Post(new(SpaceInteractionEventKind.LayoutChanged, id));
        controller.Post(new(SpaceInteractionEventKind.PointerEnter, id));
        controller.Post(new(SpaceInteractionEventKind.PointerUp, id, item, GestureId: 1));
        controller.Drain();
        Assert.Equal(item, Assert.Single(launcher.Calls));
        Assert.Equal(content.ItemIds.ToArray(), resized.ItemIds.ToArray());
        Assert.Null(controller.FocusOwner);
        Assert.Equal(2, workspace.Revision);
    }
}
