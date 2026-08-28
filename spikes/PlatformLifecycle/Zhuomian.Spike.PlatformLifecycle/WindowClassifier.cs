using System.Runtime.InteropServices;

namespace Zhuomian.Spike.PlatformLifecycle;

internal sealed record WindowClassification(
    bool Exists,
    bool Visible,
    bool Cloaked,
    bool Minimized,
    bool HasCaption,
    bool HasThickFrame,
    bool CoversMonitor,
    bool ExternalProcess,
    bool Fullscreen,
    DesktopAvailability Availability);

internal static class WindowClassifier
{
    private const uint DwmwaCloaked = 14;
    private const uint MonitorDefaultToNearest = 2;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;
    private const long WsThickFrame = 0x00040000;

    internal static WindowClassification Classify(nint window)
    {
        var exists = window != 0 && NativeMethods.IsWindow(window);
        var visible = exists && NativeMethods.IsWindowVisible(window);
        var minimized = exists && NativeMethods.IsIconic(window);
        var cloaked = false;
        if (exists && NativeMethods.DwmGetWindowAttribute(
            window,
            DwmwaCloaked,
            out var cloakValue,
            sizeof(uint)) == 0)
        {
            cloaked = cloakValue != 0;
        }

        var processId = 0U;
        var processAvailable = exists &&
            NativeMethods.GetWindowThreadProcessId(window, out processId) != 0;
        var externalProcess = processAvailable && processId != Environment.ProcessId;
        var style = exists ? NativeMethods.GetWindowLongPtrW(window, GwlStyle).ToInt64() : 0;
        var hasCaption = (style & WsCaption) != 0;
        var hasThickFrame = (style & WsThickFrame) != 0;
        var coversMonitor = false;
        if (exists && NativeMethods.GetWindowRect(window, out var windowRect))
        {
            var monitor = NativeMethods.MonitorFromWindow(window, MonitorDefaultToNearest);
            var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (monitor != 0 && NativeMethods.GetMonitorInfoW(monitor, ref info))
            {
                coversMonitor =
                    windowRect.Left <= info.Monitor.Left + 2 &&
                    windowRect.Top <= info.Monitor.Top + 2 &&
                    windowRect.Right >= info.Monitor.Right - 2 &&
                    windowRect.Bottom >= info.Monitor.Bottom - 2;
            }
        }

        var fullscreen = exists && visible && !cloaked && !minimized &&
            externalProcess && coversMonitor && !hasCaption && !hasThickFrame;
        return new(
            exists,
            visible,
            cloaked,
            minimized,
            hasCaption,
            hasThickFrame,
            coversMonitor,
            externalProcess,
            fullscreen,
            fullscreen ? DesktopAvailability.Suspended : DesktopAvailability.ExternalForeground);
    }
}
