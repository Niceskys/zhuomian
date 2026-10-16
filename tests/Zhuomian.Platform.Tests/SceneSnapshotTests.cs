using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

public sealed class SceneSnapshotTests
{
    private static readonly int[] Capacities = [6, 8, 12];
    private sealed class Clock : IInteractionClock { public long NowMilliseconds { get; set; } }
    [Theory]
    [InlineData(1d)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2d)]
    public void ScenePreservesCapacityOrderIdentityAndRoundedHitGeometry(double scale)
    {
        var dispatcher = new SpaceDispatcher(new Clock(), new(), modernFixture: true);
        dispatcher.HostRestored(); dispatcher.SetAvailability(DesktopAvailability.DesktopAvailable, "fixture");
        var scene = SceneSnapshot.Capture(1, dispatcher, scale, new(reducedTransparency: true));
        Assert.Equal(Capacities, scene.Spaces.Select(s => s.Items.Length).Order());
        Assert.Equal(26, scene.Spaces.SelectMany(s => s.Items).Select(i => i.Id).Distinct().Count());
        foreach (var space in scene.Spaces)
        {
            Assert.False(space.Expanded); Assert.Null(space.CloseBounds); Assert.Null(space.ExitBounds);
            Assert.Equal(dispatcher.Workspace.Spaces[Guid.Parse(space.Id)].ItemIds.Select(id => id.ToString("D")), space.Items.Select(i => i.Id));
            foreach (var item in space.Items)
            {
                int x = (int)Math.Round((item.Bounds.X + 10) * scale), y = (int)Math.Round((item.Bounds.Y + 10) * scale);
                var hit = scene.HitTest(x, y);
                Assert.Equal(item.Id, hit?.ItemId);
                int before = dispatcher.FakeLaunchCount;
                dispatcher.DispatchPointer(PointerAction.Down, hit);
                dispatcher.DispatchPointer(PointerAction.Up, hit);
                Assert.Equal(before + 1, dispatcher.FakeLaunchCount);
                Assert.Null(dispatcher.Controller.FocusOwner);
                dispatcher.DispatchPointer(PointerAction.Down, hit);
                dispatcher.HostLost(); dispatcher.HostRestored();
                dispatcher.DispatchPointer(PointerAction.Up, hit);
                Assert.Equal(before + 1, dispatcher.FakeLaunchCount);
            }
        }
    }
    [Fact]
    public void UnknownFakeIdentityCannotLaunch()
    {
        var dispatcher = new SpaceDispatcher(new Clock(), new(), modernFixture: true);
        Assert.False(dispatcher.TryLaunch(Guid.NewGuid().ToString("D")));
        Assert.Equal(0, dispatcher.FakeLaunchCount);
    }
}
