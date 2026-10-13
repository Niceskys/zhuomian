using System.Collections.Immutable;
using System.Windows;
using System.Windows.Media;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Ui.WpfCandidate;

internal sealed record SceneSpace(string Id, Rect Bounds, Rect Return, Rect Exit, ImmutableArray<Rect> Cells,
    AppearanceResolution Appearance, bool Expanded);

/// <summary>One immutable frame, shared by drawing, native Region and input. Coordinates are host-local DIPs.</summary>
internal sealed class CandidateScene
{
    private CandidateScene(ImmutableArray<SceneSpace> spaces) => Spaces = spaces;
    internal ImmutableArray<SceneSpace> Spaces { get; }
    internal static readonly ReadablePalette Palette = new(new(25, 30, 40), new(242, 245, 250));
    private static readonly AppearanceConfiguration Theme = new(new AppearanceLayers(new AppearanceOverrides
    {
        Material = PropertyOverride.Set(SurfaceMaterial.Solid),
        CornerRadius = PropertyOverride.Set(16d)
    }));
    internal static AppearanceResolution Resolve(Guid id, bool expanded, AppearanceEnvironment environment) =>
        AppearanceResolver.Resolve(id, expanded ? AppearanceState.Focused : AppearanceState.Idle, Theme,
            new(Palette, 1), environment.HighContrast && !environment.ReducedMotion
                ? new(true, true, true, environment.HighContrastPalette) : environment,
            new(false, false, false), new(false, false, false, 24, 0, 0, 0, 0));
    internal static Rect Snap(LogicalRect bounds, double scale)
    {
        double left = Math.Round(bounds.X * scale), top = Math.Round(bounds.Y * scale);
        return new(left / scale, top / scale, (Math.Round(bounds.Right * scale) - left) / scale,
            (Math.Round(bounds.Bottom * scale) - top) / scale);
    }
    internal static CandidateScene Create(SpaceDispatcher dispatcher, AppearanceEnvironment environment, double scale)
    {
        var spaces = ImmutableArray.CreateBuilder<SceneSpace>();
        // Focus owner is last in paint, first in hit, exactly once per identity.
        foreach (string id in dispatcher.VisualBounds.Keys.OrderBy(id => id == dispatcher.Controller.FocusOwner ? 1 : 0))
        {
            var bounds = Snap(dispatcher.VisualBounds[id], scale);
            bool expanded = dispatcher.Controller.FocusOwner == id;
            var appearance = Resolve(Guid.Parse(id), expanded, environment);
            var grid = IconGridLayout.Calculate(new(bounds.X, bounds.Y, bounds.Width, bounds.Height), 4,
                new(new(56, 62), 12, 12, new(18, 70, 18, 42), 4, 4));
            var cells = ImmutableArray.CreateBuilder<Rect>();
            for (int i = 0; i < Math.Min(4, grid.GeometricPreviewCapacity); i++)
            {
                var cell = grid.GetCell(i);
                cells.Add(new(grid.ContentArea.X + cell.X, grid.ContentArea.Y + cell.Y, cell.Width, cell.Height));
            }
            spaces.Add(new(id, bounds, new(bounds.Right - 100, bounds.Top + 14, 48, 28),
                new(bounds.Right - 44, bounds.Top + 14, 28, 28), cells.ToImmutable(), appearance, expanded));
        }
        return new(spaces.ToImmutable());
    }
    internal SurfaceHit? Hit(double x, double y)
    {
        var point = new Point(x, y);
        foreach (var space in Spaces.Reverse())
        {
            if (!new RectangleGeometry(space.Bounds, space.Appearance.Effective.CornerRadius,
                space.Appearance.Effective.CornerRadius).FillContains(point)) { continue; }
            if (space.Exit.Contains(point)) { return new(space.Id, Exit: true); }
            if (space.Expanded && space.Return.Contains(point)) { return new(space.Id, Close: true); }
            // Four synthetic tiles share one fake counter resource. No launch authority added.
            if (space.Cells.Any(cell => cell.Contains(point))) { return new(space.Id, EngineeringFixture.ItemId.ToString("D")); }
            return new(space.Id);
        }
        return null;
    }
}
