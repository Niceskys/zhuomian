using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

public sealed class MotionTests
{
    internal sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    private static string A => EngineeringFixture.HoverId.ToString("D");
    private static string B => EngineeringFixture.ClickId.ToString("D");
    private static MonitorWorkArea Monitor => new("test", new(0, 0, 1200, 800), new(1.5));
    private static void Click(SpaceDispatcher d, string id, bool close = false)
    {
        var hit = new SurfaceHit(id, Close: close);
        d.DispatchPointer(PointerAction.Down, hit); d.DispatchPointer(PointerAction.Up, hit);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSpaceContinuouslyInterpolatesAndReturnsToSavedBounds(bool hover)
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        string id = hover ? A : B;
        if (hover) { d.DispatchPointer(PointerAction.Move, new(A)); clock.NowMilliseconds = 20; }
        else { Click(d, B); }
        var saved = EngineeringFixture.SavedBounds(Guid.Parse(id));
        Assert.Empty(d.RenderTargets(Monitor, true)); Assert.Equal(saved, d.VisualBounds[id]);
        Assert.True(d.HasActiveAnimation);
        clock.NowMilliseconds += 120;
        Assert.Empty(d.RenderTargets(Monitor, true)); var middle = d.VisualBounds[id];
        Assert.True(middle.Width > saved.Width); Assert.True(Monitor.Bounds.Contains(middle));
        if (hover) { Assert.True(middle.Contains(saved)); }
        else { Assert.True(middle.Y > saved.Y); }
        clock.NowMilliseconds += 120;
        var completed = Assert.Single(d.RenderTargets(Monitor, true));
        Assert.False(d.HasActiveAnimation); d.Complete(completed);
        if (!hover) { Assert.Equal(600, d.VisualBounds[id].X + d.VisualBounds[id].Width / 2); }
        Click(d, id, close: true); d.RenderTargets(Monitor, true);
        clock.NowMilliseconds += 240;
        d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
        Assert.Equal(saved, d.VisualBounds[id]); Assert.Equal(SpaceVisualState.Idle, d.Controller.GetState(id).VisualState);
        Assert.False(d.HasActiveAnimation); Assert.Equal(2, d.VisualBounds.Count);
    }
    [Fact]
    public void ReverseTwiceStartsAtDisplayedFrameAndRejectsOldToken()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new()); Click(d, B);
        d.RenderTargets(Monitor, true); clock.NowMilliseconds = 100; d.RenderTargets(Monitor, true);
        var displayed = d.VisualBounds[B];
        var stale = new VisualCompletion(B, d.Controller.InputGeneration, d.Controller.GetState(B).TransitionToken);
        Click(d, B, close: true); clock.NowMilliseconds = 115;
        d.RenderTargets(Monitor, true); Assert.Equal(displayed, d.VisualBounds[B]);
        clock.NowMilliseconds = 175; d.RenderTargets(Monitor, true); var returning = d.VisualBounds[B];
        Assert.True(returning.Width < displayed.Width);
        Click(d, B); d.RenderTargets(Monitor, true); Assert.Equal(returning, d.VisualBounds[B]);
        d.Complete(stale); Assert.Equal(SpaceVisualState.Focusing, d.Controller.GetState(B).VisualState);
        clock.NowMilliseconds += 240; d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
        Assert.Equal(SpaceVisualState.Focused, d.Controller.GetState(B).VisualState);
    }
    [Fact]
    public void FiveHundredSwitchesReleaseOwnerBeforeNextFocusAndNeverLeakMotion()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        for (int i = 0; i < 500; i++)
        {
            Click(d, B); d.RenderTargets(Monitor, true);
            clock.NowMilliseconds += 80; d.RenderTargets(Monitor, true);
            d.Controller.Post(new(SpaceInteractionEventKind.ExplicitExpand, A)); d.Controller.Drain();
            d.RenderTargets(Monitor, true); Assert.Equal(B, d.Controller.FocusOwner);
            clock.NowMilliseconds += 240; d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
            Assert.Equal(A, d.Controller.FocusOwner); Assert.Equal(SpaceVisualState.Idle, d.Controller.GetState(B).VisualState);
            d.RenderTargets(Monitor, true); clock.NowMilliseconds += 240;
            d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
            Click(d, A, close: true); d.RenderTargets(Monitor, true); clock.NowMilliseconds += 240;
            d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
            Assert.Null(d.Controller.FocusOwner); Assert.False(d.HasActiveAnimation);
            Assert.Equal(KeyboardMode.NoKeyboardCapture, d.Controller.KeyboardMode);
        }
    }
    [Fact]
    public void PreviewAndExternalDragFreezeGeometryUntilReleased()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new()); Click(d, B);
        d.RenderTargets(Monitor, true); clock.NowMilliseconds = 60; d.RenderTargets(Monitor, true);
        var pinned = d.VisualBounds[B]; var item = new SurfaceHit(B, EngineeringFixture.ItemId.ToString("D"));
        d.DispatchPointer(PointerAction.Down, item); d.RenderTargets(Monitor, true);
        clock.NowMilliseconds = 1000; d.RenderTargets(Monitor, true); Assert.Equal(pinned, d.VisualBounds[B]);
        d.DispatchPointer(PointerAction.Up, item); d.RenderTargets(Monitor, true);
        Assert.Equal(1, d.FakeLaunchCount); Assert.Equal(pinned, d.VisualBounds[B]);
        d.DispatchPointer(PointerAction.Move, new(B), externalButtonHeld: true); d.RenderTargets(Monitor, true);
        clock.NowMilliseconds = 2000; d.RenderTargets(Monitor, true); Assert.Equal(pinned, d.VisualBounds[B]);
        d.DispatchPointer(PointerAction.Move, new(B)); d.RenderTargets(Monitor, true);
        clock.NowMilliseconds += 240; d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
        Assert.False(d.HasActiveAnimation);
    }
    [Fact]
    public void HoverReentryReversesAutomaticReturnAtCurrentBoundsWithoutBypassingExternalGate()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "shell");
        d.DispatchPointer(PointerAction.Move, new(A)); clock.NowMilliseconds = 20;
        d.RenderTargets(Monitor, true); clock.NowMilliseconds += 240;
        d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
        d.DispatchPointer(PointerAction.Leave); clock.NowMilliseconds += 120;
        d.RenderTargets(Monitor, true); clock.NowMilliseconds += 80; d.RenderTargets(Monitor, true);
        var current = d.VisualBounds[A];
        var old = new VisualCompletion(A, d.Controller.InputGeneration, d.Controller.GetState(A).TransitionToken);
        d.DispatchPointer(PointerAction.Move, new(A)); d.RenderTargets(Monitor, true);
        Assert.Equal(current, d.VisualBounds[A]); Assert.Equal(SpaceVisualState.Focusing, d.Controller.GetState(A).VisualState);
        d.Complete(old); Assert.Equal(SpaceVisualState.Focusing, d.Controller.GetState(A).VisualState);
        d.SetAvailability(DesktopAvailability.ExternalForeground, "external");
        d.DispatchPointer(PointerAction.Leave); d.DispatchPointer(PointerAction.Move, new(A));
        Assert.Equal(SpaceVisualState.Returning, d.Controller.GetState(A).VisualState);
    }
    [Fact]
    public void ExternalForegroundReturnsFromCurrentFrameAndCompletedFrameDoesNotRestart()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "shell"); Click(d, B);
        d.RenderTargets(Monitor, true); clock.NowMilliseconds = 100; d.RenderTargets(Monitor, true);
        var current = d.VisualBounds[B];
        d.SetAvailability(DesktopAvailability.ExternalForeground, "ordinary-application");
        d.RenderTargets(Monitor, true); Assert.Equal(current, d.VisualBounds[B]);
        Assert.Equal(SpaceVisualState.Returning, d.Controller.GetState(B).VisualState);
        clock.NowMilliseconds += 240; var done = Assert.Single(d.RenderTargets(Monitor, true));
        clock.NowMilliseconds += 100; Assert.Equal(done, Assert.Single(d.RenderTargets(Monitor, true)));
        Assert.False(d.HasActiveAnimation); d.Complete(done);
        Assert.Null(d.Controller.FocusOwner); Assert.Equal(DesktopIntentGate.Disarmed, d.Controller.IntentGate);
    }
    [Fact]
    public void CurrentMonitorWorkAreaAndEdgeClampContainAllFramesWithoutChangingSavedPlacement()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        var monitor = new MonitorWorkArea("current-only", new(0, 0, 720, 480), new(2));
        var saved = EngineeringFixture.SavedBounds(EngineeringFixture.ClickId); Click(d, B);
        for (int i = 0; i <= 24; i++)
        {
            clock.NowMilliseconds = i * 10; d.RenderTargets(monitor, true);
            Assert.True(monitor.Bounds.Contains(d.VisualBounds[B]));
            Assert.Equal(saved, EngineeringFixture.SavedBounds(EngineeringFixture.ClickId));
        }
        Assert.Equal(360, d.VisualBounds[B].X + d.VisualBounds[B].Width / 2);
        Assert.Equal(240, d.VisualBounds[B].Y + d.VisualBounds[B].Height / 2);
    }
    [Fact]
    public void ReducedMotionAndSuspensionCancelFramesAndHostLossRejectsGeneration()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new()); Click(d, B);
        d.RenderTargets(Monitor, true); clock.NowMilliseconds = 80; d.RenderTargets(Monitor, true);
        var old = new VisualCompletion(B, d.Controller.InputGeneration, d.Controller.GetState(B).TransitionToken);
        d.Complete(Assert.Single(d.RenderTargets(Monitor, false))); Assert.False(d.HasActiveAnimation);
        Click(d, B, close: true); d.RenderTargets(Monitor, true);
        d.SetAvailability(DesktopAvailability.Suspended, "locked");
        Assert.Empty(d.RenderTargets(Monitor, true)); Assert.False(d.HasActiveAnimation);
        Assert.Equal(EngineeringFixture.SavedBounds(EngineeringFixture.ClickId), d.VisualBounds[B]);
        d.HostLost(); d.HostRestored(); d.Complete(old);
        Assert.Null(d.Controller.FocusOwner); Assert.False(d.HasActiveAnimation);
        Assert.Equal(DesktopIntentGate.Disarmed, d.Controller.IntentGate);
    }
}
