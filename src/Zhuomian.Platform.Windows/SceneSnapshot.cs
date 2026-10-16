using System.Collections.Immutable;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;

namespace Zhuomian.Platform.Windows;

public sealed record SceneItem(string Id, string Label, LogicalRect Bounds);
public sealed record SceneSpace(string Id, string Name, LogicalRect Bounds, SpaceVisualState State,
    bool Expanded, LogicalRect? CloseBounds, LogicalRect? ExitBounds, ImmutableArray<SceneItem> Items,
    RgbColor Background, RgbColor Foreground);

/// <summary>A submitted scene is NOT proof of compositor/display presentation.</summary>
public sealed record SceneSnapshot(long FrameId, long HostGeneration, string MonitorId, double PixelsPerDip,
    ImmutableArray<SceneSpace> Spaces)
{
    public static SceneSnapshot Capture(long frameId, SpaceDispatcher dispatcher, double scale, AppearanceEnvironment environment)
    {
        var spaces = ImmutableArray.CreateBuilder<SceneSpace>();
        foreach (var pair in dispatcher.VisualBounds.OrderBy(p => p.Key == dispatcher.Controller.FocusOwner ? 1 : 0).ThenBy(p => p.Key, StringComparer.Ordinal))
        {
            var space = dispatcher.Workspace.Spaces[Guid.Parse(pair.Key)];
            var state = dispatcher.Controller.GetState(pair.Key).VisualState;
            bool expanded = state is SpaceVisualState.Focusing or SpaceVisualState.Focused;
            var b = pair.Value;
            var palette = EngineeringFixture.Resolve(space.Id, expanded, environment).ContentPalette;
            int columns = Math.Max(1, (int)((b.Width - 32) / 64));
            double top = expanded ? 136 : 44;
            int rows = Math.Max(0, (int)((b.Height - top - 12) / 56));
            int capacity = expanded ? columns * rows : Math.Min(space.PreviewPolicy.CapacityOverride ?? columns * rows, columns * rows);
            var items = space.ItemIds.Take(capacity).Select((id, index) => new SceneItem(id.ToString("D"),
                dispatcher.Workspace.ItemStore.Items[id].Metadata.DisplayName,
                new(b.X + 16 + index % columns * 64, b.Y + top + index / columns * 56, 56, 52))).ToImmutableArray();
            spaces.Add(new(pair.Key, space.Name, b, state, expanded,
                expanded ? new(b.Right - 88, b.Y + 8, 40, 28) : null,
                expanded ? new(b.Right - 44, b.Y + 8, 36, 28) : null,
                items, palette.Background, palette.Foreground));
        }
        return new(frameId, dispatcher.Controller.InputGeneration, "engineering-primary", scale, spaces.ToImmutable());
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
    int Run();
    void RequestStop(int exitCode);
}
