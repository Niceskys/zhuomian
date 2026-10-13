using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.Layout;

public sealed class SpaceLayoutTests
{
    private static readonly Guid Id = new("00000001-0000-0000-0000-000000000000");
    private static Space Space(ActivationMode mode) => new(Id, "Fixture", new(mode));
    private static ExpansionConstraints Constraints(double scale = 2) => new(scale, new(10, 10), new(2000, 2000));
    private static MonitorWorkArea Monitor(double x = 0, double y = 0, double width = 1000, double height = 800, double density = 1) =>
        new("monitor", new(x, y, width, height), new(density));
    private static SpaceLayoutPlan Expand(ActivationMode mode, LogicalRect bounds, MonitorWorkArea? monitor = null) =>
        SpaceLayoutEngine.Calculate(Space(mode), bounds, monitor ?? Monitor(), Constraints(), LayoutTransitionDirection.Expand);

    [Fact]
    public void AExpandsAroundOriginalCenterAndKeepsUserBounds()
    {
        var original = new LogicalRect(200, 200, 100, 80);
        var plan = Expand(ActivationMode.Hover, original);
        Assert.Equal(new(150, 160, 200, 160), plan.ExpandedBounds);
        Assert.Same(original, plan.UserBounds);
        Assert.Equal(original, plan.IdleBounds);
        Assert.Equal(plan.IdleBounds, plan.FromBounds);
        Assert.Equal(plan.ExpandedBounds, plan.TargetBounds);
        Assert.Equal(Id, plan.SpaceId);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(900, 700, 800, 600)]
    [InlineData(0, 700, 0, 600)]
    [InlineData(900, 0, 800, 0)]
    public void AClampsAtEachCornerWithoutChangingUserSize(double x, double y, double expectedX, double expectedY)
    {
        var original = new LogicalRect(x, y, 100, 100);
        var plan = Expand(ActivationMode.Hover, original);
        Assert.Equal(new(expectedX, expectedY, 200, 200), plan.ExpandedBounds);
        Assert.True(Monitor().Bounds.Contains(plan.ExpandedBounds));
        Assert.Equal(original, plan.UserBounds);
    }

    [Fact]
    public void TinyWorkAreaOverridesMinimumAndUniformlyShrinks()
    {
        var monitor = Monitor(-10, -10, 20, 10);
        var original = new LogicalRect(-5, -5, 100, 100);
        var plan = SpaceLayoutEngine.Calculate(Space(ActivationMode.Hover), original, monitor,
            new(2, new(200, 200), new(500, 500)), LayoutTransitionDirection.Expand);
        Assert.Equal(10, plan.ExpandedBounds.Width);
        Assert.Equal(10, plan.ExpandedBounds.Height);
        Assert.True(monitor.Bounds.Contains(plan.IdleBounds));
        Assert.True(monitor.Bounds.Contains(plan.ExpandedBounds));
        Assert.Same(original, plan.UserBounds);
    }

