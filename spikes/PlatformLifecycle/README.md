# Full-screen and secure-desktop lifecycle Spike

## Question

Can Zhuomian detect a real external borderless full-screen process, suppress Hover, release runtime resources, and use public input-desktop and session-notification signals for fail-safe lock/UAC handling?

## Automated scope

The probe starts a separate borderless process that exactly covers the primary monitor, gives it foreground through a physical click, and validates the real HWND rather than synthetic geometry. It then verifies:

- external-process, visible, non-cloaked, monitor-covering geometry;
- borderless full-screen classification as `Suspended`;
- zero Hover expansions while suspended;
- immediate release of pointer capture, keyboard mode, animation and media ownership;
- current input desktop access and its `Default` name;
- successful `WTSRegisterSessionNotification` and unregister lifecycle;
- Unlock alone does not resume;
- Desktop Ready still does not resume until the accessible input desktop is `Default`;
- safe resume begins `ExternalForeground / Disarmed / NoKeyboardCapture / Idle`;
- child process, original foreground and pointer are restored.

## Current result

On Windows 11 build 26100, x64, with one physical monitor, the automated probe passed **5/5** runs. All five real full-screen transitions became foreground, classified as `Suspended`, and produced **0/500** Hover expansions.

See [the structured evidence](evidence/windows-11-26100-platform-lifecycle-summary.json).

## Run

The screen briefly becomes a controlled dark full-screen surface and the pointer moves, then the previous foreground and pointer are restored.

```powershell
pwsh ./spikes/PlatformLifecycle/run-platform-lifecycle-probe.ps1 -Count 5
```

## Deliberate safety boundary

`CoverageComplete` remains false. This unattended probe does not call `LockWorkStation`, trigger a UAC consent dialog, enter sleep or disconnect the session. Those actions can strand automation, require credentials or leave the repository task unable to report cleanup.

A user-attended compatibility run must still observe real `WTS_SESSION_LOCK`, `WTS_SESSION_UNLOCK`, `WTS_SESSION_DESKTOP_READY` and Winlogon/UAC input-desktop transitions. Until then, secure-desktop handling remains fail-safe policy evidence rather than a complete real transition pass.

## Official API basis

- [OpenInputDesktop](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openinputdesktop)
- [Desktops](https://learn.microsoft.com/windows/win32/winstation/desktops)
- [WM_WTSSESSION_CHANGE](https://learn.microsoft.com/windows/win32/termserv/wm-wtssession-change)
