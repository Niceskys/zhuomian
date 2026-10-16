using System.Collections.Immutable;
using Zhuomian.Core.Interaction;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

public sealed class SceneInputGuardTests
{
    private sealed class Clock : IInteractionClock { public long NowMilliseconds => 0; }
    private static SceneSnapshot Scene(double scale = 1, SceneLayout? layout = null)
    {
        var d = new SpaceDispatcher(new Clock(), new(), modernFixture: true);
        return SceneSnapshot.Capture(1, d, scale, new(reducedTransparency: true), layout);
    }
    private static SceneSnapshot Swap(SceneSnapshot scene, long frame)
    {
        var space = scene.Spaces[0];
        var items = space.Items.SetItem(0, space.Items[0] with { Id = space.Items[1].Id }).SetItem(1, space.Items[1] with { Id = space.Items[0].Id });
        return scene with { FrameId = frame, Spaces = scene.Spaces.SetItem(0, space with { Items = items }) };
    }
    [Theory]
    [InlineData(1d)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2d)]
    public void OldAndNewItemConflictCancelsFirstDownAtSyntheticDpi(double scale)
    {
        var a = Scene(scale); var guard = new SceneInputGuard();
        guard.Observe(a, 0); guard.Observe(Swap(a, 2), 16);
        var b = a.Spaces[0].Items[0].Bounds;
        Assert.False(guard.TryPointerDown((int)((b.X + 10) * scale), (int)((b.Y + 10) * scale), a.HostGeneration, 16, out var hit));
        Assert.Null(hit); Assert.Equal(1, guard.Conflicts); Assert.Null(guard.PinnedFrameId);
    }
    [Fact]
    public void UnchangedTargetAndBlankRemainUsableDuringOtherSpaceMotion()
    {
        var custom = Scene(layout: new() { Padding = 20, ColumnPitch = 72, RowPitch = 60 });
        Assert.Equal(custom.Spaces[0].Bounds.X + 20, custom.Spaces[0].Items[0].Bounds.X);
        Assert.Equal(72, custom.Spaces[0].Items[1].Bounds.X - custom.Spaces[0].Items[0].Bounds.X);
        Assert.Throws<ArgumentOutOfRangeException>(() => Scene(layout: new() { ColumnPitch = 0 }));
        var a = Scene(); var guard = new SceneInputGuard(); guard.Observe(a, 0);
        var other = a.Spaces[1];
        guard.Observe(a with { FrameId = 2, Spaces = a.Spaces.SetItem(1, other with { Bounds = new(other.Bounds.X + 5, other.Bounds.Y, other.Bounds.Width, other.Bounds.Height) }) }, 16);
        var item = a.Spaces[0].Items[0];
        Assert.True(guard.TryPointerDown((int)item.Bounds.X + 10, (int)item.Bounds.Y + 10, a.HostGeneration, 16, out var hit));
        Assert.Equal(item.Id, hit?.ItemId); Assert.Equal(2, guard.PinnedFrameId);
        guard.EndGesture(); Assert.Null(guard.PinnedFrameId);
        Assert.True(guard.TryPointerDown(1190, 790, a.HostGeneration, 16, out hit)); Assert.Null(hit);
    }
    [Fact]
    public void ItemToBlankAndBlankToItemCannotBecomeSpaceActivation()
    {
        var a = Scene(); var guard = new SceneInputGuard(); guard.Observe(a, 0);
        var b = a with { FrameId = 2, Spaces = a.Spaces.SetItem(0, a.Spaces[0] with { Items = ImmutableArray<SceneItem>.Empty }) };
        guard.Observe(b, 1); var item = a.Spaces[0].Items[0];
        Assert.False(guard.TryPointerDown((int)item.Bounds.X + 10, (int)item.Bounds.Y + 10, a.HostGeneration, 1, out _));
        guard.Observe(a with { FrameId = 3 }, 2);
        Assert.False(guard.TryPointerDown((int)item.Bounds.X + 10, (int)item.Bounds.Y + 10, a.HostGeneration, 2, out _));
    }
    [Fact]
    public void GenerationBoundaryRejectsUntilHistoryExpiresAndNeverAcceptsStaleGeneration()
    {
        var a = Scene(); var guard = new SceneInputGuard(); guard.Observe(a, 0);
        var b = a with { FrameId = 2, HostGeneration = a.HostGeneration + 1 }; guard.Observe(b, 16);
        Assert.False(guard.TryPointerDown(50, 90, b.HostGeneration, 16, out _));
        Assert.False(guard.TryPointerDown(50, 90, a.HostGeneration, 300, out _));
        Assert.True(guard.TryPointerDown(50, 90, b.HostGeneration, 300, out _));
        Assert.Equal(2, guard.GenerationRejected);
    }
    [Fact]
    public void OverflowFailsClosedAndStableHistoryRecoversWithoutIdleTimer()
    {
        var a = Scene(); var guard = new SceneInputGuard();
        for (int i = 1; i <= 100; i++) { guard.Observe(i % 2 == 0 ? Swap(a, i) : a with { FrameId = i }, i); }
        Assert.Equal(SceneInputGuard.Capacity, guard.RetainedScenes); Assert.True(guard.Evicted > 0);
        Assert.False(guard.TryPointerDown(50, 90, a.HostGeneration, 100, out _));
        Assert.Equal(1, guard.CoverageRejected);
        Assert.True(guard.TryPointerDown(50, 90, a.HostGeneration, 400, out _)); Assert.Equal(1, guard.RetainedScenes);
        guard.Clear(); Assert.Equal(0, guard.RetainedScenes);
    }
    [Fact]
    public void ReversalDoesNotResurrectOldTargetAndReducedMotionJumpMustAgree()
    {
        var a = Scene(); var guard = new SceneInputGuard(); guard.Observe(a, 0);
        guard.Observe(Swap(a, 2), 16); guard.Observe(a with { FrameId = 3 }, 32);
        var item = a.Spaces[0].Items[0]; int x = (int)item.Bounds.X + 10, y = (int)item.Bounds.Y + 10;
        Assert.False(guard.TryPointerDown(x, y, a.HostGeneration, 32, out _));
        Assert.True(guard.TryPointerDown(x, y, a.HostGeneration, 300, out var hit)); Assert.Equal(item.Id, hit?.ItemId);
    }
    [Fact]
    public void HostLostClearRejectsDelayedSceneVersionAndOldPressCannotLaunch()
    {
        var clock = new Clock(); var d = new SpaceDispatcher(clock, new(), modernFixture: true);
        var scene = SceneSnapshot.Capture(1, d, 1, new(reducedTransparency: true));
        var guard = new SceneInputGuard(); guard.Observe(scene, 0); var item = scene.Spaces[0].Items[0];
        Assert.True(guard.TryPointerDown((int)item.Bounds.X + 10, (int)item.Bounds.Y + 10, scene.HostGeneration, 0, out var hit));
        d.DispatchPointer(PointerAction.Down, hit); d.HostLost(); guard.Clear(); d.HostRestored(); d.DispatchPointer(PointerAction.Up, hit);
        Assert.Equal(0, d.FakeLaunchCount); Assert.False(d.HasGesture); Assert.Null(guard.PinnedFrameId);
        Assert.Throws<InvalidOperationException>(() => guard.Observe(scene, 1));
        Assert.False(guard.TryPointerDown(50, 90, d.Controller.InputGeneration, 1, out _));
    }
    [Fact]
    public void StaticSubmissionsDoNotAccumulateAndLongIdlePictureSurvivesSupersession()
    {
        var a = Scene(); var guard = new SceneInputGuard();
        for (int i = 1; i <= 500; i++) { guard.Observe(a with { FrameId = i }, i); }
        Assert.Equal(1, guard.RetainedScenes);
        guard.Observe(Swap(a, 501), 501);
        for (int i = 502; i <= 800; i++) { guard.Observe(Swap(a, i), i); }
        Assert.Equal(1, guard.RetainedScenes);
        Assert.True(guard.TryPointerDown(50, 90, a.HostGeneration, 800, out _));
        // An old static scene remains a candidate for a full lag window after a late change.
        guard.Observe(a with { FrameId = 801 }, 10000);
        Assert.False(guard.TryPointerDown(50, 90, a.HostGeneration, 10000, out _));
        Assert.True(guard.TryPointerDown(50, 90, a.HostGeneration, 10251, out _));
    }
}
