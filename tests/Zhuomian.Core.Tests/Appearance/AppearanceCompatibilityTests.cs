using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Core.WorkspaceModel;
using static Zhuomian.Core.Tests.Appearance.AppearanceFixtures;

namespace Zhuomian.Core.Tests.Appearance;

public sealed class AppearanceCompatibilityTests
{
    private sealed class Clock : IInteractionClock { public long NowMilliseconds => 0; }
    private sealed class Launcher : IInteractionLauncher
    {
        internal List<string> Calls { get; } = [];
        public bool TryLaunch(string itemId) { Calls.Add(itemId); return true; }
    }

    [Theory]
    [InlineData(ActivationMode.Hover)]
    [InlineData(ActivationMode.Click)]
    public void ThemeChangesDoNotWriteBackGeometryOrderCapacityOrFocus(ActivationMode mode)
    {
        var itemId = new Guid("30000000-0000-0000-0000-000000000001");
        var workspace = Workspace.Create(new("40000000-0000-0000-0000-000000000001"))
            .CreateSpace(A, "Fixture", new(mode, 0), new(1))
            .AddItemToSpace(A, new UrlItem(itemId, new("Fixture"), new("https://example.test/")));
        var space = workspace.Spaces[A];
        var user = new LogicalRect(10, 20, 100, 80);
        var monitor = new MonitorWorkArea("fixture", new(0, 0, 1000, 800), new(1.5));
        var constraints = new ExpansionConstraints(2, new(10, 10), new(1000, 800));
        var layout = SpaceLayoutEngine.Calculate(space, user, monitor, constraints, LayoutTransitionDirection.Expand);
        var placement = PlacementRecovery.CaptureUserEdit(user, monitor);
        var content = SpaceContentLayout.Calculate(workspace, A, user, new(new(20, 20), 5, 5, new(0, 0, 0, 0), 10, 20));
        var scheduler = new DeterministicInteractionScheduler();
        var launcher = new Launcher();
        var controller = new SpaceInteractionController(workspace.GetInteractionPolicies(), new Clock(), scheduler, launcher, 30);
        string id = A.ToString("D");
        controller.Post(new(SpaceInteractionEventKind.ExplicitExpand, id)); controller.Drain();
        long token = controller.GetState(id).TransitionToken;
        controller.Post(new(SpaceInteractionEventKind.TransitionCompleted, id, Token: token)); controller.Drain();
        controller.Post(new(SpaceInteractionEventKind.SearchActivate, id)); controller.Drain();
        controller.Post(new(SpaceInteractionEventKind.TextInput, id, Token: controller.KeyboardToken, Text: "fixture")); controller.Drain();
        var stateBefore = controller.GetState(id);
        long inputGeneration = controller.InputGeneration, keyboardToken = controller.KeyboardToken;
        var config = Config(new() { Opacity = Set(0.5) });
        for (int i = 0; i < 100; i++)
        {
            config = config.WithGlobalTheme(new(new() { Opacity = Set(i % 2 == 0 ? 0d : 1d) }));
            var appearance = Resolve(config, AppearanceState.Focused, environment: new(reducedMotion: i % 2 == 0));
            Assert.Equal(A, appearance.SpaceId);
        }
        Assert.Equal(layout, SpaceLayoutEngine.Calculate(space, user, monitor, constraints, LayoutTransitionDirection.Expand));
        Assert.Same(user, layout.UserBounds);
        Assert.Equal(placement, PlacementRecovery.CaptureUserEdit(user, monitor));
        Assert.Equal(2, workspace.Revision);
        Assert.Same(space, workspace.Spaces[A]);
        Assert.Equal(new[] { itemId }, space.ItemIds.ToArray());
        Assert.Equal(1, content.PreviewCapacity);
        Assert.Equal(1, space.PreviewPolicy.CapacityOverride);
        Assert.Equal(stateBefore, controller.GetState(id));
        Assert.Equal(id, controller.FocusOwner);
        Assert.Equal(KeyboardMode.ZhuomianKeyboardActive, controller.KeyboardMode);
        Assert.Equal("fixture", controller.SearchText);
        Assert.Equal(inputGeneration, controller.InputGeneration);
        Assert.Equal(keyboardToken, controller.KeyboardToken);
        Assert.Empty(launcher.Calls);
        Assert.Null(scheduler.NextDeadline);
    }

    [Fact]
    public void ABShareAppearanceAndAppearanceNeverSchedulesIdleWork()
    {
        var config = Config().WithSpace(A, new(new() { Tint = Set(Red) })).WithSpace(B, new(new() { Tint = Set(Red) }));
        var workspace = Workspace.Create(new("40000000-0000-0000-0000-000000000001"))
            .CreateSpace(A, "A", new(ActivationMode.Hover)).CreateSpace(B, "B", new(ActivationMode.Click));
        var scheduler = new DeterministicInteractionScheduler();
        var controller = new SpaceInteractionController(workspace.GetInteractionPolicies(), new Clock(), scheduler, new Launcher(), 30);
        for (int i = 0; i < 500; i++)
        {
            var state = i % 2 == 0 ? AppearanceState.Idle : AppearanceState.Focused;
            Assert.Equal(Resolve(config, state, A).Effective, Resolve(config, state, B).Effective);
        }
        Assert.Null(scheduler.NextDeadline);
        Assert.Null(controller.FocusOwner);
        Assert.Equal(KeyboardMode.NoKeyboardCapture, controller.KeyboardMode);
        Assert.Equal(SpaceVisualState.Idle, controller.GetState(A.ToString("D")).VisualState);
    }

    [Fact]
    public void CandidateAppearanceDoesNotChangeWorkspaceSchemaOrGrantAssetGarbageCollection()
    {
        var workspace = Workspace.Create(new("40000000-0000-0000-0000-000000000001"));
        var config = Config(new() { Image = Set(Image) });
        _ = Resolve(config);
        _ = config.ResetSpace(A);
        Assert.Equal(Workspace.CurrentSchemaVersion, workspace.SchemaVersion);
        Assert.Equal(0, workspace.Revision);
        Assert.Empty(workspace.GetRetainedManagedAssetIds());
        Assert.DoesNotContain(typeof(Workspace).GetProperties(), property => property.Name.Contains("Appearance", StringComparison.Ordinal));
        // Workspace's existing inventory does not know these candidates. It cannot authorize GC of their references.
        Assert.Same(Image.Reference, Resolve(config).Inherited.Image.Reference);
    }
}
