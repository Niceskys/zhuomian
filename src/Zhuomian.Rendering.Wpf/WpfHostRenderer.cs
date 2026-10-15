using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Rendering.Wpf;

/// <summary>One HwndSource, no Window/Application/child HWND, hook, session or animation authority.</summary>
public sealed class WpfHostRenderer : IHostRenderer
{
    private HwndSource? _source;
    private readonly Canvas _root = new() { IsHitTestVisible = false, Focusable = false, Background = Brushes.Black };
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherFrame _frame = new();
    private Func<nint, uint, nuint, nint, nint?>? _callback;
    private bool _disposed;
    private int _viewportWidth, _viewportHeight;
    public SceneSnapshot? SubmittedScene { get; private set; }
    public nint Handle => _source?.Handle ?? 0;
    public bool HasRootVisual => _source?.RootVisual is not null;
    public nint Create(int left, int top, int width, int height, Func<nint, uint, nuint, nint, nint?> callback)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) { throw new InvalidOperationException("sta_required"); }
        if (_source is not null) { throw new InvalidOperationException("source_already_created"); }
        _callback = callback;
        _viewportWidth = width; _viewportHeight = height;
        try
        {
            _source = new(new HwndSourceParameters("Zhuomian UI-I1 ENGINEERING ONLY")
            {
                PositionX = left,
                PositionY = top,
                Width = width,
                Height = height,
                WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080,
                HwndSourceHook = Hook,
                UsesPerPixelOpacity = false,
                AdjustSizingForNonClientArea = false
            });
            _source.RootVisual = _root;
            _dispatcher.UnhandledException += OnUnhandledException;
            return _source.Handle;
        }
        catch { Dispose(); throw; }
    }
    private nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled)
    {
        var result = _callback?.Invoke(window, unchecked((uint)message), unchecked((nuint)wParam), lParam);
        if (result is { } value) { handled = true; return value; }
        return 0;
    }
    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Fail closed on the owner thread. Never export exception payloads or continue rendering.
        e.Handled = true;
        _callback?.Invoke(Handle, 0x02, 0, 0);
        RequestStop(1);
    }
    public void Submit(SceneSnapshot scene)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(SubmittedScene, scene)) { return; }
        _root.Children.Clear();
        foreach (var space in scene.Spaces)
        {
            var background = Brush(space.Background);
            var foreground = Brush(space.Foreground);
            var card = new Border
            {
                Tag = "space:" + space.Id,
                Background = background,
                BorderBrush = new SolidColorBrush(Color.FromRgb(99, 110, 130)),
                BorderThickness = new(1),
                CornerRadius = new(14),
                ClipToBounds = true
            };
            Place(card, space.Bounds, scene.PixelsPerDip);
            // All geometry comes from the coordinator. WPF never reads Core or chooses Focus.
            var title = Label(space.Name, foreground, 17, FontWeights.SemiBold);
            Place(title, new(space.Bounds.X + 16, space.Bounds.Y + 10, space.Bounds.Width - (space.Expanded ? 112 : 32), 28), scene.PixelsPerDip);
            if (space.Expanded)
            {
                var search = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
                    CornerRadius = new(7),
                    Child = Label("Search in Space", foreground, 13, FontWeights.Normal),
                    Padding = new(12, 6, 0, 0)
                };
                Place(search, new(space.Bounds.X + 16, space.Bounds.Y + 48, space.Bounds.Width - 32, 36), scene.PixelsPerDip);
                Place(Label("All items     ·     Grid     ·     Sort     ·     Settings", foreground, 12, FontWeights.Normal),
                    new(space.Bounds.X + 16, space.Bounds.Y + 98, space.Bounds.Width - 32, 24), scene.PixelsPerDip);
                Place(Label("Back", foreground, 11, FontWeights.Normal), space.CloseBounds!, scene.PixelsPerDip);
                Place(Label("Exit", foreground, 11, FontWeights.Normal), space.ExitBounds!, scene.PixelsPerDip);
            }
            foreach (var item in space.Items)
            {
                var tile = new StackPanel { Tag = "item:" + space.Id + "/" + item.Id, HorizontalAlignment = HorizontalAlignment.Center };
                int identity = Guid.Parse(item.Id).ToByteArray()[15];
                var iconBackground = new RgbColor(70 + identity * 19 % 100, 110 + identity * 13 % 100, 150 + identity * 7 % 100);
                var iconForeground = iconBackground.ContrastRatio(new(255, 255, 255)) >= ReadablePalette.MinimumContrastRatio ? Brushes.White : Brushes.Black;
                tile.Children.Add(new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new(8),
                    Background = Brush(iconBackground),
                    Child = new TextBlock
                    {
                        Text = identity.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
                        Foreground = iconForeground,
                        FontSize = 12,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
                tile.Children.Add(Label(item.Label, foreground, 10, FontWeights.Normal));
                Place(tile, item.Bounds, scene.PixelsPerDip);
            }
        }
        SubmittedScene = scene;
        // Lay out against the actual coordinator-created host viewport even while hidden.
        // This establishes WPF layout, never compositor/display presentation.
        _root.InvalidateMeasure(); _root.InvalidateArrange();
        _root.Measure(new(_viewportWidth / scene.PixelsPerDip, _viewportHeight / scene.PixelsPerDip));
        _root.Arrange(new Rect(0, 0, _viewportWidth / scene.PixelsPerDip, _viewportHeight / scene.PixelsPerDip));
        _root.UpdateLayout();
    }
    private void Place(FrameworkElement element, LogicalRect bounds, double scale)
    {
        double x = Math.Round(bounds.X * scale) / scale, y = Math.Round(bounds.Y * scale) / scale;
        element.Width = Math.Max(0, Math.Round(bounds.Right * scale) / scale - x);
        element.Height = Math.Max(0, Math.Round(bounds.Bottom * scale) / scale - y);
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y);
        _root.Children.Add(element);
    }
    private static SolidColorBrush Brush(RgbColor c) => new(Color.FromRgb((byte)c.Red, (byte)c.Green, (byte)c.Blue));
    private static TextBlock Label(string text, Brush color, double size, FontWeight weight) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight,
        FontFamily = new("Segoe UI"),
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };
    internal bool MatchesSubmittedGeometry(SceneSnapshot scene)
    {
        _dispatcher.VerifyAccess();
        var visuals = _root.Children.OfType<FrameworkElement>().Where(element => element.Tag is string)
            .ToDictionary(element => (string)element.Tag);
        bool Matches(string key, LogicalRect bounds)
        {
            if (!visuals.Remove(key, out var visual)) { return false; }
            double scale = scene.PixelsPerDip;
            var offset = VisualTreeHelper.GetOffset(visual);
            return visual.IsMeasureValid && visual.IsArrangeValid &&
                visual.RenderSize.Width == visual.Width && visual.RenderSize.Height == visual.Height &&
                offset.X == Canvas.GetLeft(visual) && offset.Y == Canvas.GetTop(visual) &&
                Canvas.GetLeft(visual) == Math.Round(bounds.X * scale) / scale &&
                Canvas.GetTop(visual) == Math.Round(bounds.Y * scale) / scale &&
                visual.Width == Math.Round(bounds.Right * scale) / scale - Math.Round(bounds.X * scale) / scale &&
                visual.Height == Math.Round(bounds.Bottom * scale) / scale - Math.Round(bounds.Y * scale) / scale;
        }
        foreach (var space in scene.Spaces)
        {
            if (!Matches("space:" + space.Id, space.Bounds)) { return false; }
            foreach (var item in space.Items)
            {
                string key = "item:" + space.Id + "/" + item.Id;
                if (!visuals.TryGetValue(key, out var visual) || visual is not StackPanel tile ||
                    tile.Children[0] is not Border icon || icon.Background is not SolidColorBrush backing ||
                    icon.Child is not TextBlock glyph || glyph.Foreground is not SolidColorBrush text) { return false; }
                var background = new RgbColor(backing.Color.R, backing.Color.G, backing.Color.B);
                if (background.ContrastRatio(new(text.Color.R, text.Color.G, text.Color.B)) < ReadablePalette.MinimumContrastRatio) { return false; }
                if (!Matches(key, item.Bounds)) { return false; }
            }
        }
        return visuals.Count == 0;
    }
    internal void SaveOffscreen(string path)
    {
        _dispatcher.VerifyAccess();
        if (_source is null || Zhuomian.Platform.Windows.NativeMethods.IsWindowVisible(Handle))
        { throw new InvalidOperationException("hidden_capture_only"); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var source = _source;
        // A hidden HwndTarget can suppress drawing updates. Capture the SAME visual tree
        // detached from that target, never pretend this is a live-window/display screenshot.
        source.RootVisual = null;
        try
        {
            InvalidateTree(_root);
            _root.Measure(new(1200, 800)); _root.Arrange(new Rect(0, 0, 1200, 800)); _root.UpdateLayout();
            File.WriteAllText(Path.ChangeExtension(path, ".scene.json"), JsonSerializer.Serialize(SubmittedScene));
            var bitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(_root);
            VerifyOffscreenPixels(bitmap);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path); encoder.Save(stream);
        }
        finally { if (!_disposed) { source.RootVisual = _root; } }
    }
    private void VerifyOffscreenPixels(RenderTargetBitmap bitmap)
    {
        var scene = SubmittedScene!;
        var tiles = _root.Children.OfType<StackPanel>().ToDictionary(tile => (string)tile.Tag);
        byte[] pixel = new byte[4];
        foreach (var space in scene.Spaces)
        {
            foreach (var item in space.Items)
            {
                var icon = (Border)tiles["item:" + space.Id + "/" + item.Id].Children[0];
                var sample = icon.TransformToAncestor(_root).Transform(new Point(5, 10));
                var hit = scene.HitTest((int)Math.Round(sample.X * scene.PixelsPerDip), (int)Math.Round(sample.Y * scene.PixelsPerDip));
                if (hit?.SpaceId != space.Id || hit?.ItemId != item.Id) { continue; } // Correctly occluded by a later card.
                bitmap.CopyPixels(new Int32Rect((int)sample.X, (int)sample.Y, 1, 1), pixel, 4, 0);
                var expected = ((SolidColorBrush)icon.Background).Color;
                if (pixel[0] != expected.B || pixel[1] != expected.G || pixel[2] != expected.R || pixel[3] != 255)
                { throw new InvalidOperationException("offscreen_visible_icon_pixel_mismatch"); }
            }
        }
    }
    private static void InvalidateTree(DependencyObject visual)
    {
        if (visual is UIElement element) { element.InvalidateMeasure(); element.InvalidateArrange(); element.InvalidateVisual(); }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(visual); index++)
        { InvalidateTree(VisualTreeHelper.GetChild(visual, index)); }
    }
    public int Run() { _dispatcher.VerifyAccess(); Dispatcher.PushFrame(_frame); return 0; }
    public void RequestStop(int exitCode) { _dispatcher.VerifyAccess(); _frame.Continue = false; }
    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) { return; }
        _disposed = true;
        _dispatcher.UnhandledException -= OnUnhandledException;
        _root.Children.Clear();
        if (_source is { } source)
        {
            source.RootVisual = null;
            source.RemoveHook(Hook);
            source.Dispose();
            _source = null;
        }
        SubmittedScene = null; _callback = null; _frame.Continue = false;
    }
}
