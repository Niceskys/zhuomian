using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.FallbackVisualUsability;

internal sealed class VisualUsabilityProbe : IDisposable
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;
    private const long WsExTopMost = 0x00000008;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTop = 0;

    private FallbackCanvasForm? _host;
    private OrdinaryWindowForm? _ordinaryWindow;
    private ShellDesktopController? _desktopController;
    private NativeMethods.NativePoint _originalPointer;

    internal VisualUsabilityEvidence Run(string screenshotPath)
    {
        if (!NativeMethods.GetCursorPos(out _originalPointer))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetCursorPos failed.");
        }

        _desktopController = new ShellDesktopController();
        _desktopController.MinimizeAll();
        Pump(TimeSpan.FromMilliseconds(700));

        var screen = Screen.PrimaryScreen ?? throw new InvalidOperationException("No primary screen exists.");
        var workArea = screen.WorkingArea;
        _host = new FallbackCanvasForm(workArea);
        _host.Show();
        NativeMethods.SetWindowPos(
            _host.Handle,
            HwndTop,
            workArea.Left,
            workArea.Top,
            workArea.Width,
            workArea.Height,
            SwpNoActivate | SwpShowWindow);
        _host.Refresh();
        Pump(TimeSpan.FromMilliseconds(400));

        var hostVisible = SampleCenter(_host.ExposedSample) == FallbackCanvasForm.CardColor;
        var transparentTarget = NativeMethods.WindowFromPoint(ToNative(_host.TransparentPoint));
        var transparentAreaPassedThrough = transparentTarget != _host.Handle;

        var desktopForeground = NativeMethods.GetForegroundWindow();
        InputInjector.Click(_host.PreviewClickPoint);
        Pump(TimeSpan.FromMilliseconds(200));
        var previewSingleClickExecuted = _host.PreviewExecutionCount == 1;
        var previewPreservedForeground = NativeMethods.GetForegroundWindow() == desktopForeground;

        var covered = _host.CoveredSample;
        _ordinaryWindow = new OrdinaryWindowForm(new Rectangle(
            covered.Left - 80,
            covered.Top - 80,
            covered.Width + 160,
            covered.Height + 160));
        _ordinaryWindow.Show();
        NativeMethods.SetWindowPos(
            _ordinaryWindow.Handle,
            HwndTop,
            _ordinaryWindow.Left,
            _ordinaryWindow.Top,
            _ordinaryWindow.Width,
            _ordinaryWindow.Height,
            SwpNoActivate | SwpShowWindow);
        _ordinaryWindow.Refresh();
        Pump(TimeSpan.FromMilliseconds(250));
        var ordinaryWindowCoveredHost =
            SampleCenter(covered) == OrdinaryWindowForm.SurfaceColor;

        SavePrivacySafeProof(screenshotPath, _host.ExposedSample, covered);

        var style = NativeMethods.GetWindowLongPtrW(_host.Handle, GwlStyle).ToInt64();
        var extendedStyle = NativeMethods.GetWindowLongPtrW(_host.Handle, GwlExStyle).ToInt64();
        var hostWasBorderless = (style & (WsCaption | WsThickFrame)) == 0;
        var hostWasNotTopMost = (extendedStyle & WsExTopMost) == 0;
        var hostWasNoActivate = (extendedStyle & WsExNoActivate) != 0;
        var executionCount = _host.PreviewExecutionCount;

        var hostHandle = _host.Handle;
        _ordinaryWindow.Close();
        _ordinaryWindow.Dispose();
        _ordinaryWindow = null;
        _host.Close();
        _host.Dispose();
        _host = null;
        Pump(TimeSpan.FromMilliseconds(100));
        var hostWasDestroyed = !NativeMethods.IsWindow(hostHandle);

        RestoreDesktopAndPointer();

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["host-visible-on-exposed-desktop"] = hostVisible,
            ["ordinary-window-covers-host"] = ordinaryWindowCoveredHost,
            ["preview-item-single-click"] = previewSingleClickExecuted,
            ["preview-click-preserves-external-foreground"] = previewPreservedForeground,
            ["transparent-area-passes-through"] = transparentAreaPassedThrough,
            ["host-borderless"] = hostWasBorderless,
            ["host-not-topmost"] = hostWasNotTopMost,
            ["host-noactivate"] = hostWasNoActivate,
            ["host-destroyed"] = hostWasDestroyed,
        };
        var failedChecks = checks.Where(check => !check.Value).Select(check => check.Key).ToArray();

        return new VisualUsabilityEvidence(
            SchemaVersion: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.VersionString,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            MonitorCount: Screen.AllScreens.Length,
            Strategy: "PublicWin32.ManagedZOrder.TransparentNoActivateCanvas",
            Passed: failedChecks.Length == 0,
            HostVisibleOnExposedDesktop: hostVisible,
            OrdinaryWindowCoveredHost: ordinaryWindowCoveredHost,
            PreviewItemSingleClickExecuted: previewSingleClickExecuted,
            PreviewClickPreservedExternalForeground: previewPreservedForeground,
            TransparentAreaPassedThrough: transparentAreaPassedThrough,
            HostWasBorderless: hostWasBorderless,
            HostWasNotTopMost: hostWasNotTopMost,
            HostWasNoActivate: hostWasNoActivate,
            HostWasDestroyed: hostWasDestroyed,
            PreviewExecutionCount: executionCount,
            ScreenshotArtifact: Path.GetFileName(screenshotPath),
            Checks: checks,
            FailedChecks: failedChecks,
            Limitations:
            [
                "The probe uses managed ordinary-window Z-order while the desktop is exposed; it does not provide a documented wallpaper/icon Shell band.",
                "Opaque Space pixels can cover desktop icons when their rectangles overlap, so placement conflict handling remains a product requirement.",
                "One physical monitor was available; mixed-DPI and hot-plug coverage remains open.",
                "The privacy-safe PNG contains only controlled surface crops, not the user's wallpaper or desktop icons.",
            ]);
    }

    public void Dispose()
    {
        _ordinaryWindow?.Dispose();
        _ordinaryWindow = null;
        _host?.Dispose();
        _host = null;
        RestoreDesktopAndPointer();
    }

    private void RestoreDesktopAndPointer()
    {
        if (_desktopController is not null)
        {
            _desktopController.RestoreAll();
            _desktopController.Dispose();
            _desktopController = null;
            Pump(TimeSpan.FromMilliseconds(500));
        }

        NativeMethods.SetCursorPos(_originalPointer.X, _originalPointer.Y);
    }

    private static NativeMethods.NativePoint ToNative(Point point) => new() { X = point.X, Y = point.Y };

    private static Color SampleCenter(Rectangle rectangle)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            rectangle.Left + rectangle.Width / 2,
            rectangle.Top + rectangle.Height / 2,
            0,
            0,
            new Size(1, 1));
        return bitmap.GetPixel(0, 0);
    }

    private static void SavePrivacySafeProof(string path, Rectangle exposed, Rectangle covered)
    {
        const int gap = 20;
        using var proof = new Bitmap(exposed.Width + covered.Width + gap, Math.Max(exposed.Height, covered.Height));
        using var graphics = Graphics.FromImage(proof);
        graphics.CopyFromScreen(exposed.Location, Point.Empty, exposed.Size);
        graphics.CopyFromScreen(covered.Location, new Point(exposed.Width + gap, 0), covered.Size);
        proof.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void Pump(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
    }
}
