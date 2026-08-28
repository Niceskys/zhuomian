using System.Runtime.InteropServices;

namespace Zhuomian.Spike.ForegroundClassification;

internal static class WindowSignalExtractor
{
    private const uint DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const int GwlStyle = -16;
    private const ulong WsCaption = 0x00C00000;
    private const ulong WsThickFrame = 0x00040000;
    private const uint DesktopReadObjects = 0x0001;
    private const uint DesktopSwitchDesktop = 0x0100;

    public static WindowSignals CaptureForeground() => Capture(NativeMethods.GetForegroundWindow());

    public static WindowSignals Capture(nint window)
    {
        var sessionInteractive = IsInputDesktopAccessible();
        var exists = window != 0 && NativeMethods.IsWindow(window);
        var windowProcessId = 0U;
        var processIdAvailable = exists &&
            NativeMethods.GetWindowThreadProcessId(window, out windowProcessId) != 0 &&
            windowProcessId != 0;
        var shellWindow = NativeMethods.GetShellWindow();
        var visible = exists && NativeMethods.IsWindowVisible(window);
        var minimized = exists && NativeMethods.IsIconic(window);
        var maximized = exists && NativeMethods.IsZoomed(window);
        var cloaked = false;
        if (exists && NativeMethods.DwmGetWindowAttribute(
            window,
            DwmwaCloaked,
            out var cloakValue,
            sizeof(uint)) == 0)
        {
            cloaked = cloakValue != 0;
        }

        var windowBounds = default(NativeMethods.Rect);
        var monitorBounds = default(NativeMethods.Rect);
        if (exists && NativeMethods.GetWindowRect(window, out windowBounds))
        {
            var monitor = NativeMethods.MonitorFromWindow(window, MonitorDefaultToNearest);
            var monitorInfo = new NativeMethods.MonitorInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>(),
            };
            if (monitor != 0 && NativeMethods.GetMonitorInfoW(monitor, ref monitorInfo))
            {
                monitorBounds = monitorInfo.Monitor;
            }
        }

        var style = exists
            ? unchecked((ulong)NativeMethods.GetWindowLongPtrW(window, GwlStyle).ToInt64())
            : 0;
        return new WindowSignals(
            SessionInteractive: sessionInteractive,
            WindowExists: exists,
            ProcessIdAvailable: processIdAvailable,
            WindowProcessId: processIdAvailable ? windowProcessId : 0,
            CurrentProcessId: (uint)Environment.ProcessId,
            ExactShellWindow: exists && window == shellWindow,
            Visible: visible,
            Cloaked: cloaked,
            Minimized: minimized,
            Maximized: maximized,
            HasCaption: (style & WsCaption) != 0,
            HasThickFrame: (style & WsThickFrame) != 0,
            WindowBounds: windowBounds,
            MonitorBounds: monitorBounds);
    }

    private static bool IsInputDesktopAccessible()
    {
        var desktop = NativeMethods.OpenInputDesktop(
            0,
            false,
            DesktopReadObjects | DesktopSwitchDesktop);
        if (desktop == 0)
        {
            return false;
        }

        NativeMethods.CloseDesktop(desktop);
        return true;
    }
}
