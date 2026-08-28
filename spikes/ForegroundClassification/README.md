# Foreground classification and Hover-gating Spike

## Question

Can Zhuomian classify the current Windows interaction context conservatively enough that external applications never trigger Hover expansion while still recognizing the exact Shell desktop and an explicitly activated Zhuomian surface?

## Classification strategy

The disposable classifier combines multiple signals:

- current foreground HWND;
- exact Shell HWND identity;
- owning process identity;
- input-desktop accessibility;
- window visibility, cloaking and minimized/maximized state;
- window and monitor geometry;
- caption and resize-frame styles.

It does not classify every window owned by `explorer.exe` as the desktop. Only the exact Shell window is `DesktopAvailable`; File Explorer and other Shell-owned windows fail safe as `ExternalForeground`.

## Fail-safe rules

- unavailable input desktop or no foreground window → `Suspended`;
- exact Zhuomian process window → `DesktopAvailable`;
- exact Shell desktop window → `DesktopAvailable`;
- visible borderless monitor-covering external window → `Suspended`;
- every other window, including inaccessible ownership data → `ExternalForeground`.

The Hover gate then follows the interaction specification exactly. `ExternalForeground + Disarmed`, `Suspended`, Click activation, and any higher-priority event cannot enter Intent. The gate has no keyboard-capture operation.

## Current result

On Windows 11 build 26100, x64, the repeatability harness passed **5/5 runs**:

- 500/500 real external-foreground samples blocked Hover expansion;
- 0 external/disarmed expansions;
- 24/24 deterministic gate combinations passed per run;
- live exact Shell and physically activated Zhuomian windows classified correctly;
- all nine synthetic safety and boundary checks passed;
- original foreground and pointer position were restored after every run.

See [the committed JSON evidence](evidence/windows-11-26100-foreground-classification-summary.json).

## Run

The pointer briefly moves while the probe explicitly activates its own test window.

```powershell
pwsh ./spikes/ForegroundClassification/run-foreground-classification-probe.ps1 -Count 5
```

## Official API basis

- [GetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- [Window foreground/background behavior](https://learn.microsoft.com/windows/win32/winmsg/window-features#foreground-and-background-windows)
- [DwmGetWindowAttribute](https://learn.microsoft.com/windows/win32/api/dwmapi/nf-dwmapi-dwmgetwindowattribute)
- [OpenInputDesktop](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openinputdesktop)

## Known limitations

- A real game/video full-screen transition was not activated; the signal path is deterministic evidence.
- Lock screen and UAC secure desktop were not entered during the run.
- Foreground event delivery and debouncing are not implemented; production must avoid idle high-frequency polling.
- This is disposable Win32 evidence, not production classification code.
