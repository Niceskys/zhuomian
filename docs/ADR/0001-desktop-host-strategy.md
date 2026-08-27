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

### Enhanced WorkerW host

Potentially achieves the intended desktop band, but depends on undocumented Shell topology. Not yet tested and may only be accepted as an optional adapter.

### Single full-virtual-screen host

Simplifies composition but creates mixed-DPI, hit-testing and failure-domain risks. The current architecture prefers per-monitor ownership; this option remains unselected.

## Decision

No production host is accepted yet.

Continue Phase 0B with two adapters behind `IDesktopHost`:

1. Treat the public Win32 host as the mandatory fallback candidate.
2. Evaluate WorkerW only as an enhanced candidate.
3. Do not let product or interaction code observe Shell HWND details.

## Consequences

- Zhuomian can retain a supported fallback even if enhanced hosting fails.
- The fallback may not visually satisfy “between wallpaper and icons”; product presentation must be honest about this limitation.
- Focus/input and Explorer lifecycle require separate evidence before this ADR can become Accepted.

## Validation and rollback

Evidence and commands are in [the Desktop Hosting Spike](../../spikes/DesktopHosting/README.md), including the [20-run summary](../../spikes/DesktopHosting/evidence/windows-11-26100-fallback-host-summary.json). Acceptance requires:

- repeated NoActivate evidence;
- per-monitor/mixed-DPI evidence;
- foreground and explicit keyboard-mode evidence;
- Explorer restart recovery;
- enhanced host comparison and failure fallback;
- no TopMost dependency.

The Spike is disposable and can be removed without affecting production code.

## References

- [Architecture specification](../ARCHITECTURE.md)
- [Interaction specification](../INTERACTION_SPEC.md)
- [Microsoft extended window styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles)
- [Microsoft CreateWindowExW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-createwindowexw)
