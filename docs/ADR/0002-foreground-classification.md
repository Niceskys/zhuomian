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

The [Platform Lifecycle Spike](../../spikes/PlatformLifecycle/README.md) passes 5/5 automated runs against a real separate borderless process covering the primary monitor. All five transitions became foreground and classified as `Suspended`; the live default input desktop was accessible and named `Default`; WTS session notification registration and cleanup succeeded. Unlock/Desktop Ready ordering, Hover suppression after `Suspended`, and pointer/keyboard/animation/media release are deterministic lifecycle-policy assertions in this Spike, not proof that a production Hover dispatcher or live runtime-resource owners were exercised.

## Consequences

- False positives favor blocking Hover rather than interrupting another application.
- A desktop intent click may need a separate hit-test path when the exact foreground HWND is not the public Shell window.
- Real full-screen classification now has repeated external-process evidence.
- Real lock/UAC transitions, sleep/remote-session behavior, foreground event delivery/debounce and lifecycle integration with actual runtime owners remain required before this ADR becomes Accepted.

## Validation and rollback

Acceptance requires user-attended real lock/UAC lifecycle evidence, sleep/remote-session evidence, event-delivery/debounce testing, integrated no-external/disarmed-Hover behavior, and proof that real pointer/keyboard/animation/media ownership obeys suspension. The automated real full-screen classification criterion is satisfied; the Spike is disposable and can be removed without affecting production code.

## References

- [Product specification](../PRODUCT_SPEC.md)
- [Interaction specification](../INTERACTION_SPEC.md)
- [Architecture specification](../ARCHITECTURE.md)
- [Microsoft foreground-window documentation](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getforegroundwindow)
- [Microsoft OpenInputDesktop documentation](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openinputdesktop)
- [Microsoft desktop-object documentation](https://learn.microsoft.com/windows/win32/winstation/desktops)
- [Microsoft session-change documentation](https://learn.microsoft.com/windows/win32/termserv/wm-wtssession-change)
