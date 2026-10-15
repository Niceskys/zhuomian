using System.IO;
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
                var tile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                int identity = int.Parse(item.Id.AsSpan(item.Id.Length - 4), System.Globalization.CultureInfo.InvariantCulture);
                tile.Children.Add(new Border
                {
                    Width = 30,
                    Height = 30,
                    CornerRadius = new(8),
                    Background = new SolidColorBrush(Color.FromRgb((byte)(70 + identity * 19 % 100), (byte)(110 + identity * 13 % 100), (byte)(150 + identity * 7 % 100))),
                    Child = new TextBlock
                    {
                        Text = identity.ToString("00", System.Globalization.CultureInfo.InvariantCulture),
                        Foreground = Brushes.White,
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
        // This schedules layout/rendering; it does not acknowledge display presentation.
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
    internal void SaveOffscreen(string path)
    {
        _dispatcher.VerifyAccess();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _root.Measure(new(1200, 800)); _root.Arrange(new Rect(0, 0, 1200, 800)); _root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1200, 800, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(_root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
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
