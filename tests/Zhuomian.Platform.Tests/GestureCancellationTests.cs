using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

public sealed class GestureCancellationTests
{
    private sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    private static string A => EngineeringFixture.HoverId.ToString("D");
    private static SurfaceHit Preview => new(A, EngineeringFixture.ItemId.ToString("D"));
    private static MonitorWorkArea Monitor => new("test", new(0, 0, 1200, 800), new(1));

    [Theory]
    [InlineData(PointerAction.Cancel)]
    [InlineData(PointerAction.Leave)]
    [InlineData(PointerAction.DoubleClick)]
    public void CancellationReleasesCorePinAndAllowsFreshHoverAndClick(PointerAction cancel)
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        d.DispatchPointer(PointerAction.Down, Preview);
        Assert.True(d.Controller.PointerCaptured);
        d.DispatchPointer(cancel);
        Assert.False(d.HasGesture); Assert.False(d.Controller.PointerCaptured);
        d.DispatchPointer(PointerAction.Up, Preview);
        Assert.Equal(0, d.FakeLaunchCount);
        d.DispatchPointer(PointerAction.Leave);
        d.DispatchPointer(PointerAction.Move, new(A)); clock.NowMilliseconds = 20;
        d.Complete(Assert.Single(d.RenderTargets(Monitor)));
        Assert.Equal(A, d.Controller.FocusOwner);
        d.DispatchPointer(PointerAction.Down, Preview); d.DispatchPointer(PointerAction.Up, Preview);
        Assert.Equal(1, d.FakeLaunchCount); Assert.False(d.Controller.PointerCaptured);
    }

    [Fact]
    public void DoubleClickFollowUpIsConsumedButNextIndependentClickWorks()
    {
        var d = new SpaceDispatcher(new Clock(), new());
        d.DispatchPointer(PointerAction.Down, Preview); d.DispatchPointer(PointerAction.Up, Preview);
        d.DispatchPointer(PointerAction.DoubleClick, Preview); d.DispatchPointer(PointerAction.Up, Preview);
        Assert.Equal(1, d.FakeLaunchCount); Assert.False(d.Controller.PointerCaptured);
        d.DispatchPointer(PointerAction.Down, Preview); d.DispatchPointer(PointerAction.Up, Preview);
        Assert.Equal(2, d.FakeLaunchCount); Assert.Null(d.Controller.FocusOwner);
    }

    [Fact]
    public void CancelledDragDoesNotLaunchAndRequiresFreshEnterBeforeHover()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        d.DispatchPointer(PointerAction.Down, Preview); d.DispatchPointer(PointerAction.Cancel);
        d.DispatchPointer(PointerAction.Move, Preview, externalButtonHeld: true);
        clock.NowMilliseconds = 20; Assert.Empty(d.RenderTargets(Monitor));
        d.DispatchPointer(PointerAction.Up, Preview);
        Assert.False(d.HasGesture); Assert.False(d.Controller.PointerCaptured);
        clock.NowMilliseconds = 40; Assert.Empty(d.RenderTargets(Monitor));
        d.DispatchPointer(PointerAction.Leave); d.DispatchPointer(PointerAction.Move, new(A));
        clock.NowMilliseconds = 60; Assert.Single(d.RenderTargets(Monitor));
        Assert.Equal(0, d.FakeLaunchCount);
    }

    [Theory]
    [InlineData("foreground")]
    [InlineData("suspended")]
    [InlineData("hostlost")]
    public void SafetyPreemptionReleasesGestureAndRejectsLateUp(string path)
    {
        var scheduler = new DeterministicInteractionScheduler();
        var d = new SpaceDispatcher(new Clock(), scheduler);
        d.SetAvailability(DesktopAvailability.ExternalForeground, "one");
        d.DispatchPointer(PointerAction.Down, Preview);
        if (path == "hostlost") { d.HostLost(); d.HostRestored(); }
        else { d.SetAvailability(path == "suspended" ? DesktopAvailability.Suspended : DesktopAvailability.ExternalForeground, "two"); }
        d.DispatchPointer(PointerAction.Up, Preview);
        Assert.False(d.HasGesture); Assert.False(d.Controller.PointerCaptured);
        Assert.Null(scheduler.NextDeadline); Assert.Null(d.Controller.FocusOwner); Assert.Equal(0, d.FakeLaunchCount);
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "shell");
        d.DispatchPointer(PointerAction.Down, Preview); d.DispatchPointer(PointerAction.Up, Preview);
        Assert.Equal(1, d.FakeLaunchCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TitleActionsDoNotLeaveACoreGesture(bool exit)
    {
        string b = EngineeringFixture.ClickId.ToString("D");
        var d = new SpaceDispatcher(new Clock(), new());
        var title = new SurfaceHit(b, Close: !exit, Exit: exit);
        d.DispatchPointer(PointerAction.Down, title); d.DispatchPointer(PointerAction.Up, title);
        Assert.False(d.HasGesture); Assert.False(d.Controller.PointerCaptured);
        Assert.Equal(exit, d.ExitRequested); Assert.Equal(0, d.FakeLaunchCount);
    }

    [Fact]
    public void CancelThenPreviewDownAtHoverDeadlineRetainsInputPriority()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "shell");
        d.DispatchPointer(PointerAction.Down, Preview); d.DispatchPointer(PointerAction.Cancel);
        d.DispatchPointer(PointerAction.Leave); d.DispatchPointer(PointerAction.Move, Preview);
        clock.NowMilliseconds = 20;
        d.SetAvailability(DesktopAvailability.DesktopAvailable, "shell");
        d.DispatchPointer(PointerAction.Down, Preview);
        Assert.Null(d.Controller.FocusOwner);
        d.DispatchPointer(PointerAction.Up, Preview);
        Assert.Equal(1, d.FakeLaunchCount); Assert.Null(d.Controller.FocusOwner);
        Assert.False(d.Controller.PointerCaptured);
    }
}
