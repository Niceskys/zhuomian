using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Platform.Windows;

/// <summary>Deterministic, memory-only engineering data. No actual resource may be launched.</summary>
public static class EngineeringFixture
{
    public static Guid HoverId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static Guid ClickId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static Guid ItemId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static Workspace Create() => Workspace.Create(Guid.Parse("30000000-0000-0000-0000-000000000001"))
        .CreateSpace(HoverId, "A / Hover 20ms", new(ActivationMode.Hover))
        .CreateSpace(ClickId, "B / Click - continuous candidate", new(ActivationMode.Click))
        .AddItemToSpace(HoverId, new UrlItem(ItemId, new("FAKE Item - counter only"), new("https://example.invalid/")))
        .AddReference(ClickId, ItemId);
    public static LogicalRect SavedBounds(Guid id) => id == HoverId ? new(40, 60, 300, 200) : new(380, 60, 300, 200);
    public static ExpansionConstraints Expansion { get; } = new(1.5, new(300, 200), new(600, 420));
    public static ReadablePalette Palette { get; } = new(new(30, 34, 42), new(245, 245, 245));
    public static AppearanceConfiguration Appearance { get; } = new(new AppearanceLayers(
        new AppearanceOverrides { Material = PropertyOverride.Set(SurfaceMaterial.SimulatedGlass), Opacity = PropertyOverride.Set(0.5) }));
    public static AppearanceResolution Resolve(Guid id, bool focused, AppearanceEnvironment environment) =>
        AppearanceResolver.Resolve(id, focused ? AppearanceState.Focused : AppearanceState.Idle, Appearance,
            new(Palette, 1), environment, new(false, false, false), new(false, false, false, 0, 0, 0, 0, 0));
}
