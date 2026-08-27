using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.DesktopHosting;

internal sealed class FallbackHostProbe : IDisposable
{
    private const uint WsPopup = 0x80000000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExTopMost = 0x00000008;
    private const uint WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;
    private const int SwShowNoActivate = 4;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint WmMouseActivate = 0x0021;
    private const nint MaNoActivate = 3;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint PmRemove = 0x0001;

    private static readonly nint HwndBottom = 1;

    private readonly string _className = $"ZhuomianFallbackHostProbe-{Guid.NewGuid():N}";
    private readonly NativeMethods.WindowProcedure _windowProcedure;
    private readonly nint _instance;
    private nint _backgroundBrush;
    private nint _window;
    private ushort _classAtom;

    public FallbackHostProbe()
    {
        _windowProcedure = WindowProcedure;
        _instance = NativeMethods.GetModuleHandleW(null);
    }

    public HostProbeEvidence Run()
    {
        RegisterWindowClass();

        var monitorCount = CountMonitors();
        var foregroundBefore = NativeMethods.GetForegroundWindow();
        var foregroundWasAvailable = foregroundBefore != 0;
        var workArea = GetPrimaryWorkArea();
        const int width = 480;
        const int height = 240;
        var x = workArea.Left + 40;
        var y = workArea.Top + 40;

        _window = NativeMethods.CreateWindowExW(
            WsExToolWindow | WsExNoActivate,
            _className,
            "Zhuomian Fallback Host Probe",
            WsPopup,
            x,
            y,
            width,
            height,
            0,
            0,
            _instance,
            0);

        if (_window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowExW failed.");
        }

        var bottomPlacementSucceeded = NativeMethods.SetWindowPos(
            _window,
            HwndBottom,
            x,
            y,
            width,
            height,
            SwpNoActivate | SwpShowWindow);

        NativeMethods.ShowWindow(_window, SwShowNoActivate);
        NativeMethods.UpdateWindow(_window);
        PumpMessages(TimeSpan.FromMilliseconds(250));

        var foregroundAfter = NativeMethods.GetForegroundWindow();
        var extendedStyle = unchecked((ulong)NativeMethods.GetWindowLongPtrW(_window, GwlExStyle).ToInt64());
        var mouseActivationResult = NativeMethods.SendMessageW(
            _window,
            WmMouseActivate,
            foregroundBefore,
            1);
        var monitor = NativeMethods.MonitorFromWindow(_window, MonitorDefaultToNearest);
        var monitorInfo = default(NativeMethods.MonitorInfo);
        var monitorMappingSucceeded = monitor != 0 && TryGetMonitorInfo(monitor, out monitorInfo);
        var hasWindowRect = NativeMethods.GetWindowRect(_window, out var windowRect);
        var insideWorkArea = monitorMappingSucceeded && hasWindowRect &&
            windowRect.Left >= monitorInfo.Work.Left &&
            windowRect.Top >= monitorInfo.Work.Top &&
            windowRect.Right <= monitorInfo.Work.Right &&
            windowRect.Bottom <= monitorInfo.Work.Bottom;

        var noActivateStylePresent = (extendedStyle & WsExNoActivate) != 0;
        var toolWindowStylePresent = (extendedStyle & WsExToolWindow) != 0;
        var topMostStyleAbsent = (extendedStyle & WsExTopMost) == 0;
        var foregroundPreserved = foregroundWasAvailable && foregroundAfter == foregroundBefore;
        var hostNeverBecameForeground = foregroundAfter != _window;
        var mouseActivationReturnsNoActivate = mouseActivationResult == MaNoActivate;

        var destroyedWindow = _window;
        var destroyed = NativeMethods.DestroyWindow(destroyedWindow);
        _window = 0;
        PumpMessages(TimeSpan.FromMilliseconds(50));
        var windowWasDestroyed = destroyed && !NativeMethods.IsWindow(destroyedWindow);

        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["foreground-available"] = foregroundWasAvailable,
            ["foreground-preserved-after-show"] = foregroundPreserved,
            ["host-never-foreground"] = hostNeverBecameForeground,
            ["no-activate-style"] = noActivateStylePresent,
            ["tool-window-style"] = toolWindowStylePresent,
            ["topmost-style-absent"] = topMostStyleAbsent,
            ["mouse-activation-no-activate"] = mouseActivationReturnsNoActivate,
            ["bottom-placement"] = bottomPlacementSucceeded,
            ["monitor-count"] = monitorCount > 0,
            ["monitor-mapping"] = monitorMappingSucceeded,
            ["inside-work-area"] = insideWorkArea,
            ["window-destroyed"] = windowWasDestroyed,
        };

