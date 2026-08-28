using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.EnhancedHosting;

internal sealed record ShellSurface(
    nint Window,
    string ClassName,
    bool Visible,
    NativeMethods.Rect Bounds,
    bool ContainsDesktopView,
    bool OwnedByShellProcess,
    bool CoversPrimaryMonitor);

internal sealed record ShellTopology(
    ShellSurface[] Surfaces,
    int WorkerWCount,
    int VisibleWorkerWCount,
    int ViableWorkerWCount,
    bool ProgmanContainsDesktopView)
{
    public bool HasViableEnhancedCandidate => ViableWorkerWCount > 0;
}

internal static class ShellTopologyScanner
{
    private const double MinimumCoverageRatio = 0.90;

    public static ShellTopology Scan(Rectangle primaryMonitorBounds)
    {
        var shellWindow = NativeMethods.GetShellWindow();
        var shellProcessId = 0U;
        var shellProcessAvailable = shellWindow != 0 &&
            NativeMethods.GetWindowThreadProcessId(shellWindow, out shellProcessId) != 0;
        var surfaces = new List<ShellSurface>();
        NativeMethods.WindowEnumeration enumeration = (window, _) =>
        {
            var classNameBuffer = new char[64];
            var classNameLength = NativeMethods.GetClassNameW(
                window,
                classNameBuffer,
                classNameBuffer.Length);
            if (classNameLength <= 0)
            {
                return true;
            }

            var className = new string(classNameBuffer, 0, classNameLength);
            if (className is not ("WorkerW" or "Progman"))
            {
                return true;
            }

            NativeMethods.GetWindowRect(window, out var bounds);
            var visible = NativeMethods.IsWindowVisible(window);
            var containsDesktopView = NativeMethods.FindWindowExW(
                window,
                0,
                "SHELLDLL_DefView",
                null) != 0;
            var processAvailable = NativeMethods.GetWindowThreadProcessId(
                window,
                out var processId) != 0;
            var coversPrimary =
                bounds.Width >= primaryMonitorBounds.Width * MinimumCoverageRatio &&
                bounds.Height >= primaryMonitorBounds.Height * MinimumCoverageRatio;
            surfaces.Add(new ShellSurface(
                window,
                className,
                visible,
                bounds,
                containsDesktopView,
                shellProcessAvailable && processAvailable && processId == shellProcessId,
                coversPrimary));
            return true;
        };

        if (!NativeMethods.EnumWindows(enumeration, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumWindows failed.");
        }

        GC.KeepAlive(enumeration);
        var workerWindows = surfaces.Where(surface => surface.ClassName == "WorkerW").ToArray();
        var viable = workerWindows.Count(surface =>
            surface.Visible &&
            surface.OwnedByShellProcess &&
            surface.CoversPrimaryMonitor &&
            !surface.ContainsDesktopView);
        return new ShellTopology(
            [.. surfaces],
            workerWindows.Length,
            workerWindows.Count(surface => surface.Visible),
            viable,
            surfaces.Any(surface =>
                surface.ClassName == "Progman" && surface.ContainsDesktopView));
    }

    public static bool RequestPrivateWorkerW()
    {
        const uint privateWorkerMessage = 0x052C;
        const uint timeoutMilliseconds = 1000;
        var progman = NativeMethods.FindWindowW("Progman", null);
        return progman != 0 && NativeMethods.SendMessageTimeoutW(
            progman,
            privateWorkerMessage,
            0,
            0,
            0,
            timeoutMilliseconds,
            out _) != 0;
    }
}
