using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zhuomian.Core.Appearance;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Ui.WpfCandidate;

/// <summary>Retained WPF drawing; no Rendering subscription, storyboard, focusable control or idle loop.</summary>
internal sealed class CandidateSurface : FrameworkElement
{
    private static readonly string[] Glyphs = ["N", "D", "C", "F"];
    private static readonly string[] Labels = ["Notes", "Design", "Code", "Files"];
    internal CandidateScene? Scene { get; private set; }
    internal SurfaceHit? Hover { get; set; }
    internal int PaintCount { get; private set; }
    internal CandidateSurface() { Focusable = false; IsHitTestVisible = false; }
    internal void Present(CandidateScene scene) { Scene = scene; InvalidateVisual(); }
    internal void Release() { Scene = null; Hover = null; InvalidateVisual(); }
    private static SolidColorBrush Brush(RgbColor color)
    {
        var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)); brush.Freeze(); return brush;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        PaintCount++;
        if (Scene is null) { return; }
        foreach (var space in Scene.Spaces)
        {
            var bounds = space.Bounds;
            var palette = space.Appearance.ContentPalette;
            var foreground = Brush(palette.Foreground);
            var background = Brush(palette.Background);
            bool highContrast = (space.Appearance.Reasons & AppearanceReason.HighContrast) != 0;
            var accent = highContrast ? foreground : new SolidColorBrush(Color.FromRgb(127, 183, 255));
            double radius = space.Appearance.Effective.CornerRadius;
            dc.PushClip(new RectangleGeometry(bounds, radius, radius));
            dc.DrawRoundedRectangle(background, new(foreground, highContrast ? 2 : 0.5), bounds, radius, radius);
            if (!highContrast)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(32, 39, 52)), null,
                    new(bounds.X, bounds.Y, bounds.Width, 57));
            }
            Text(dc, space.Id == EngineeringFixture.HoverId.ToString("D") ? "Ideas" : "Workspace", bounds.X + 18, bounds.Y + 14, 18, foreground, bounds.Width - 126);
            Text(dc, space.Id == EngineeringFixture.HoverId.ToString("D") ? "A  /  hover in place" : "B  /  click to center", bounds.X + 18, bounds.Y + 38, 10, foreground, bounds.Width - 126);
            if (space.Expanded) { Text(dc, "Back", space.Return.X, space.Return.Y + 6, 11, foreground, 48); }
            Text(dc, "×", space.Exit.X + 7, space.Exit.Y + 2, 18, foreground, 24);
            for (int i = 0; i < space.Cells.Length; i++)
            {
                var cell = space.Cells[i];
                bool hovered = Hover?.SpaceId == space.Id && Hover?.ItemId is not null;
                if (hovered) { dc.DrawRoundedRectangle(null, new(accent, 1), cell, 8, 8); }
                var icon = new Rect(cell.X + 8, cell.Y + 2, 40, 36);
                dc.DrawRoundedRectangle(accent, null, icon, 9, 9);
                var ink = highContrast ? background : new SolidColorBrush(Color.FromRgb(25, 30, 40));
                Text(dc, Glyphs[i], icon.X + 13, icon.Y + 7, 16, ink, 28);
                Text(dc, Labels[i], cell.X, cell.Y + 43, 11, foreground, cell.Width);
            }
            Text(dc, space.Expanded ? "CONTENT  /  synthetic resources only" : "PREVIEW  /  click a tile to count", bounds.X + 18, bounds.Bottom - 29, 10, foreground, bounds.Width - 36);
            dc.Pop();
        }
    }
    private void Text(DrawingContext dc, string value, double x, double y, double size, Brush foreground, double width)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, width), MaxTextHeight = size * 1.6, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(text, new(x, y));
    }
    internal void SaveOffscreenRender(string path)
    {
        // Actual WPF render output of the same retained scene. NOT a screen capture or visible HWND proof.
        Measure(new(Width, Height)); Arrange(new(0, 0, Width, Height)); UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = System.IO.File.Create(path); encoder.Save(output);
    }
}
