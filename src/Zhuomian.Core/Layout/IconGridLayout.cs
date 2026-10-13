using System.Collections.Immutable;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Layout;

/// <summary>All visual numbers and capacity limits are injected provisional parameters.</summary>
public sealed record GridParameters
{
    public GridParameters(LogicalSize iconSize, double horizontalGap, double verticalGap, ContentInsets padding,
        int automaticPreviewLimit, int maximumPreviewCapacity)
    {
        ArgumentNullException.ThrowIfNull(iconSize);
        ArgumentNullException.ThrowIfNull(padding);
        HorizontalGap = GeometryGuard.Number(horizontalGap, nameof(horizontalGap), 0);
        VerticalGap = GeometryGuard.Number(verticalGap, nameof(verticalGap), 0);
        ArgumentOutOfRangeException.ThrowIfNegative(automaticPreviewLimit);
        if (maximumPreviewCapacity < automaticPreviewLimit || maximumPreviewCapacity > IconGridLayout.MaximumItemCount)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPreviewCapacity));
        }
        IconSize = iconSize;
        Padding = padding;
        AutomaticPreviewLimit = automaticPreviewLimit;
        MaximumPreviewCapacity = maximumPreviewCapacity;
    }
    public LogicalSize IconSize { get; }
    public double HorizontalGap { get; }
    public double VerticalGap { get; }
    public ContentInsets Padding { get; }
    public int AutomaticPreviewLimit { get; }
    public int MaximumPreviewCapacity { get; }
}

/// <summary>Content-local pivot; only an adapter visual transform, never cell geometry or hit testing.</summary>
public readonly record struct ItemHoverTransform(double Scale, double PivotX, double PivotY);

/// <summary>O(1) grid metrics and indexed cells. Full-content cells can lie below viewport for scrolling.</summary>
public sealed class IconGridLayout
{
    /// <summary>Engineering resource envelope, not a UI/product Item limit.</summary>
    public const int MaximumItemCount = 1_000_000;
    private IconGridLayout(LogicalRect contentArea, GridParameters parameters, int itemCount, int columns, int visibleRows)
    {
        ContentArea = contentArea;
        Parameters = parameters;
        ItemCount = itemCount;
        Columns = columns;
        VisibleRows = visibleRows;
        TotalRows = columns == 0 ? 0 : (itemCount + columns - 1) / columns;
        ContentHeight = TotalRows == 0 ? 0 : (TotalRows - 1) * (parameters.IconSize.Height + parameters.VerticalGap) + parameters.IconSize.Height;
        GeometryGuard.Number(ContentHeight, nameof(ContentHeight), 0);
        GeometricPreviewCapacity = (int)Math.Min(MaximumItemCount, (long)columns * visibleRows);
    }
    public LogicalRect ContentArea { get; }
    public GridParameters Parameters { get; }
    public int ItemCount { get; }
    public int Columns { get; }
    public int VisibleRows { get; }
    public int TotalRows { get; }
    public double ContentHeight { get; }
    public int GeometricPreviewCapacity { get; }

    public static IconGridLayout Calculate(LogicalRect spaceBounds, int itemCount, GridParameters parameters)
    {
        GeometryGuard.Area(spaceBounds);
        ArgumentNullException.ThrowIfNull(parameters);
        if (itemCount < 0 || itemCount > MaximumItemCount) { throw new ArgumentOutOfRangeException(nameof(itemCount)); }
        var content = parameters.Padding.Apply(spaceBounds);
        int columns = CountThatFits(content.Width, parameters.IconSize.Width, parameters.HorizontalGap);
        int rows = CountThatFits(content.Height, parameters.IconSize.Height, parameters.VerticalGap);
        // Avoid empty-grid divisions and redundant columns; no allocation proportional to itemCount.
        columns = Math.Min(columns, itemCount);
        return new(content, parameters, itemCount, columns, rows);
    }

    private static int CountThatFits(double extent, double icon, double gap)
    {
        if (extent < icon) { return 0; }
        int count = (int)Math.Min(MaximumItemCount, Math.Floor((extent + gap) / (icon + gap)));
        // Strict fit, including floating-point boundary effects, without an epsilon that admits overflow.
        if (count > 0 && (count - 1) * (icon + gap) + icon > extent) { count--; }
        return count;
    }

    public int ResolvePreviewCapacity(PreviewCapacityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        int requested = policy.CapacityOverride ?? Parameters.AutomaticPreviewLimit;
        // Oversized overrides are safely constrained, without rewriting the original policy.
        return Math.Min(ItemCount, Math.Min(GeometricPreviewCapacity, Math.Min(requested, Parameters.MaximumPreviewCapacity)));
    }

    /// <summary>Content-local unscrolled cell; caller realizes only visible indices and maps scroll offset.</summary>
    public LogicalRect GetCell(int index)
    {
        if (index < 0 || index >= ItemCount || Columns == 0) { throw new ArgumentOutOfRangeException(nameof(index)); }
        return new(index % Columns * (Parameters.IconSize.Width + Parameters.HorizontalGap),
            index / Columns * (Parameters.IconSize.Height + Parameters.VerticalGap),
            Parameters.IconSize.Width, Parameters.IconSize.Height);
    }

    public ItemHoverTransform GetHoverTransform(int index, double scale)
    {
        GeometryGuard.Number(scale, nameof(scale), 1, 1000);
        var cell = GetCell(index);
        return new(scale, cell.X + cell.Width / 2, cell.Y + cell.Height / 2);
    }
}

/// <summary>Immutable ordered view over Workspace's structurally usable IDs; not launch authorization.</summary>
public sealed class SpaceContentLayout
{
    private SpaceContentLayout(Guid spaceId, ImmutableArray<Guid> itemIds, IconGridLayout grid, int previewCapacity)
    {
        SpaceId = spaceId;
        ItemIds = itemIds;
        Grid = grid;
        PreviewCapacity = previewCapacity;
    }
    public Guid SpaceId { get; }
    public ImmutableArray<Guid> ItemIds { get; }
    public IconGridLayout Grid { get; }
    public int PreviewCapacity { get; }
    public static SpaceContentLayout Calculate(Workspace workspace, Guid spaceId, LogicalRect spaceBounds, GridParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!workspace.Spaces.TryGetValue(spaceId, out var space)) { throw new WorkspaceOperationException(DomainError.SpaceNotFound); }
        var ids = workspace.GetUsableItemIds(spaceId);
        var grid = IconGridLayout.Calculate(spaceBounds, ids.Length, parameters);
        return new(spaceId, ids, grid, grid.ResolvePreviewCapacity(space.PreviewPolicy));
    }
    public Guid GetPreviewItemId(int index)
    {
        if (index < 0 || index >= PreviewCapacity) { throw new ArgumentOutOfRangeException(nameof(index)); }
        return ItemIds[index];
    }
}
