using System.Diagnostics;
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
            _source = new(new HwndSourceParameters("Zhuomian UI-I2 ENGINEERING ONLY")
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
    private readonly Dictionary<string, SpaceVisual> _spaces = new(StringComparer.Ordinal);
    private readonly PresentationTheme _theme;
    public RendererDiagnostics Diagnostics { get; } = new();
    public WpfHostRenderer(PresentationTheme? theme = null) { _theme = theme ?? new(); }
    public void Submit(SceneSnapshot scene)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        if (ReferenceEquals(SubmittedScene, scene)) { return; }
        long start = Stopwatch.GetTimestamp(), allocation = GC.GetAllocatedBytesForCurrentThread();
        try
        {
            if (_spaces.Count == 0 && scene.Spaces.Length > 0) { Diagnostics.FullTreeRebuild++; }
            int order = 0;
            foreach (string id in _spaces.Keys.Where(id => !scene.Spaces.Any(s => s.Id == id)).ToArray())
            { Remove(_spaces[id]); _spaces.Remove(id); }
            foreach (var space in scene.Spaces)
            {
                if (!_spaces.TryGetValue(space.Id, out var visual))
                {
                    visual = new(space.Id, _theme);
                    _spaces.Add(space.Id, visual);
                    foreach (var element in visual.Chrome) { Add(element, element == visual.Search ? 2 : 1); }
                }
                else { Diagnostics.VisualReused += 7; } // card, title, search + child, toolbar, Back, Exit
                var previous = visual.Scene;
                if (previous?.Name != space.Name) { visual.Title.Text = space.Name; }
                if (previous?.Background != space.Background) { visual.Card.Background = Brush(space.Background); }
                if (previous?.Foreground != space.Foreground)
                {
                    var foreground = Brush(space.Foreground);
                    foreach (var label in visual.Labels) { label.Foreground = foreground; }
                }
                Place(visual.Card, space.Bounds, scene.PixelsPerDip, order++);
                Place(visual.Title, new(space.Bounds.X + SceneLayout.Padding, space.Bounds.Y + _theme.TitleTop,
                    space.Bounds.Width - (space.Expanded ? _theme.ExpandedTitleReserve : 2 * SceneLayout.Padding), _theme.TitleHeight), scene.PixelsPerDip, order++);
                foreach (var control in visual.ExpandedControls)
                { control.Visibility = space.Expanded ? Visibility.Visible : Visibility.Collapsed; }
                if (space.Expanded)
                {
                    Place(visual.Search, new(space.Bounds.X + SceneLayout.Padding, space.Bounds.Y + _theme.SearchTop,
                        space.Bounds.Width - 2 * SceneLayout.Padding, _theme.SearchHeight), scene.PixelsPerDip, order++);
                    Place(visual.Toolbar, new(space.Bounds.X + SceneLayout.Padding, space.Bounds.Y + _theme.ToolbarTop,
                        space.Bounds.Width - 2 * SceneLayout.Padding, _theme.ToolbarHeight), scene.PixelsPerDip, order++);
                    Place(visual.Back, space.CloseBounds!, scene.PixelsPerDip, order++);
                    Place(visual.Exit, space.ExitBounds!, scene.PixelsPerDip, order++);
                }
                foreach (string id in visual.Items.Keys.Where(id => space.ItemIdentities.IsDefault ? !space.Items.Any(i => i.Id == id) : !space.ItemIdentities.Contains(id)).ToArray())
                { Remove(visual.Items[id].Tile, 4); visual.Items.Remove(id); }
                foreach (var cached in visual.Items)
                { cached.Value.Tile.Visibility = space.Items.Any(item => item.Id == cached.Key) ? Visibility.Visible : Visibility.Collapsed; }
                foreach (var item in space.Items)
                {
                    if (!visual.Items.TryGetValue(item.Id, out var tile))
                    { tile = new(space.Id, item.Id, _theme); visual.Items.Add(item.Id, tile); Add(tile.Tile, 4); }
                    else { Diagnostics.VisualReused += 4; }
                    if (tile.Scene?.Label != item.Label) { tile.Label.Text = item.Label; }
                    if (tile.Foreground != space.Foreground) { tile.Label.Foreground = Brush(space.Foreground); tile.Foreground = space.Foreground; }
                    tile.Tile.Visibility = Visibility.Visible;
                    Place(tile.Tile, item.Bounds, scene.PixelsPerDip, order++);
                    tile.Scene = item;
                }
                visual.Scene = space;
            }
            SubmittedScene = scene;
            Diagnostics.SceneSubmitted++;
            // Submit/layout are CPU-side operations, never display-present acknowledgements.
            _root.Measure(new(_viewportWidth / scene.PixelsPerDip, _viewportHeight / scene.PixelsPerDip));
            _root.Arrange(new Rect(0, 0, _viewportWidth / scene.PixelsPerDip, _viewportHeight / scene.PixelsPerDip));
            _root.UpdateLayout();
        }
        catch
        {
            // Partial updates cannot remain an authoritative picture. Coordinator closes the lease.
            Reset();
            throw;
        }
        finally
        {
            Diagnostics.FrameUpdateTicks = Stopwatch.GetTimestamp() - start;
            Diagnostics.ManagedAllocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
        }
    }
    private void Add(FrameworkElement element, int count = 1)
    { _root.Children.Add(element); Diagnostics.VisualCreated += count; Diagnostics.LiveVisuals += count; }
    private void Remove(FrameworkElement element, int count = 1)
    { _root.Children.Remove(element); Diagnostics.VisualRemoved += count; Diagnostics.LiveVisuals -= count; }
    private void Remove(SpaceVisual visual)
    {
        foreach (var tile in visual.Items.Values) { Remove(tile.Tile, 4); }
        visual.Items.Clear();
        foreach (var element in visual.Chrome) { Remove(element, element == visual.Search ? 2 : 1); }
        visual.Scene = null;
    }
    public void Reset()
    {
        _dispatcher.VerifyAccess();
        foreach (var visual in _spaces.Values) { Remove(visual); }
        _spaces.Clear(); SubmittedScene = null;
    }
    private void Place(FrameworkElement element, LogicalRect bounds, double scale, int order)
    {
        double x = Math.Round(bounds.X * scale) / scale, y = Math.Round(bounds.Y * scale) / scale;
        double width = Math.Max(0, Math.Round(bounds.Right * scale) / scale - x);
        double height = Math.Max(0, Math.Round(bounds.Bottom * scale) / scale - y);
        bool changed = element.Width != width || element.Height != height || Canvas.GetLeft(element) != x || Canvas.GetTop(element) != y;
        if (element.Width != width) { element.Width = width; }
        if (element.Height != height) { element.Height = height; }
        if (Canvas.GetLeft(element) != x) { Canvas.SetLeft(element, x); }
        if (Canvas.GetTop(element) != y) { Canvas.SetTop(element, y); }
        if (Panel.GetZIndex(element) != order) { Panel.SetZIndex(element, order); }
        if (changed) { Diagnostics.LayoutInvalidated++; }
    }
    private sealed class SpaceVisual
    {
        internal readonly Border Card, Search;
        internal readonly TextBlock Title, Toolbar, Back, Exit;
        internal readonly FrameworkElement[] Chrome, ExpandedControls;
        internal readonly TextBlock[] Labels;
        internal readonly Dictionary<string, ItemVisual> Items = new(StringComparer.Ordinal);
        internal SceneSpace? Scene;
        internal SpaceVisual(string id, PresentationTheme theme)
        {
            Card = new() { Tag = "space:" + id, BorderBrush = Brush(theme.BorderColor), BorderThickness = new(theme.BorderWidth), CornerRadius = new(theme.CardRadius), ClipToBounds = true };
            Title = Label("", Brushes.White, theme.TitleFontSize, FontWeights.SemiBold);
            var searchLabel = Label("Search in Space", Brushes.White, theme.BodyFontSize, FontWeights.Normal);
            Search = new() { Background = Brush(theme.SearchColor, theme.SearchOpacity), CornerRadius = new(theme.SearchRadius), Child = searchLabel, Padding = new(theme.SearchInset, 6, 0, 0) };
            Toolbar = Label("All items     ·     Grid     ·     Sort     ·     Settings", Brushes.White, theme.ToolbarFontSize, FontWeights.Normal);
            Back = Label("Back", Brushes.White, theme.ActionFontSize, FontWeights.Normal);
            Exit = Label("Exit", Brushes.White, theme.ActionFontSize, FontWeights.Normal);
            Chrome = [Card, Title, Search, Toolbar, Back, Exit];
            ExpandedControls = [Search, Toolbar, Back, Exit];
            Labels = [Title, searchLabel, Toolbar, Back, Exit];
        }
    }
    private sealed class ItemVisual
    {
        internal readonly StackPanel Tile;
        internal readonly TextBlock Label;
        internal SceneItem? Scene;
        internal RgbColor? Foreground;
        internal ItemVisual(string spaceId, string itemId, PresentationTheme theme)
        {
            int identity = Guid.Parse(itemId).ToByteArray()[15];
            var background = new RgbColor(70 + identity * 19 % 100, 110 + identity * 13 % 100, 150 + identity * 7 % 100);
            Tile = new()
            {
                Tag = "item:" + spaceId + "/" + itemId,
                HorizontalAlignment = HorizontalAlignment.Center,
                RenderTransformOrigin = new(.5, .5),
                RenderTransform = new ScaleTransform(1, 1)
            };
            Tile.Children.Add(new Border
            {
                Width = theme.IconSize,
                Height = theme.IconSize,
                CornerRadius = new(theme.IconRadius),
                Background = Brush(background),
                Child = new TextBlock
                {
                    Text = identity.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
                    Foreground = background.ContrastRatio(new(255, 255, 255)) >= ReadablePalette.MinimumContrastRatio ? Brushes.White : Brushes.Black,
                    FontSize = theme.ToolbarFontSize,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            Label = WpfHostRenderer.Label("", Brushes.White, theme.ItemFontSize, FontWeights.Normal);
            Tile.Children.Add(Label);
        }
    }
    private static SolidColorBrush Brush(RgbColor c, double opacity = 1)
    {
        var brush = new SolidColorBrush(Color.FromRgb((byte)c.Red, (byte)c.Green, (byte)c.Blue));
        brush.Opacity = opacity;
        brush.Freeze(); return brush;
    }
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
    internal string? GeometryMismatch { get; private set; }
    internal bool MatchesSubmittedGeometry(SceneSnapshot scene)
    {
        _dispatcher.VerifyAccess();
        GeometryMismatch = null;
        var visuals = _root.Children.OfType<FrameworkElement>().Where(element => element.Tag is string && element.Visibility == Visibility.Visible)
            .ToDictionary(element => (string)element.Tag);
        bool Matches(string key, LogicalRect bounds)
        {
            if (!visuals.Remove(key, out var visual)) { GeometryMismatch = "missing:" + key; return false; }
            double scale = scene.PixelsPerDip;
            var offset = VisualTreeHelper.GetOffset(visual);
            bool matches = visual.IsMeasureValid && visual.IsArrangeValid &&
                visual.RenderSize.Width == visual.Width && visual.RenderSize.Height == visual.Height &&
                offset.X == Canvas.GetLeft(visual) && offset.Y == Canvas.GetTop(visual) &&
                Canvas.GetLeft(visual) == Math.Round(bounds.X * scale) / scale &&
                Canvas.GetTop(visual) == Math.Round(bounds.Y * scale) / scale &&
                visual.Width == Math.Round(bounds.Right * scale) / scale - Math.Round(bounds.X * scale) / scale &&
                visual.Height == Math.Round(bounds.Bottom * scale) / scale - Math.Round(bounds.Y * scale) / scale;
            if (!matches)
            { GeometryMismatch = FormattableString.Invariant($"{key}: valid={visual.IsMeasureValid}/{visual.IsArrangeValid} render={visual.RenderSize} declared={visual.Width}/{visual.Height} offset={offset} canvas={Canvas.GetLeft(visual)}/{Canvas.GetTop(visual)} expected={bounds} scale={scale}"); }
            return matches;
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
        if (visuals.Count != 0) { GeometryMismatch = "extra:" + string.Join(",", visuals.Keys); }
        return visuals.Count == 0;
    }
    internal void SaveOffscreen(string path)
    {
        _dispatcher.VerifyAccess();
        if (_source is null || Zhuomian.Platform.Windows.NativeMethods.IsWindowVisible(Handle))
        { throw new InvalidOperationException("hidden_capture_only"); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Offscreen rendering of the existing submitted tree; never a display-present acknowledgement.
        InvalidateTree(_root);
        _root.Measure(new(1200, 800)); _root.Arrange(new Rect(0, 0, 1200, 800)); _root.UpdateLayout();
        File.WriteAllText(Path.ChangeExtension(path, ".scene.json"), JsonSerializer.Serialize(SubmittedScene));
        var bitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        VerifyOffscreenPixels(bitmap);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
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
        Reset();
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