    [Theory]
    [InlineData(100, 20, 100, 20)]
    [InlineData(2000, 1000, 1000, 500)]
    [InlineData(1000, 2000, 400, 800)]
    public void FitPreservesAspectWhenShrinking(double width, double height, double expectedWidth, double expectedHeight)
    {
        var fitted = SpaceLayoutEngine.Fit(new(-999, 999, width, height), Monitor().Bounds);
        Assert.Equal(expectedWidth, fitted.Width);
        Assert.Equal(expectedHeight, fitted.Height);
        Assert.True(Monitor().Bounds.Contains(fitted));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1920, -200)]
    [InlineData(2000, -1000)]
    public void BUsesChosenMonitorCenterAndReturnsOriginalData(double x, double y)
    {
        var monitor = Monitor(x, y);
        var original = new LogicalRect(x + 10, y + 20, 100, 80);
        var expanded = Expand(ActivationMode.Click, original, monitor);
        Assert.Equal(new(x + 400, y + 320, 200, 160), expanded.TargetBounds);
        var returned = SpaceLayoutEngine.Calculate(Space(ActivationMode.Click), original, monitor, Constraints(), LayoutTransitionDirection.Return);
        Assert.Equal(original, returned.TargetBounds);
        Assert.Equal(expanded.TargetBounds, returned.FromBounds);
        Assert.Same(original, returned.UserBounds);
        Assert.Equal("monitor", returned.MonitorId);
    }

    [Theory]
    [InlineData(ActivationMode.Hover)]
    [InlineData(ActivationMode.Click)]
    public void InterruptedTransitionStartsAtExactAdapterVisualBounds(ActivationMode mode)
    {
        var original = new LogicalRect(10, 10, 100, 80);
        var current = new LogicalRect(-30, 50, 150, 110);
        foreach (var direction in Enum.GetValues<LayoutTransitionDirection>())
        {
            var plan = SpaceLayoutEngine.Calculate(Space(mode), original, Monitor(), Constraints(), direction, current);
            Assert.Same(current, plan.FromBounds);
            Assert.Same(original, plan.UserBounds);
            Assert.True(Monitor().Bounds.Contains(plan.TargetBounds));
        }
    }

    [Fact]
    public void ExplicitMaximumConstrainsDesiredExpansion()
    {
        var plan = SpaceLayoutEngine.Calculate(Space(ActivationMode.Click), new(10, 10, 100, 80), Monitor(),
            new(3, new(10, 10), new(120, 100)), LayoutTransitionDirection.Expand);
        Assert.Equal(120, plan.ExpandedBounds.Width);
        Assert.Equal(100, plan.ExpandedBounds.Height);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    [InlineData(3)]
    public void LogicalGeometryIsIndependentOfDensityMetadata(double density)
    {
        var bounds = new LogicalRect(-900, -100, 100, 80);
        var plan = Expand(ActivationMode.Click, bounds, Monitor(-1000, -200, density: density));
        Assert.Equal(Expand(ActivationMode.Click, bounds, Monitor(-1000, -200)).TargetBounds, plan.TargetBounds);
        Assert.Equal(new(100, 80), new PixelDensity(density).FromPixelSize(100 * density, 80 * density));
    }

    [Fact]
    public void PlacementCapturesOnlyAcceptedEditAndRestoresNormalizedPosition()
    {
        var monitor = Monitor(-1000, -200);
        var placement = PlacementRecovery.CaptureUserEdit(new(-800, 0, 100, 80), monitor);
        Assert.Equal(0.2, placement.NormalizedX);
        Assert.Equal(0.25, placement.NormalizedY);
        var nextMonitor = Monitor(-2000, -400, 2000, 1600, 2);
        var restored = PlacementRecovery.Resolve(placement, nextMonitor, nextMonitor);
        Assert.Equal(new(-1600, 0, 100, 80), restored.IdleBounds);
        Assert.Same(placement, restored.UserPlacement);
        Assert.False(restored.UsedPrimaryFallback);
    }

    [Fact]
    public void MissingMonitorUsesSuppliedPrimaryWithoutWritingPlacement()
    {
        var placement = new UserPlacement("missing", 1, 1, new(2000, 1000));
        var primary = new MonitorWorkArea("primary", new(-100, -80, 40, 30), new(2));
        var result = PlacementRecovery.Resolve(placement, null, primary);
        Assert.True(result.UsedPrimaryFallback);
        Assert.Same(primary, result.Monitor);
        Assert.True(primary.Bounds.Contains(result.IdleBounds));
        Assert.Same(placement, result.UserPlacement);
        Assert.Equal("missing", placement.MonitorId);
        Assert.Equal(new(2000, 1000), placement.Size);
        Assert.Throws<ArgumentException>(() => PlacementRecovery.Resolve(placement, primary, primary));
    }

    [Theory]
    [InlineData(1000000000, 0.000001)]
    [InlineData(0.000001, 1000000000)]
    public void ExtremeAspectHasFiniteVisibleFallback(double width, double height)
    {
        var plan = Expand(ActivationMode.Hover, new(0, 0, width, height), Monitor(-1, -1, 2, 2));
        Assert.True(Monitor(-1, -1, 2, 2).Bounds.Contains(plan.TargetBounds));
        Assert.True(plan.TargetBounds.Width > 0 && plan.TargetBounds.Height > 0);
    }

    [Fact]
    public void LargeOriginsRemainInsideWorkAreaAndRepeatExactly()
    {
        var monitor = Monitor(999999999, -999999999, 1, 1);
        var bounds = new LogicalRect(0, 0, 123, 77);
        var first = Expand(ActivationMode.Click, bounds, monitor);
        for (int i = 0; i < 100; i++) { Assert.Equal(first, Expand(ActivationMode.Click, bounds, monitor)); }
        Assert.True(monitor.Bounds.Contains(first.TargetBounds));
        Assert.True(monitor.Bounds.Contains(first.IdleBounds));
        var restored = PlacementRecovery.Resolve(new("monitor", 1, 1, new(123, 77)), monitor, monitor);
        Assert.True(monitor.Bounds.Contains(restored.IdleBounds));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(double.MaxValue)]
    public void InvalidSizesAndWorkAreasReject(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalSize(value, 10));
        if (value != 0)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalRect(0, 0, value, 10));
        }
        else
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MonitorWorkArea("monitor", new(0, 0, 0, 10), new(1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => Expand(ActivationMode.Hover, new(0, 0, 0, 10)));
        }
    }

    [Fact]
    public void DeterministicFractionalGeometryAlwaysFitsChosenMonitor()
    {
        var random = new Random(731);
        for (int i = 0; i < 500; i++)
        {
            var monitor = Monitor(random.NextDouble() * 4000 - 2000, random.NextDouble() * 4000 - 2000,
                random.NextDouble() * 2000 + 0.01, random.NextDouble() * 2000 + 0.01);
            var original = new LogicalRect(random.NextDouble() * 8000 - 4000, random.NextDouble() * 8000 - 4000,
                random.NextDouble() * 4000 + 0.01, random.NextDouble() * 4000 + 0.01);
            foreach (var mode in Enum.GetValues<ActivationMode>())
            {
                var plan = Expand(mode, original, monitor);
                Assert.True(monitor.Bounds.Contains(plan.IdleBounds));
                Assert.True(monitor.Bounds.Contains(plan.ExpandedBounds));
                Assert.Same(original, plan.UserBounds);
                Assert.Equal(plan, Expand(mode, original, monitor));
            }
        }
    }

    [Fact]
    public void InvalidParametersNullsAndOverflowRejectWithoutChangingInputs()
    {
        var original = new LogicalRect(10, 10, 100, 80);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalRect(1000000000, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalRect(double.NaN, 0, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelDensity(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelDensity(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Constraints(double.MaxValue));
        Assert.Throws<ArgumentException>(() => new ExpansionConstraints(2, new(20, 20), new(10, 10)));
        Assert.Throws<ArgumentException>(() => new MonitorWorkArea("bad\n", original, new(1)));
        Assert.Throws<ArgumentException>(() => new MonitorWorkArea(" ", original, new(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserPlacement("monitor", -1, 0, new(10, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserPlacement("monitor", 0, double.NaN, new(10, 10)));
        Assert.Throws<ArgumentException>(() => PlacementRecovery.CaptureUserEdit(new(-1, 0, 100, 80), Monitor()));
        Assert.Throws<ArgumentOutOfRangeException>(() => SpaceLayoutEngine.Calculate(Space(ActivationMode.Hover), original, Monitor(), Constraints(), (LayoutTransitionDirection)99));
        Assert.Throws<ArgumentNullException>(() => SpaceLayoutEngine.Calculate(null!, original, Monitor(), Constraints(), LayoutTransitionDirection.Expand));
        Assert.Throws<ArgumentNullException>(() => SpaceLayoutEngine.Fit(original, null!));
        Assert.Equal(new(10, 10, 100, 80), original);
    }
}
