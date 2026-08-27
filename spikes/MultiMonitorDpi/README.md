# Multi-monitor and DPI Spike

## Question

Can Zhuomian create one independent, truly borderless, Per-Monitor V2 fallback host for every attached display and map logical Space placement safely across different DPI values and changed monitor topology?

This Spike deliberately distinguishes a passing probe from complete hardware coverage. Synthetic mixed-DPI results never count as real multi-monitor evidence.

## Borderless contract

Each disposable host is a `WS_POPUP` window with no caption, thick frame, system menu, minimize box or maximize box. It also uses `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, is not TopMost, and is placed at the bottom of ordinary window Z-order.

The probe requires the window rectangle and client rectangle to match exactly. This proves there is no operating-system title bar or page border around the host.

## Pass criteria

- the process and every host use Per-Monitor V2 awareness;
- exactly one independently owned host is created for every enumerated monitor;
- every host maps back to its intended monitor and exactly matches that monitor's work area;
- every host is borderless and its client area equals its complete window area;
- stable monitor identity hashes are unique without publishing raw device identifiers;
- all hosts are destroyed;
- deterministic 96/144-DPI mapping, negative-origin, missing-monitor migration and visible clamping checks pass.

`Passed` means all checks possible on the attached hardware succeeded. `CoverageComplete` additionally requires at least two physical monitors with at least two different DPI values.

## Current result

On Windows 11 build 26100, x64, the repeatability harness passed **10/10 runs** on one 2560×1600 display at 150% scaling. The real host was borderless, matched its 2560×1528 work area and ran Per-Monitor V2. All four synthetic mixed-DPI/topology checks passed.

`CoverageComplete` remains **false** because no second physical monitor or hot-plug event was available. See [the committed JSON evidence](evidence/windows-11-26100-monitor-dpi-summary.json).

## Run

```powershell
pwsh ./spikes/MultiMonitorDpi/run-monitor-dpi-probe.ps1 -Count 10
```

For complete evidence, attach two monitors, configure different scaling values, rerun the harness, then perform the separately specified hot-plug cycle. Do not edit the JSON to mark coverage complete.

## Official API basis

- [Setting the default DPI awareness for a process](https://learn.microsoft.com/windows/win32/hidpi/setting-the-default-dpi-awareness-for-a-process)
- [High DPI reference](https://learn.microsoft.com/windows/win32/hidpi/high-dpi-reference)
- [WM_DPICHANGED](https://learn.microsoft.com/windows/win32/hidpi/wm-dpichanged)
- [EnumDisplayMonitors](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors)

## Known limitations

- Current physical evidence covers one monitor at 150%, not a real mixed-DPI pair.
- Hot-plug and `WM_DISPLAYCHANGE` recovery remain untested.
- Synthetic tests validate the coordinate algorithm, not Windows compositor behavior.
- The windows are disposable Win32 hosts, not production WinUI composition surfaces.
