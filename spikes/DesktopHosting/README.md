# Desktop Hosting Spike 1: public fallback and NoActivate

## Question

Can Zhuomian create a borderless, non-topmost, public-Win32 fallback host that remains mapped to a monitor work area, does not take foreground focus when shown, and returns the documented no-activate response to `WM_MOUSEACTIVATE`?

This Spike does **not** test WorkerW or claim placement between wallpaper and desktop icons.

## Strategy

The disposable executable creates one top-level `WS_POPUP` window with:

- `WS_EX_NOACTIVATE`
- `WS_EX_TOOLWINDOW`
- no `WS_EX_TOPMOST`
- `SW_SHOWNOACTIVATE`
- `WM_MOUSEACTIVATE → MA_NOACTIVATE`
- `SetWindowPos(HWND_BOTTOM, SWP_NOACTIVATE)`

It enumerates monitors, maps the probe window to the nearest monitor work area, verifies the foreground handle is unchanged, destroys the host, and emits machine-readable JSON evidence.

## Pass criteria

- a foreground window exists before the probe;
- showing the host preserves that foreground window;
- the probe host never becomes foreground;
- NoActivate and ToolWindow styles are present;
- TopMost is absent;
- `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`;
- bottom placement succeeds;
- at least one monitor is enumerated and the host fits its work area;
- the host is destroyed during cleanup.

Any failed check makes the executable return exit code 1.

## Current result

On Windows 11 build 26100, x64, one monitor, the repeatability harness passed **20/20 runs** with no failed checks. See [the committed JSON evidence](evidence/windows-11-26100-fallback-host-summary.json).

## Run

```powershell
dotnet run --project ./spikes/DesktopHosting/Zhuomian.Spike.DesktopHosting `
  --configuration Release -- `
  --output ./spikes/DesktopHosting/evidence/local-fallback-host.json
```

Run the repeatability harness:

```powershell
pwsh ./spikes/DesktopHosting/run-fallback-probe.ps1 -Count 20
```

## Official API basis

- [Extended Window Styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles)
- [CreateWindowExW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-createwindowexw)
- [EnumDisplayMonitors](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors)
- [Multiple Monitor System Metrics](https://learn.microsoft.com/windows/win32/gdi/multiple-monitor-system-metrics)

Microsoft documents `WS_EX_NOACTIVATE` as preventing a top-level window from becoming foreground when clicked. The CreateWindowEx documentation additionally calls out handling `WM_MOUSEACTIVATE` for click-time queue activation. Monitor work areas other than the primary are obtained through monitor enumeration and `GetMonitorInfo`.

## Known limitations

- Public Win32 does not provide a documented shell band that guarantees a window remains between wallpaper and desktop icons.
- This first Spike validates one disposable host, not per-monitor lifetime orchestration.
- A physical mouse click is not injected because that would take over the user's pointer; this Spike validates the `WM_MOUSEACTIVATE` decision directly.
- Keyboard search mode is a separate Spike because it intentionally requires an explicit focus transition.
- Enhanced WorkerW hosting remains unvalidated and must never become the only implementation.
