using Zhuomian.Core.Appearance;

namespace Zhuomian.Rendering.Wpf;

/// <summary>Presentation only. Geometry-affecting grid metrics belong to authoritative SceneLayout.</summary>
public sealed record PresentationTheme
{
    public double CardRadius { get; init; } = 14;
    public double BorderWidth { get; init; } = 1;
    public RgbColor BorderColor { get; init; } = new(99, 110, 130);
    public RgbColor SearchColor { get; init; } = new(128, 128, 128);
    public double SearchOpacity { get; init; } = 24d / 255;
    public double SearchRadius { get; init; } = 7;
    public double SearchInset { get; init; } = 12;
    public double IconSize { get; init; } = 30;
    public double IconRadius { get; init; } = 8;
    public double TitleFontSize { get; init; } = 17;
    public double BodyFontSize { get; init; } = 13;
    public double ToolbarFontSize { get; init; } = 12;
    public double ActionFontSize { get; init; } = 11;
    public double ItemFontSize { get; init; } = 10;
    public double TitleTop { get; init; } = 10;
    public double TitleHeight { get; init; } = 28;
    public double ExpandedTitleReserve { get; init; } = 112;
    public double SearchTop { get; init; } = 48;
    public double SearchHeight { get; init; } = 36;
    public double ToolbarTop { get; init; } = 98;
    public double ToolbarHeight { get; init; } = 24;
    // Future native hover adapter may use a transform, never change the grid's hit/layout bounds.
    public double HoverScale { get; init; } = 1.04;
}

/// <summary>Owned FrameworkElements only, excluding WPF internal template/composition objects.
/// LayoutInvalidated counts changed element geometry; duration/allocation cover synchronous Submit only.</summary>
public sealed class RendererDiagnostics
{
    public long VisualCreated { get; internal set; }
    public long VisualReused { get; internal set; }
    public long VisualRemoved { get; internal set; }
    public long SceneSubmitted { get; internal set; }
    public long LayoutInvalidated { get; internal set; }
    public long FullTreeRebuild { get; internal set; }
    public int LiveVisuals { get; internal set; }
    public long FrameUpdateTicks { get; internal set; }
    public long ManagedAllocation { get; internal set; }
}