        return new HostProbeEvidence(
            SchemaVersion: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.VersionString,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            MonitorCount: monitorCount,
            ForegroundWasAvailable: foregroundWasAvailable,
            ForegroundPreservedAfterShow: foregroundPreserved,
            HostNeverBecameForeground: hostNeverBecameForeground,
            NoActivateStylePresent: noActivateStylePresent,
            ToolWindowStylePresent: toolWindowStylePresent,
            TopMostStyleAbsent: topMostStyleAbsent,
            MouseActivationReturnsNoActivate: mouseActivationReturnsNoActivate,
            BottomPlacementSucceeded: bottomPlacementSucceeded,
            MonitorMappingSucceeded: monitorMappingSucceeded,
            WindowWasInsideMonitorWorkArea: insideWorkArea,
            WindowWasDestroyed: windowWasDestroyed,
            HostMode: "FallbackDesktopHost.Win32TopLevelNoActivate",
            Limitations:
            [
                "This public Win32 fallback does not guarantee placement between wallpaper and desktop icons.",
                "This probe validates one host window; per-monitor lifetime orchestration is a later spike.",
                "Keyboard interaction mode is intentionally not implemented in this probe.",
            ],
            FailedChecks: checks.Where(pair => !pair.Value).Select(pair => pair.Key).ToArray());
    }

    public void Dispose()
    {
        if (_window != 0)
        {
            NativeMethods.DestroyWindow(_window);
            _window = 0;
        }

        if (_classAtom != 0)
        {
            NativeMethods.UnregisterClassW(_className, _instance);
            _classAtom = 0;
        }

        if (_backgroundBrush != 0)
        {
            NativeMethods.DeleteObject(_backgroundBrush);
            _backgroundBrush = 0;
        }
    }

    private void RegisterWindowClass()
    {
        _backgroundBrush = NativeMethods.CreateSolidBrush(0x005A321E);
        if (_backgroundBrush == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateSolidBrush failed.");
        }

        var windowClass = new NativeMethods.WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.WindowClassEx>(),
            WindowProcedure = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
            Instance = _instance,
            BackgroundBrush = _backgroundBrush,
            ClassName = _className,
        };

        _classAtom = NativeMethods.RegisterClassExW(ref windowClass);
        if (_classAtom == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterClassExW failed.");
        }
    }

    private static int CountMonitors()
    {
        var count = 0;
        NativeMethods.MonitorEnumeration enumeration = (_, _, _, _) =>
        {
            count++;
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, enumeration, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        }

        GC.KeepAlive(enumeration);
        return count;
    }

    private static NativeMethods.Rect GetPrimaryWorkArea()
    {
        var monitor = NativeMethods.MonitorFromPoint(default, MonitorDefaultToNearest);
        if (!TryGetMonitorInfo(monitor, out var monitorInfo))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMonitorInfoW failed.");
        }

        return monitorInfo.Work;
    }

    private static bool TryGetMonitorInfo(nint monitor, out NativeMethods.MonitorInfo monitorInfo)
    {
        monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };

        return NativeMethods.GetMonitorInfoW(monitor, ref monitorInfo);
    }

    private static void PumpMessages(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            while (NativeMethods.PeekMessageW(out var message, 0, 0, 0, PmRemove))
            {
                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessageW(ref message);
            }

            Thread.Sleep(5);
        }
    }

    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == WmMouseActivate)
        {
            return MaNoActivate;
        }

        return NativeMethods.DefWindowProcW(window, message, wParam, lParam);
    }
}
