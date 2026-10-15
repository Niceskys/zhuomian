using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

[Collection("Native resource diagnostics")]
public sealed class MotionIntegrationTests
{
    private sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    private static string B => EngineeringFixture.ClickId.ToString("D");
    private static MonitorWorkArea Monitor => new("test", new(0, 0, 1200, 800), new(1));
    private static void Click(SpaceDispatcher d)
    {
        d.DispatchPointer(PointerAction.Down, new(B)); d.DispatchPointer(PointerAction.Up, new(B));
    }
    [Theory]
    [InlineData(null, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void UnknownDisabledOrHighContrastCannotGrantMotion(bool? preference, bool contrast, bool expected)
        => Assert.Equal(expected, MotionPreference.Allow(preference, contrast));

    [Fact]
    public void SystemQueryFailureIsReducedMotionWithoutExportingExceptionPayload()
        => Assert.False(MotionPreference.Allow(MotionPreference.Read(() => throw new IOException("private")), false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsampledTimeBeforePauseAndRepeatedPolicyChangesDoNotJumpPinnedGeometry(bool drag)
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new()); Click(d);
        d.RenderTargets(Monitor, true); clock.NowMilliseconds = 60; d.RenderTargets(Monitor, true);
        var frame = d.VisualBounds[B]; clock.NowMilliseconds = 100;
        var item = new SurfaceHit(B, EngineeringFixture.ItemId.ToString("D"));
        if (drag) { d.DispatchPointer(PointerAction.Move, new(B), externalButtonHeld: true); }
        else { d.DispatchPointer(PointerAction.Down, item); }
        foreach (bool allow in new[] { true, false, true, false })
        {
            clock.NowMilliseconds += 400; Assert.Empty(d.RenderTargets(Monitor, allow));
            Assert.Equal(frame, d.VisualBounds[B]); Assert.False(d.HasActiveAnimation);
        }
        if (drag) { d.DispatchPointer(PointerAction.Move, new(B)); }
        else { d.DispatchPointer(PointerAction.Cancel); d.DispatchPointer(PointerAction.Up, item); }
        d.RenderTargets(Monitor, true); Assert.Equal(frame, d.VisualBounds[B]);
        clock.NowMilliseconds += 240; d.Complete(Assert.Single(d.RenderTargets(Monitor, true)));
        Assert.False(d.Controller.PointerCaptured); Assert.Equal(0, d.FakeLaunchCount);
        Assert.False(d.HasActiveAnimation); Assert.Equal(SpaceVisualState.Focused, d.Controller.GetState(B).VisualState);
    }

    [Fact]
    public void FiveRepeatedReversalsAndDelayedCompletionsRetainOneOwnerAndExactAppliedFrame()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new()); Click(d);
        d.RenderTargets(Monitor, true);
        var stale = new List<VisualCompletion>();
        for (int i = 0; i < 6; i++)
        {
            clock.NowMilliseconds += 57; d.RenderTargets(Monitor, true); var frame = d.VisualBounds[B];
            stale.Add(new(B, d.Controller.InputGeneration, d.Controller.GetState(B).TransitionToken));
            var hit = new SurfaceHit(B, Close: i % 2 == 0);
            d.DispatchPointer(PointerAction.Down, hit); d.DispatchPointer(PointerAction.Up, hit);
            d.RenderTargets(Monitor, true); Assert.Equal(frame, d.VisualBounds[B]);
            foreach (var old in stale) { d.Complete(old); }
            Assert.Equal(B, d.Controller.FocusOwner);
        }
        clock.NowMilliseconds += 240; var done = Assert.Single(d.RenderTargets(Monitor, true));
        clock.NowMilliseconds += 1000; Assert.Equal(done, Assert.Single(d.RenderTargets(Monitor, true)));
        Assert.False(d.HasActiveAnimation); d.Complete(done);
        d.HostLost(); d.HostRestored(); d.Complete(done);
        Assert.Null(d.Controller.FocusOwner); Assert.False(d.HasActiveAnimation);
    }

    [Fact]
    public void HoverCloseDuringExpansionAndReentryReverseFromAppliedGeometry()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new());
        string a = EngineeringFixture.HoverId.ToString("D");
        d.DispatchPointer(PointerAction.Move, new(a)); clock.NowMilliseconds = 20; d.RenderTargets(Monitor, true);
        clock.NowMilliseconds = 100; d.RenderTargets(Monitor, true); var expanding = d.VisualBounds[a];
        var close = new SurfaceHit(a, Close: true);
        d.DispatchPointer(PointerAction.Down, close); d.DispatchPointer(PointerAction.Up, close);
        d.RenderTargets(Monitor, true); Assert.Equal(expanding, d.VisualBounds[a]);
        Assert.Equal(SpaceVisualState.Returning, d.Controller.GetState(a).VisualState);
        clock.NowMilliseconds += 40; d.RenderTargets(Monitor, true); var returning = d.VisualBounds[a];
        d.DispatchPointer(PointerAction.Leave); d.DispatchPointer(PointerAction.Move, new(a));
        d.RenderTargets(Monitor, true); Assert.Equal(returning, d.VisualBounds[a]);
        Assert.Equal(SpaceVisualState.Focusing, d.Controller.GetState(a).VisualState);
        Assert.Equal(2, d.Workspace.Spaces.Count);
        Assert.True(d.VisualBounds[a].Contains(EngineeringFixture.SavedBounds(EngineeringFixture.HoverId)));
    }

    [Fact]
    public void NativeMotionHasRealRegionsPaintAndTimerOwnership()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { return; }
        NativeSmoke.CheckHiddenMotion(_ => { });
        NativeSmoke.CheckMotionSafety(_ => { });
    }
}
