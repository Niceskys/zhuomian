# ADR-0002: Foreground classification strategy

- Status: Proposed
- Date: 2026-08-28
- Owners: Niceskys
- Supersedes: none
- Superseded by: none

## Context

Zhuomian must suppress Hover expansion and keyboard capture while an external application is in use, while still allowing an exact Shell desktop surface and an explicitly activated Zhuomian keyboard surface. `GetForegroundWindow` alone returns a handle, not the product state needed by the interaction model.

## Decision drivers

- External applications must never be mistaken for the desktop.
- A File Explorer window must not be accepted merely because it shares an Explorer process with Shell components.
- Full-screen and unavailable-session paths must fail safe.
- The classifier must not depend on private WorkerW topology.
- Idle classification must not require high-frequency polling.

## Decision

Build `DesktopAvailability` from a conservative aggregate of public signals:

1. input-desktop accessibility;
2. foreground window validity;
3. exact Zhuomian process ownership;
4. exact public Shell window identity;
5. DWM cloaking, visibility and minimized/maximized state;
6. monitor-covering geometry and frame styles.

Classification order is:

- unavailable input desktop or missing foreground → `Suspended`;
- Zhuomian-owned foreground → `DesktopAvailable`;
- exact Shell desktop HWND → `DesktopAvailable`;
- visible, non-cloaked, borderless monitor-covering external window → `Suspended`;
- otherwise → `ExternalForeground`.

Missing process information is never interpreted as desktop availability. Explorer process identity without exact Shell HWND identity remains external.

Foreground change notification and debouncing remain implementation work. Production should prefer event-driven notification with bounded reconciliation and must not add an idle high-frequency polling loop.

## Evidence

The [Foreground Classification Spike](../../spikes/ForegroundClassification/README.md) passes 5/5 runs on Windows 11 build 26100, including 500 real external samples, the complete 24-case Hover truth table per run and synthetic failure-safe cases.

## Consequences

- False positives favor blocking Hover rather than interrupting another application.
- A desktop intent click may need a separate hit-test path when the exact foreground HWND is not the public Shell window.
- Real lock/UAC/full-screen applications and foreground event delivery remain required before this ADR becomes Accepted.

## Validation and rollback

Acceptance requires real full-screen transition evidence, lock/UAC lifecycle evidence, event-delivery/debounce testing, and no external/disarmed Hover expansion. The Spike is disposable and can be removed without affecting production code.

## References

- [Product specification](../PRODUCT_SPEC.md)
- [Interaction specification](../INTERACTION_SPEC.md)
- [Architecture specification](../ARCHITECTURE.md)
- [Microsoft foreground-window documentation](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- [Microsoft OpenInputDesktop documentation](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openinputdesktop)
