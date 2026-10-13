using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

public sealed class DispatcherTests
{
    private sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    private static string A => EngineeringFixture.HoverId.ToString("D");
    private static string B => EngineeringFixture.ClickId.ToString("D");
    private static string Item => EngineeringFixture.ItemId.ToString("D");
    private static MonitorWorkArea Monitor => new("test", new(0, 0, 1200, 800), new(1));
    private static (SpaceDispatcher Dispatcher, Clock Clock, DeterministicInteractionScheduler Scheduler) Create()
    {
        var clock = new Clock(); var scheduler = new DeterministicInteractionScheduler();
        return (new(clock, scheduler), clock, scheduler);
    }
    [Fact]
    public void RealMappingKeepsPreviewPinnedAndDoesNotExpandAtHoverDeadline()
    {
        var (d, clock, _) = Create();
        var preview = new SurfaceHit(A, Item);
        d.DispatchPointer(PointerAction.Move, preview);
        clock.NowMilliseconds = 19;
        d.DispatchPointer(PointerAction.Down, preview);
        clock.NowMilliseconds = 20;
        d.RenderTargets(Monitor);
        d.DispatchPointer(PointerAction.Up, preview);
        Assert.Equal(1, d.FakeLaunchCount);
        Assert.Null(d.Controller.FocusOwner);
        d.DispatchPointer(PointerAction.DoubleClick, preview);
        d.DispatchPointer(PointerAction.Up, preview);
        Assert.Equal(1, d.FakeLaunchCount);
    }
    [Fact]
    public void ForegroundReconciliationCannotConsumeHoverBeforeSameTickPreviewDown()
    {
        var (d, clock, _) = Create();
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "shell");
        var preview = new SurfaceHit(A, Item);
        d.DispatchPointer(PointerAction.Move, preview);
        clock.NowMilliseconds = 20;
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "shell");
        d.DispatchPointer(PointerAction.Down, preview);
        Assert.Null(d.Controller.FocusOwner);
        d.DispatchPointer(PointerAction.Up, preview);
        Assert.Equal(1, d.FakeLaunchCount);
    }
    [Theory]
    [InlineData(DesktopAvailability.ExternalForeground)]
    [InlineData(DesktopAvailability.Suspended)]
    public void UnarmedOrUnsafeHoverCannotExpand(DesktopAvailability availability)
    {
        var (d, clock, _) = Create();
        d.SetAvailability(availability, "external-1");
        for (int i = 0; i < 100; i++)
        {
            d.DispatchPointer(PointerAction.Move, new(A)); clock.NowMilliseconds += 30;
            Assert.Empty(d.RenderTargets(Monitor)); d.DispatchPointer(PointerAction.Leave);
            Assert.Null(d.Controller.FocusOwner);
        }
        Assert.Equal(KeyboardMode.NoKeyboardCapture, d.Controller.KeyboardMode);
    }
    [Fact]
    public void BlankClickArmsExplicitIntentButDoesNotLaunch()
    {
        var (d, clock, _) = Create();
        d.SetAvailability(DesktopAvailability.ExternalForeground, "external");
        d.DispatchPointer(PointerAction.Down, new(A)); d.DispatchPointer(PointerAction.Up, new(A));
        Assert.Equal(DesktopIntentGate.Armed, d.Controller.IntentGate);
        d.DispatchPointer(PointerAction.Leave); d.DispatchPointer(PointerAction.Move, new(A));
        clock.NowMilliseconds = 20;
        Assert.Single(d.RenderTargets(Monitor));
        Assert.Equal(A, d.Controller.FocusOwner);
        Assert.Equal(0, d.FakeLaunchCount);
    }
    [Fact]
    public void HoverUses20msAndSafeInPlaceBoundsThenReturns()
    {
        var (d, clock, scheduler) = Create();
        d.DispatchPointer(PointerAction.Move, new(A));
        clock.NowMilliseconds = 19; Assert.Empty(d.RenderTargets(Monitor));
        clock.NowMilliseconds = 20; var focus = Assert.Single(d.RenderTargets(Monitor));
        Assert.Equal(A, focus.SpaceId); d.Complete(focus);
        Assert.Equal(SpaceVisualState.Focused, d.Controller.GetState(A).VisualState);
        var expected = SpaceLayoutEngine.Calculate(d.Workspace.Spaces[EngineeringFixture.HoverId],
            EngineeringFixture.SavedBounds(EngineeringFixture.HoverId), Monitor, EngineeringFixture.Expansion, LayoutTransitionDirection.Expand);
        Assert.Equal(expected.TargetBounds, d.VisualBounds[A]);
        Assert.True(Monitor.Bounds.Contains(d.VisualBounds[A]));
        d.DispatchPointer(PointerAction.Leave); clock.NowMilliseconds += 120;
        d.Complete(Assert.Single(d.RenderTargets(Monitor)));
        Assert.Equal(SpaceVisualState.Idle, d.Controller.GetState(A).VisualState);
        Assert.Equal(EngineeringFixture.SavedBounds(EngineeringFixture.HoverId), d.VisualBounds[A]);
        Assert.Null(scheduler.NextDeadline);
    }
    [Fact]
    public void ClickCentersSingleOwnerAndRejectsOldCompletionOnReverse()
    {
        var (d, _, _) = Create();
        d.DispatchPointer(PointerAction.Down, new(B)); d.DispatchPointer(PointerAction.Up, new(B));
        var focus = Assert.Single(d.RenderTargets(Monitor)); d.Complete(focus);
        Assert.Equal(600, d.VisualBounds[B].X + d.VisualBounds[B].Width / 2);
        d.DispatchPointer(PointerAction.Down, new(B, Close: true)); d.DispatchPointer(PointerAction.Up, new(B, Close: true));
        var returning = Assert.Single(d.RenderTargets(Monitor));
        d.DispatchPointer(PointerAction.Down, new(B)); d.DispatchPointer(PointerAction.Up, new(B));
        var reversed = Assert.Single(d.RenderTargets(Monitor));
        d.Complete(returning);
        Assert.Equal(SpaceVisualState.Focusing, d.Controller.GetState(B).VisualState);
        d.Complete(reversed);
        Assert.Equal(SpaceVisualState.Focused, d.Controller.GetState(B).VisualState);
    }
    [Fact]
    public void HostLostCancelsGestureTimersFocusAndQueuedCompletion()
    {
        var (d, _, scheduler) = Create();
        d.DispatchPointer(PointerAction.Down, new(B)); d.DispatchPointer(PointerAction.Up, new(B));
        var old = Assert.Single(d.RenderTargets(Monitor));
        d.DispatchPointer(PointerAction.Down, new(B, Item));
        d.HostLost(); d.HostRestored(); d.Complete(old);
        d.DispatchPointer(PointerAction.Up, new(B, Item));
        Assert.Null(d.Controller.FocusOwner); Assert.False(d.HasGesture);
        Assert.Equal(0, d.FakeLaunchCount); Assert.Null(scheduler.NextDeadline);
        Assert.Equal(DesktopIntentGate.Disarmed, d.Controller.IntentGate);
        Assert.Equal(KeyboardMode.NoKeyboardCapture, d.Controller.KeyboardMode);
    }
    [Fact]
    public void ForegroundGenerationChangeInvalidatesPreview()
    {
        var (d, _, _) = Create();
        d.SetAvailability(DesktopAvailability.ExternalForeground, "one");
        d.DispatchPointer(PointerAction.Down, new(A, Item));
        d.SetAvailability(DesktopAvailability.ExternalForeground, "two");
        d.DispatchPointer(PointerAction.Up, new(A, Item));
        Assert.Equal(0, d.FakeLaunchCount); Assert.Null(d.Controller.FocusOwner);
    }
    [Fact]
    public void DragLikeEntrySuppressesHoverAndRequiresFreshEnter()
    {
        var (d, clock, _) = Create();
        d.DispatchPointer(PointerAction.Move, new(A), externalButtonHeld: true);
        clock.NowMilliseconds = 30; Assert.Empty(d.RenderTargets(Monitor));
        d.DispatchPointer(PointerAction.Up, new(A));
        clock.NowMilliseconds = 60; Assert.Empty(d.RenderTargets(Monitor));
        d.DispatchPointer(PointerAction.Leave); d.DispatchPointer(PointerAction.Move, new(A));
        clock.NowMilliseconds = 80; Assert.Single(d.RenderTargets(Monitor));
        Assert.Equal(0, d.FakeLaunchCount);
    }
    [Fact]
    public void LeaveCancelAndDifferentUpCannotLaunchOrActivate()
    {
        var (d, _, _) = Create();
        d.DispatchPointer(PointerAction.Down, new(B, Item)); d.DispatchPointer(PointerAction.Leave);
        d.DispatchPointer(PointerAction.Cancel); d.DispatchPointer(PointerAction.Up, new(B));
        Assert.Equal(0, d.FakeLaunchCount); Assert.Null(d.Controller.FocusOwner);
    }
    [Fact]
    public void UntrustedVisualCapabilitiesRemainSolidOpaqueAndMotionDisabled()
    {
        var appearance = EngineeringFixture.Resolve(EngineeringFixture.ClickId, true, new(reducedTransparency: true, reducedMotion: true));
        Assert.Equal(SurfaceMaterial.SimulatedGlass, appearance.Inherited.Material);
        Assert.Equal(SurfaceMaterial.Solid, appearance.Effective.Material);
        Assert.Equal(1, appearance.Effective.Opacity); Assert.False(appearance.AllowMotion);
        Assert.Null(appearance.Effective.Image.Reference); Assert.Equal(ShadowSpec.None, appearance.Effective.Shadow);
    }
    [Fact]
    public void HighContrastPaletteWinsOverFixtureTheme()
    {
        var palette = new ReadablePalette(new(0, 0, 0), new(255, 255, 0));
        var result = EngineeringFixture.Resolve(EngineeringFixture.HoverId, false, new(true, true, true, palette));
        Assert.Equal(palette.Background, result.Effective.Tint); Assert.Equal(palette, result.ContentPalette);
    }
    [Fact]
    public void DifferentThreadCannotDispatchInput()
    {
        var (d, _, _) = Create();
        int ownerThread = Environment.CurrentManagedThreadId;
        int dispatchThread = ownerThread;
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            dispatchThread = Environment.CurrentManagedThreadId;
            failure = Record.Exception(() => d.DispatchPointer(PointerAction.Move, new(A)));
        })
        { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "Dedicated dispatcher-test thread did not complete.");
        Assert.NotEqual(ownerThread, dispatchThread);
        Assert.IsType<InvalidOperationException>(failure);
    }
}
