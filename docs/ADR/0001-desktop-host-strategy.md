# ADR-0001: Desktop host strategy

- Status: Proposed
- Date: 2026-08-27
- Owners: Niceskys
- Supersedes: none
- Superseded by: none

## Context

Zhuomian needs a desktop-attached visual layer that does not interfere with ordinary applications. Microsoft exposes ordinary top-level Win32 windows and documented no-activate behavior, but does not expose a documented shell band guaranteeing arbitrary interactive content between wallpaper and desktop icons. Common WorkerW approaches rely on private Shell structure.

## Decision drivers

- Never steal foreground or keyboard focus through Hover.
- Ordinary applications must cover Zhuomian.
- Explorer restart must be recoverable.
- Per-monitor and mixed-DPI behavior must be testable.
- A Windows update must not remove the only usable host path.

## Options considered

### Public Win32 fallback host

A borderless, non-topmost `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` top-level window using `SW_SHOWNOACTIVATE`, `MA_NOACTIVATE` and an ordinary Z-order request.

Current evidence: the fallback probe passes 20/20 one-monitor show/focus/style/work-area/cleanup runs on Windows 11 build 26100. It does not guarantee the desired wallpaper/icon band.

A separate focus/input probe passes 5/5 one-monitor runs: a physical click on the visual NoActivate surface preserves external foreground, while an explicit click on a distinct focusable surface acquires foreground and text-box focus before guarded text injection. Escape closes keyboard mode, and cleanup restores the original foreground and pointer.

The per-monitor/DPI probe passes 10/10 runs on one 2560×1600 display at 150% scaling. It creates one independent Per-Monitor V2 host for every enumerated monitor, proves the host has no OS frame or non-client border, matches the monitor work area, and passes deterministic 96/144-DPI topology mapping. Hardware coverage remains incomplete because a real mixed-DPI pair and hot-plug were unavailable.

The corrected fallback recovery probe passes 20/20 forced Explorer restart cycles with recovery between about 0.80 and 1.25 seconds. A prior design that blocked on `TaskbarCreated` failed 10/20 cycles despite the Shell returning. Therefore, Shell window validity plus a different owning process generation is the authoritative recovery condition; `TaskbarCreated` is advisory telemetry only.

### Enhanced WorkerW host

Potentially achieves the intended desktop band, but depends on undocumented Shell topology. On Windows 11 build 26100, 15 WorkerW windows were observed but none was visible or work-area-sized. The private `0x052C` request was delivered without producing a viable candidate. The probe rejected Enhanced and selected the public fallback 20/20 times without attempting cross-process `SetParent`.

Cross-process attachment remains unaccepted. Microsoft documents that `SetParent` can forcibly reset the child DPI-awareness context when processes differ, so an observed class name is not enough to justify attachment.

### Single full-virtual-screen host

Simplifies composition but creates mixed-DPI, hit-testing and failure-domain risks. The current architecture prefers per-monitor ownership; this option remains unselected.

## Decision

No production host is accepted yet.

Continue Phase 0B with two adapters behind `IDesktopHost`:

1. Treat the public Win32 host as the mandatory fallback candidate.
2. Keep WorkerW disabled unless capability discovery, attachment and DPI-preservation checks all pass; current evidence selects fallback.
3. Do not let product or interaction code observe Shell HWND details.

## Consequences

- Zhuomian can retain a supported fallback even if enhanced hosting fails.
- The fallback may not visually satisfy “between wallpaper and icons”; product presentation must be honest about this limitation.
- Basic focus/input separation now has disposable Win32 evidence. WinUI/IME/accessibility integration still requires evidence before this ADR can become Accepted.
- Abrupt Explorer recovery for the public fallback path has repeated evidence. Enhanced WorkerW invalidation and the remaining platform lifecycle matrix still require evidence before this ADR can become Accepted.
- Current-build WorkerW discovery and private activation produced no viable host; automatic fallback passed 20/20 runs. WorkerW remains optional and non-blocking.

## Validation and rollback

Evidence and commands are in [the Desktop Hosting Spike](../../spikes/DesktopHosting/README.md), [the Focus and Input Spike](../../spikes/FocusAndInput/README.md), [the Multi-monitor/DPI Spike](../../spikes/MultiMonitorDpi/README.md), [the Explorer Recovery Spike](../../spikes/ExplorerRecovery/README.md), and [the Enhanced Hosting Spike](../../spikes/EnhancedHosting/README.md). Acceptance requires:

- repeated NoActivate evidence;
- real dual-monitor mixed-DPI and hot-plug evidence beyond the validated one-monitor host and deterministic mapping;
- WinUI keyboard-mode, IME and accessibility evidence beyond the validated Win32 transition;
- enhanced-host Explorer recovery and remaining lifecycle-matrix evidence beyond the validated abrupt fallback recovery;
- fallback visual-usability evidence and, if WorkerW is ever enabled, valid cross-build attachment/DPI evidence;
- no TopMost dependency.

The Spike is disposable and can be removed without affecting production code.

## References

- [Architecture specification](../ARCHITECTURE.md)
- [Interaction specification](../INTERACTION_SPEC.md)
- [Microsoft extended window styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles)
- [Microsoft CreateWindowExW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-createwindowexw)
