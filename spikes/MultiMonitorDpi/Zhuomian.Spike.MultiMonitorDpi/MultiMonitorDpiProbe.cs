using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Zhuomian.Spike.MultiMonitorDpi;

internal sealed class MultiMonitorDpiProbe : IDisposable
{
    private const uint MonitorInfoPrimary = 0x00000001;
    private const uint MonitorDefaultToNull = 0x00000000;
    private const uint WsPopup = 0x80000000;
    private const uint WsCaption = 0x00C00000;
    private const uint WsThickFrame = 0x00040000;
    private const uint WsSystemMenu = 0x00080000;
    private const uint WsMinimizeBox = 0x00020000;
    private const uint WsMaximizeBox = 0x00010000;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsExNoActivate = 0x08000000;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint WmMouseActivate = 0x0021;
    private const nint MaNoActivate = 3;
    private const int PerMonitorAware = 2;
    private const uint EddGetDeviceInterfaceName = 0x00000001;

    internal static readonly nint PerMonitorV2 = -4;
    private static readonly nint HwndBottom = 1;

    private readonly string _className = $"ZhuomianMultiMonitorProbe-{Guid.NewGuid():N}";
    private readonly NativeMethods.WindowProcedure _windowProcedure;
    private readonly nint _instance;
    private readonly List<HostObservation> _hosts = [];
    private nint _backgroundBrush;
    private ushort _classAtom;

    public MultiMonitorDpiProbe()
    {
        _windowProcedure = WindowProcedure;
        _instance = NativeMethods.GetModuleHandleW(null);
    }

    public MultiMonitorDpiEvidence Run()
    {
        RegisterWindowClass();
        var monitors = EnumerateMonitors();
        foreach (var monitor in monitors)
        {
            _hosts.Add(CreateAndInspectHost(monitor));
        }

        foreach (var host in _hosts)
        {
            host.HostWasDestroyed =
                NativeMethods.DestroyWindow(host.Window) &&
                !NativeMethods.IsWindow(host.Window);
        }

        var monitorEvidence = _hosts.Select((host, index) => new MonitorEvidence(
            Index: index,
            IsPrimary: host.Monitor.IsPrimary,
            StableKeyHash: host.Monitor.StableKeyHash,
            MonitorWidthPx: host.Monitor.Bounds.Width,
            MonitorHeightPx: host.Monitor.Bounds.Height,
            WorkAreaWidthPx: host.Monitor.WorkArea.Width,
            WorkAreaHeightPx: host.Monitor.WorkArea.Height,
            Dpi: host.Dpi,
            ScalePercent: (int)Math.Round(host.Dpi * 100.0 / 96.0),
            PerMonitorAware: host.PerMonitorAware,
            HostMappedToExpectedMonitor: host.HostMappedToExpectedMonitor,
            HostMatchedWorkArea: host.HostMatchedWorkArea,
            HostWasBorderless: host.HostWasBorderless,
            ClientAreaMatchedWindow: host.ClientAreaMatchedWindow,
            HostWasDestroyed: host.HostWasDestroyed)).ToArray();

        var syntheticChecks = RunSyntheticChecks();
        var processPerMonitorV2 = NativeMethods.AreDpiAwarenessContextsEqual(
            NativeMethods.GetThreadDpiAwarenessContext(),
            PerMonitorV2);
        var actualHostsPassed = monitorEvidence.Length > 0 && monitorEvidence.All(monitor =>
            monitor.Dpi > 0 &&
            monitor.PerMonitorAware &&
            monitor.HostMappedToExpectedMonitor &&
            monitor.HostMatchedWorkArea &&
            monitor.HostWasBorderless &&
            monitor.ClientAreaMatchedWindow &&
            monitor.HostWasDestroyed);
        var syntheticPassed = syntheticChecks.All(check => check.Passed);
        var distinctDpiCount = monitorEvidence.Select(monitor => monitor.Dpi).Distinct().Count();
        var multipleMonitorCoverage = monitorEvidence.Length >= 2;
        var mixedDpiCoverage = multipleMonitorCoverage && distinctDpiCount >= 2;
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["process-per-monitor-v2"] = processPerMonitorV2,
            ["monitor-enumeration"] = monitorEvidence.Length > 0,
            ["stable-monitor-keys-unique"] = monitorEvidence
                .Select(monitor => monitor.StableKeyHash)
                .Distinct(StringComparer.Ordinal)
                .Count() == monitorEvidence.Length,
            ["actual-per-monitor-hosts"] = actualHostsPassed,
            ["synthetic-mixed-dpi-mapping"] = syntheticPassed,
        };
        var coverageGaps = new List<string>();
        if (!multipleMonitorCoverage)
        {
            coverageGaps.Add("No multi-monitor hardware was attached during this run.");
        }

