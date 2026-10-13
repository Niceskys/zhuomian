using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.Layout;

public sealed class IconGridTests
{
    private static GridParameters Parameters(int automatic = 100, int maximum = 1000) => new(new(20, 20), 5, 5, new(10, 10, 10, 10), automatic, maximum);
    private static readonly Guid W = new("00000001-0000-0000-0000-000000000000");
    private static readonly Guid S = new("00000002-0000-0000-0000-000000000000");
    private static readonly Guid I = new("00000003-0000-0000-0000-000000000000");

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(1, 1, 1, 1)]
    [InlineData(3, 3, 1, 3)]
    [InlineData(7, 3, 3, 7)]
    [InlineData(1000, 3, 334, 9)]
    public void EmptySmallAndLargeGridsHavePredictableMetrics(int count, int columns, int totalRows, int preview)
    {
        var grid = IconGridLayout.Calculate(new(0, 0, 100, 100), count, Parameters());
        Assert.Equal(columns, grid.Columns);
        Assert.Equal(totalRows, grid.TotalRows);
        Assert.Equal(preview, grid.ResolvePreviewCapacity(PreviewCapacityPolicy.Automatic));
        if (count > 0)
        {
            Assert.Equal(new(0, 0, 20, 20), grid.GetCell(0));
            var last = grid.GetCell(count - 1);
            Assert.True(last.Right <= grid.ContentArea.Width);
            Assert.True(last.Bottom <= grid.ContentHeight);
        }
        else { Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetCell(0)); }
    }

    [Fact]
    public void ResizeReflowsRowsWhileRetainingIndexOrder()
    {
        var before = IconGridLayout.Calculate(new(0, 0, 100, 100), 10, Parameters());
        var after = IconGridLayout.Calculate(new(0, 0, 150, 100), 10, Parameters());
        Assert.Equal(3, before.Columns);
        Assert.Equal(5, after.Columns);
        Assert.Equal(new(0, 25, 20, 20), before.GetCell(3));
        Assert.Equal(new(75, 0, 20, 20), after.GetCell(3));
        Assert.Equal(10, after.ResolvePreviewCapacity(PreviewCapacityPolicy.Automatic));
        Assert.Equal(new(0, 25, 20, 20), before.GetCell(3));
    }

    [Fact]
    public void HoverReturnsOnlyVisualTransformWithoutGridReflow()
    {
        var grid = IconGridLayout.Calculate(new(0, 0, 100, 100), 1000, Parameters());
        var before = Enumerable.Range(0, 1000).Select(grid.GetCell).ToArray();
        var transform = grid.GetHoverTransform(4, 1.5);
        Assert.Equal(new ItemHoverTransform(1.5, 35, 35), transform);
        Assert.Equal(before, Enumerable.Range(0, 1000).Select(grid.GetCell));
        Assert.Equal(3, grid.Columns);
        Assert.Equal(334, grid.TotalRows);
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(8, 8)]
    [InlineData(100, 9)]
    [InlineData(int.MaxValue, 9)]
    public void OverridePrecedesAutoAndClampsToGeometryAndBudget(int? value, int expected)
    {
        var grid = IconGridLayout.Calculate(new(0, 0, 100, 100), 1000, Parameters(4, 20));
        var policy = new PreviewCapacityPolicy(value);
        Assert.Equal(expected, grid.ResolvePreviewCapacity(policy));
        Assert.Equal(value, policy.CapacityOverride);
    }

    [Fact]
    public void ExplicitPolicyIsStillBoundedByInjectedSafetyLimit()
    {
        var grid = IconGridLayout.Calculate(new(0, 0, 1000, 1000), 1000, Parameters(4, 20));
        Assert.Equal(20, grid.ResolvePreviewCapacity(new(int.MaxValue)));
        Assert.Equal(4, grid.ResolvePreviewCapacity(PreviewCapacityPolicy.Automatic));
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(1000, 1)]
    [InlineData(10, 10)]
    public void UnusableContentHasZeroPreviewAndNoInvalidCell(double width, double height)
    {
        var grid = IconGridLayout.Calculate(new(-100, -100, width, height), 1000, Parameters());
        Assert.Equal(0, grid.ResolvePreviewCapacity(new(int.MaxValue)));
        Assert.True(grid.ContentArea.Width >= 0 && grid.ContentArea.Height >= 0);
        if (grid.Columns == 0) { Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetCell(0)); }
    }

    [Theory]
    [InlineData(1, 1000)]
    [InlineData(1000, 1)]
    public void NarrowOrShortGridRemainsPredictableWithoutResizingIcons(double width, double height)
    {
        var grid = IconGridLayout.Calculate(new(0, 0, width, height), 1000,
            new(new(1, 1), 0, 0, new(0, 0, 0, 0), 1000, 1000));
        Assert.Equal((int)width, grid.Columns);
        Assert.Equal(1000, grid.ResolvePreviewCapacity(PreviewCapacityPolicy.Automatic));
        Assert.Equal(1, grid.GetCell(999).Width);
    }

    [Fact]
    public void PreviewCellsStayInsideContentAndRepeatUnderNegativeCoordinates()
    {
        var bounds = new LogicalRect(-1920, -800, 230.5, 160.25);
        var grid = IconGridLayout.Calculate(bounds, 1000, Parameters());
        var viewport = new LogicalRect(0, 0, grid.ContentArea.Width, grid.ContentArea.Height);
        int count = grid.ResolvePreviewCapacity(PreviewCapacityPolicy.Automatic);
        for (int index = 0; index < count; index++)
        {
            Assert.True(viewport.Contains(grid.GetCell(index)));
            Assert.Equal(grid.GetCell(index), IconGridLayout.Calculate(bounds, 1000, Parameters()).GetCell(index));
        }
        Assert.True(bounds.Contains(grid.ContentArea));
    }

    [Theory]
    [InlineData(69.999999, 2)]
    [InlineData(70, 3)]
    [InlineData(70.000001, 3)]
    public void CapacityUsesStrictWholeCellFit(double contentWidth, int expected)
    {
        var grid = IconGridLayout.Calculate(new(0, 0, contentWidth, 100), 1000,
            new(new(20, 20), 5, 5, new(0, 0, 0, 0), 1000, 1000));
        Assert.Equal(expected, grid.Columns);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public void InvalidIndicesRejectWithoutMutatingGrid(int index)
    {
        var grid = IconGridLayout.Calculate(new(0, 0, 100, 100), 1000, Parameters());
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetCell(index));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetHoverTransform(index, 2));
        Assert.Equal(new(0, 0, 20, 20), grid.GetCell(0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000001)]
    [InlineData(int.MaxValue)]
    public void ExcessiveOrNegativeItemCountRejects(int count) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => IconGridLayout.Calculate(new(0, 0, 100, 100), count, Parameters()));

    [Fact]
    public void ExcessiveExtentAndInvalidConfigurationRejectWithoutPartialGrid()
    {
        var bounds = new LogicalRect(0, 0, 100, 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => IconGridLayout.Calculate(bounds, 1000,
            new(new(20, 1000000000), 0, 0, new(0, 0, 0, 0), 10, 100)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridParameters(new(20, 20), double.NaN, 0, new(0, 0, 0, 0), 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GridParameters(new(20, 20), 0, double.PositiveInfinity, new(0, 0, 0, 0), 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parameters(100, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parameters(1, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => Parameters(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ContentInsets(-1, 0, 0, 0));
        Assert.Throws<ArgumentNullException>(() => IconGridLayout.Calculate(bounds, 10, null!));
        var grid = IconGridLayout.Calculate(bounds, 10, Parameters());
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetHoverTransform(0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.GetHoverTransform(0, 0));
        Assert.Throws<ArgumentNullException>(() => grid.ResolvePreviewCapacity(null!));
        Assert.Equal(new(0, 0, 100, 100), bounds);
    }

    [Fact]
    public void HugeGeometricCapacityUsesLongArithmeticAndSafeLimit()
    {
        var grid = IconGridLayout.Calculate(new(0, 0, 1000000000, 1000000000), 1000000,
            new(new(0.000001, 0.000001), 0, 0, new(0, 0, 0, 0), 10, 20));
        Assert.Equal(1000000, grid.Columns);
        Assert.Equal(1000000, grid.GeometricPreviewCapacity);
        Assert.Equal(20, grid.ResolvePreviewCapacity(new(int.MaxValue)));
        Assert.Equal(1, grid.TotalRows);
    }

    [Fact]
    public void FractionalCellSizesRemainInsidePreviewAndFullContentExtents()
    {
        var random = new Random(731);
        for (int i = 0; i < 500; i++)
        {
            var parameters = new GridParameters(new(random.NextDouble() * 50 + 1, random.NextDouble() * 50 + 1),
                random.NextDouble() * 10, random.NextDouble() * 10, new(0, 0, 0, 0), 1000, 1000);
            var bounds = new LogicalRect(0, 0, random.NextDouble() * 300 + 60, random.NextDouble() * 300 + 60);
            var grid = IconGridLayout.Calculate(bounds, 1000, parameters);
            Assert.True(grid.GetCell(999).Bottom <= grid.ContentHeight);
            Assert.True(grid.GetCell(999).Right <= bounds.Width);
            for (int index = 0; index < grid.ResolvePreviewCapacity(PreviewCapacityPolicy.Automatic); index++)
            {
                Assert.True(bounds.Contains(grid.GetCell(index)));
            }
        }
    }

    [Fact]
    public void WorkspaceProjectionPreservesOrderAndOverrideResetWithoutWritingSize()
    {
        var second = new Guid("00000004-0000-0000-0000-000000000000");
        var workspace = Workspace.Create(W).CreateSpace(S, "Fixture", new(ActivationMode.Hover))
            .AddItemToSpace(S, new UrlItem(second, new("Second"), new("https://example.test/2")))
            .AddItemToSpace(S, new UrlItem(I, new("First"), new("https://example.test/1")));
        var bounds = new LogicalRect(10, 10, 100, 100);
        var initial = SpaceContentLayout.Calculate(workspace, S, bounds, Parameters(1));
        Assert.Equal(second, initial.GetPreviewItemId(0));
        var configured = workspace.ConfigureSpace(S, new(ActivationMode.Hover), new(2));
        var manual = SpaceContentLayout.Calculate(configured, S, bounds, Parameters(1));
        Assert.Equal(2, manual.PreviewCapacity);
        Assert.Equal(new[] { second, I }, manual.ItemIds);
        var reset = configured.ConfigureSpace(S, new(ActivationMode.Hover), PreviewCapacityPolicy.Automatic);
        Assert.Equal(1, SpaceContentLayout.Calculate(reset, S, bounds, Parameters(1)).PreviewCapacity);
        Assert.Equal(new[] { second, I }, workspace.Spaces[S].ItemIds);
        Assert.Equal(3, workspace.Revision);
        Assert.Equal(new(10, 10, 100, 100), bounds);
        Assert.Throws<ArgumentOutOfRangeException>(() => initial.GetPreviewItemId(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => initial.GetPreviewItemId(-1));
        Assert.Throws<WorkspaceOperationException>(() => SpaceContentLayout.Calculate(workspace, Guid.Empty, bounds, Parameters()));
    }

    [Fact]
    public void CorruptReferencesRemainInSourceAndDoNotBecomeClickablePreview()
    {
        var item = new UrlItem(I, new("Fixture"), new("https://example.test"));
        var raw = new[] { Guid.Empty, I, W, I };
        var workspace = Workspace.FromSnapshot(W, 1, 7, ItemStore.FromSnapshot(new[] { item }, []),
            new[] { new Space(S, "Fixture", new(ActivationMode.Click), itemIds: raw) });
        var content = SpaceContentLayout.Calculate(workspace, S, new(0, 0, 100, 100), Parameters());
        Assert.Equal(new[] { I }, content.ItemIds);
        Assert.Equal(I, content.GetPreviewItemId(0));
        Assert.Equal(raw, workspace.Spaces[S].ItemIds);
        Assert.Equal(3, workspace.DiagnoseReferences(S).Length);
        Assert.Equal(7, workspace.Revision);
    }
}
