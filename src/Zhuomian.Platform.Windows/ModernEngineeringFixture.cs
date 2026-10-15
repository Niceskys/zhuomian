using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Platform.Windows;

/// <summary>Memory-only synthetic content, isolated from user resources and E3.</summary>
public static class ModernEngineeringFixture
{
    public static Guid StudyId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000003");
    public static LogicalRect SavedBounds(Guid id) => id == EngineeringFixture.HoverId ? new(32, 40, 320, 200)
        : id == EngineeringFixture.ClickId ? new(384, 40, 260, 180) : new(32, 280, 380, 240);
    public static Workspace Create()
    {
        var workspace = Workspace.Create(Guid.Parse("30000000-0000-0000-0000-000000000002"));
        var ids = new[] { EngineeringFixture.HoverId, EngineeringFixture.ClickId, StudyId };
        var names = new[] { "Work", "Study", "Play" };
        var counts = new[] { 8, 6, 12 };
        int identity = 0;
        for (int index = 0; index < ids.Length; index++)
        {
            workspace = workspace.CreateSpace(ids[index], names[index], new(index == 1 ? ActivationMode.Click : ActivationMode.Hover));
            for (int item = 0; item < counts[index]; item++)
            {
                var id = Guid.Parse($"20000000-0000-0000-0000-{++identity:000000000000}");
                workspace = workspace.AddItemToSpace(ids[index], new UrlItem(id, new($"{names[index]} {item + 1:00}"), new($"https://example.invalid/fake/{identity}")));
            }
        }
        return workspace;
    }
}