        if (!mixedDpiCoverage)
        {
            coverageGaps.Add("No real mixed-DPI monitor pair was available during this run.");
        }

        return new MultiMonitorDpiEvidence(
            SchemaVersion: 1,
            TimestampUtc: DateTimeOffset.UtcNow,
            OsVersion: Environment.OSVersion.VersionString,
            ProcessArchitecture: RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessPerMonitorV2: processPerMonitorV2,
            MonitorCount: monitorEvidence.Length,
            DistinctDpiCount: distinctDpiCount,
            MultipleMonitorHardwareCoverage: multipleMonitorCoverage,
            MixedDpiHardwareCoverage: mixedDpiCoverage,
            ActualPerMonitorHostsPassed: actualHostsPassed,
            SyntheticMixedDpiMappingPassed: syntheticPassed,
            Monitors: monitorEvidence,
            SyntheticChecks: syntheticChecks,
            CoverageGaps: [.. coverageGaps],
            Limitations:
            [
                "Hot-plug and WM_DISPLAYCHANGE were not exercised because no monitor was attached or removed.",
                "Stable keys are derived from display device identity but only a SHA-256 prefix is written to evidence.",
                "The host is a disposable Win32 probe, not the final WinUI composition surface.",
            ],
            FailedChecks: checks.Where(check => !check.Value).Select(check => check.Key).ToArray());
    }

    public void Dispose()
    {
        foreach (var host in _hosts.Where(host => !host.HostWasDestroyed))
        {
            NativeMethods.DestroyWindow(host.Window);
            host.HostWasDestroyed = true;
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
        _backgroundBrush = NativeMethods.CreateSolidBrush(0x003B2B20);
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

    private HostObservation CreateAndInspectHost(MonitorSnapshot monitor)
    {
        var work = monitor.WorkArea;
        var window = NativeMethods.CreateWindowExW(
            WsExToolWindow | WsExNoActivate,
            _className,
            "Zhuomian Per-Monitor Borderless Probe",
            WsPopup,
            work.Left,
            work.Top,
            work.Width,
            work.Height,
            0,
            0,
            _instance,
            0);

        if (window == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowExW failed.");
        }

        var placed = NativeMethods.SetWindowPos(
            window,
            HwndBottom,
            work.Left,
            work.Top,
            work.Width,
            work.Height,
            SwpNoActivate | SwpShowWindow);
        var windowDpi = NativeMethods.GetDpiForWindow(window);
        var awareness = NativeMethods.GetAwarenessFromDpiAwarenessContext(
            NativeMethods.GetWindowDpiAwarenessContext(window));
        var mappedMonitor = NativeMethods.MonitorFromWindow(window, MonitorDefaultToNull);
        var windowRectAvailable = NativeMethods.GetWindowRect(window, out var windowRect);
        var clientRectAvailable = NativeMethods.GetClientRect(window, out var clientRect);
        var clientOrigin = default(NativeMethods.Point);
        var clientOriginAvailable = NativeMethods.ClientToScreen(window, ref clientOrigin);
        var style = unchecked((uint)NativeMethods.GetWindowLongPtrW(window, GwlStyle).ToInt64());
        var extendedStyle = unchecked((uint)NativeMethods.GetWindowLongPtrW(window, GwlExStyle).ToInt64());
        var forbiddenFrameStyles = WsCaption | WsThickFrame | WsSystemMenu | WsMinimizeBox | WsMaximizeBox;

        return new HostObservation
        {
            Window = window,
            Monitor = monitor,
            Dpi = windowDpi,
            PerMonitorAware = awareness == PerMonitorAware,
            HostMappedToExpectedMonitor = mappedMonitor == monitor.Handle,
            HostMatchedWorkArea = placed && windowRectAvailable && RectEquals(windowRect, work),
            HostWasBorderless =
                (style & WsPopup) != 0 &&
                (style & forbiddenFrameStyles) == 0 &&
                (extendedStyle & WsExNoActivate) != 0 &&
                (extendedStyle & WsExToolWindow) != 0,
            ClientAreaMatchedWindow =
                windowRectAvailable &&
                clientRectAvailable &&
                clientOriginAvailable &&
                clientOrigin.X == windowRect.Left &&
                clientOrigin.Y == windowRect.Top &&
                clientRect.Width == windowRect.Width &&
                clientRect.Height == windowRect.Height,
        };
    }

    private static MonitorSnapshot[] EnumerateMonitors()
    {
        var monitors = new List<MonitorSnapshot>();
        NativeMethods.MonitorEnumeration enumeration = (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
                DeviceName = string.Empty,
            };
            if (!NativeMethods.GetMonitorInfoW(monitor, ref info))
            {
                return false;
            }

            var identity = GetMonitorIdentity(info.DeviceName);
            monitors.Add(new MonitorSnapshot(
                monitor,
                info.Monitor,
                info.Work,
                (info.Flags & MonitorInfoPrimary) != 0,
                HashIdentity(identity)));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(0, 0, enumeration, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        }

        GC.KeepAlive(enumeration);
        return [.. monitors];
    }

    private static string GetMonitorIdentity(string displayName)
    {
        var device = new NativeMethods.DisplayDevice
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.DisplayDevice>(),
            DeviceName = string.Empty,
            DeviceString = string.Empty,
            DeviceId = string.Empty,
            DeviceKey = string.Empty,
        };

        return NativeMethods.EnumDisplayDevicesW(displayName, 0, ref device, EddGetDeviceInterfaceName) &&
            !string.IsNullOrWhiteSpace(device.DeviceId)
            ? device.DeviceId
            : displayName;
    }

    private static string HashIdentity(string identity)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash)[..16];
    }

    private static SyntheticCheckEvidence[] RunSyntheticChecks()
    {
        var primary = new MonitorGeometry(
            "primary-96",
            NewRect(0, 0, 1920, 1040),
            96,
            true);
        var secondary = new MonitorGeometry(
            "secondary-144",
            NewRect(-2560, -120, 2560, 1400),
            144,
            false);
        MonitorGeometry[] monitors = [primary, secondary];
        var logical = new LogicalPlacement("primary-96", 0.25, 0.30, 400, 300);
        var primaryMapped = PlacementMapper.Map(logical, monitors);
        var secondaryMapped = PlacementMapper.Map(logical with { MonitorKey = "secondary-144" }, monitors);
        var missingMapped = PlacementMapper.Map(logical with { MonitorKey = "missing" }, monitors);
        var oversizedMapped = PlacementMapper.Map(
            new LogicalPlacement("secondary-144", 2, -1, 4000, 4000),
            monitors);

        return
        [
            new("logical-size-scales-from-96-to-144-dpi",
                primaryMapped.PhysicalBounds.Width == 400 &&
                primaryMapped.PhysicalBounds.Height == 300 &&
                secondaryMapped.PhysicalBounds.Width == 600 &&
                secondaryMapped.PhysicalBounds.Height == 450),
            new("negative-origin-monitor-remains-visible",
                IsInside(secondaryMapped.PhysicalBounds, secondary.WorkArea) &&
                secondaryMapped.PhysicalBounds.Left < 0),
            new("missing-monitor-migrates-to-primary",
                missingMapped.MigratedToPrimary &&
                missingMapped.MonitorKey == primary.Key &&
                IsInside(missingMapped.PhysicalBounds, primary.WorkArea)),
            new("oversized-placement-is-clamped",
                IsInside(oversizedMapped.PhysicalBounds, secondary.WorkArea) &&
                RectEquals(oversizedMapped.PhysicalBounds, secondary.WorkArea)),
        ];
    }

    private static NativeMethods.Rect NewRect(int left, int top, int width, int height) =>
        new()
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };

    private static bool IsInside(NativeMethods.Rect inner, NativeMethods.Rect outer) =>
        inner.Left >= outer.Left &&
        inner.Top >= outer.Top &&
        inner.Right <= outer.Right &&
        inner.Bottom <= outer.Bottom;

    private static bool RectEquals(NativeMethods.Rect first, NativeMethods.Rect second) =>
        first.Left == second.Left &&
        first.Top == second.Top &&
        first.Right == second.Right &&
        first.Bottom == second.Bottom;

    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam) =>
        message == WmMouseActivate
            ? MaNoActivate
            : NativeMethods.DefWindowProcW(window, message, wParam, lParam);

    private sealed record MonitorSnapshot(
        nint Handle,
        NativeMethods.Rect Bounds,
        NativeMethods.Rect WorkArea,
        bool IsPrimary,
        string StableKeyHash);

    private sealed class HostObservation
    {
        public required nint Window { get; init; }

        public required MonitorSnapshot Monitor { get; init; }

        public uint Dpi { get; init; }

        public bool PerMonitorAware { get; init; }

        public bool HostMappedToExpectedMonitor { get; init; }

        public bool HostMatchedWorkArea { get; init; }

        public bool HostWasBorderless { get; init; }

        public bool ClientAreaMatchedWindow { get; init; }

        public bool HostWasDestroyed { get; set; }
    }
}
