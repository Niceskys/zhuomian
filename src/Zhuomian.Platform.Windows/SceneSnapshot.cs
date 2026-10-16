using System.Collections.Immutable;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;

namespace Zhuomian.Platform.Windows;

/// <summary>Shared authoritative grid contract; presentation never derives item coordinates.</summary>
public sealed record SceneLayout
{
    public static SceneLayout Default { get; } = new();
    public double Padding { get; init; } = 16;
    public double ColumnPitch { get; init; } = 64;
    public double RowPitch { get; init; } = 56;
    public double ItemWidth { get; init; } = 56;
    public double ItemHeight { get; init; } = 52;
    public double IdleContentTop { get; init; } = 44;
    public double ExpandedContentTop { get; init; } = 136;
    public double BottomPadding { get; init; } = 12;
    internal void Validate()
    {
        if (!double.IsFinite(Padding) || Padding < 0 || !double.IsFinite(BottomPadding) || BottomPadding < 0 ||
            !double.IsFinite(ColumnPitch) || ColumnPitch <= 0 || !double.IsFinite(RowPitch) || RowPitch <= 0 ||
            !double.IsFinite(ItemWidth) || ItemWidth <= 0 || ItemWidth > ColumnPitch ||
            !double.IsFinite(ItemHeight) || ItemHeight <= 0 || ItemHeight > RowPitch ||
            !double.IsFinite(IdleContentTop) || IdleContentTop < 0 || !double.IsFinite(ExpandedContentTop) || ExpandedContentTop < 0)
        { throw new ArgumentOutOfRangeException(nameof(Padding), "invalid_scene_layout"); }
    }
}

public sealed record SceneItem(string Id, string Label, LogicalRect Bounds);
public sealed record SceneSpace(string Id, string Name, LogicalRect Bounds, SpaceVisualState State,
    bool Expanded, LogicalRect? CloseBounds, LogicalRect? ExitBounds, ImmutableArray<SceneItem> Items,
    RgbColor Background, RgbColor Foreground, ImmutableArray<string> ItemIdentities = default);

/// <summary>A submitted scene is NOT proof of compositor/display presentation.</summary>
public sealed record SceneSnapshot(long FrameId, long HostGeneration, string MonitorId, double PixelsPerDip,
    ImmutableArray<SceneSpace> Spaces, SceneLayout? Layout = null)
{
    public static SceneSnapshot Capture(long frameId, SpaceDispatcher dispatcher, double scale, AppearanceEnvironment environment, SceneLayout? layout = null)
    {
        layout ??= SceneLayout.Default; layout.Validate();
        var spaces = ImmutableArray.CreateBuilder<SceneSpace>();
        foreach (var pair in dispatcher.VisualBounds.OrderBy(p => p.Key == dispatcher.Controller.FocusOwner ? 1 : 0).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var space = dispatcher.Workspace.Spaces[Guid.Parse(pair.Key)];
            var state = dispatcher.Controller.GetState(pair.Key).VisualState;
            bool expanded = state is SpaceVisualState.Focusing or SpaceVisualState.Focused;
            var b = pair.Value;
            var palette = EngineeringFixture.Resolve(space.Id, expanded, environment).ContentPalette;
            int columns = Math.Max(1, (int)((b.Width - 2 * layout.Padding) / layout.ColumnPitch));
            double top = expanded ? layout.ExpandedContentTop : layout.IdleContentTop;
            int rows = Math.Max(0, (int)((b.Height - top - layout.BottomPadding) / layout.RowPitch));
            int capacity = expanded ? columns * rows : Math.Min(space.PreviewPolicy.CapacityOverride ?? columns * rows, columns * rows);
            var items = space.ItemIds.Take(capacity).Select((id, index) => new SceneItem(id.ToString("D"),
                dispatcher.Workspace.ItemStore.Items[id].Metadata.DisplayName,
                new(b.X + layout.Padding + index % columns * layout.ColumnPitch, b.Y + top + index / columns * layout.RowPitch, layout.ItemWidth, layout.ItemHeight))).ToImmutableArray();
            spaces.Add(new(pair.Key, space.Name, b, state, expanded,
                expanded ? new(b.Right - 88, b.Y + 8, 40, 28) : null,
                expanded ? new(b.Right - 44, b.Y + 8, 36, 28) : null,
                items, palette.Background, palette.Foreground, space.ItemIds.Select(id => id.ToString("D")).ToImmutableArray()));
        }
        return new(frameId, dispatcher.Controller.InputGeneration, "engineering-primary", scale, spaces.ToImmutable(), layout);
    }
    public SurfaceHit? HitTest(int x, int y)
    {
        bool Inside(LogicalRect b) => x >= Math.Round(b.X * PixelsPerDip) && x < Math.Round(b.Right * PixelsPerDip)
            && y >= Math.Round(b.Y * PixelsPerDip) && y < Math.Round(b.Bottom * PixelsPerDip);
        foreach (var space in Spaces.Reverse())
        {
            if (!Inside(space.Bounds)) { continue; }
            if (space.CloseBounds is { } close && Inside(close)) { return new(space.Id, Close: true); }
            if (space.ExitBounds is { } exit && Inside(exit)) { return new(space.Id, Exit: true); }
            foreach (var item in space.Items) { if (Inside(item.Bounds)) { return new(space.Id, item.Id); } }
            return new(space.Id);
        }
        return null;
    }
}

/// <summary>Factory owns the renderer/source; HostLease alone authorizes its disposal.</summary>
public interface IHostRenderer : IDisposable
{
    nint Create(int left, int top, int width, int height, Func<nint, uint, nuint, nint, nint?> callback);
    void Submit(SceneSnapshot scene);
    void Reset() { }
    int Run();
    void RequestStop(int exitCode);
}
