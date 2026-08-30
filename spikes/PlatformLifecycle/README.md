# Full-screen and secure-desktop lifecycle Spike

## Question

Can Zhuomian detect a real external borderless full-screen process and combine public input-desktop/session signals with a fail-safe lifecycle policy for Hover, input and runtime-resource ownership?

## Automated scope

The probe starts a separate borderless process that exactly covers the primary monitor, gives it foreground through a physical click, and validates the real HWND rather than synthetic geometry. It then verifies real platform observations for:

- external-process, visible, non-cloaked, monitor-covering geometry;
- borderless full-screen classification as `Suspended`;
- current input desktop access and its `Default` name;
- successful `WTSRegisterSessionNotification` and unregister lifecycle;
- child process cleanup plus restoration of the original foreground and pointer.

The same run also verifies deterministic **policy-model** behavior after those observations:

- a `Suspended` classification blocks the modeled Hover gate;
- `Suspend` clears modeled pointer capture, keyboard mode, animation and media ownership;
- Unlock alone does not resume;
- Desktop Ready still does not resume until the accessible input desktop is `Default`;
- safe resume begins `ExternalForeground / Disarmed / NoKeyboardCapture / Idle`.

The Hover counter and runtime-resource flags in this Spike are not production integration probes: they do not inject Hover into a real Zhuomian interaction surface and do not own/release real media, animation, keyboard or pointer resources.

## Current result

On Windows 11 build 26100, x64, with one physical monitor, the automated probe passed **5/5** runs. All five real full-screen transitions became foreground and classified as `Suspended`. The same runs passed the deterministic lifecycle-policy assertions used to model Hover suppression and release/stopped state.

See [the structured evidence](evidence/windows-11-26100-platform-lifecycle-summary.json). Its `SuspendedHoverAttempts`, `SuspendedHoverExpansions` and `RuntimeResourcesReleased` fields are **policy-model evidence**, not proof of integrated Hover dispatch or live resource release.

## Run

The screen briefly becomes a controlled dark full-screen surface and the pointer moves, then the previous foreground and pointer are restored.

```powershell
pwsh ./spikes/PlatformLifecycle/run-platform-lifecycle-probe.ps1 -Count 5
```

## Deliberate safety boundary

`CoverageComplete` remains false. This unattended probe does not call `LockWorkStation`, trigger a UAC consent dialog, enter sleep or disconnect the session. Those actions can strand automation, require credentials or leave the repository task unable to report cleanup.

A user-attended compatibility run must still observe real `WTS_SESSION_LOCK`, `WTS_SESSION_UNLOCK`, `WTS_SESSION_DESKTOP_READY` and Winlogon/UAC input-desktop transitions. Production integration must additionally prove that real Hover dispatch, pointer/keyboard ownership, animation callbacks and media ownership obey the lifecycle policy. Until then, secure-desktop handling and runtime release remain fail-safe policy evidence rather than complete integration evidence.

## Official API basis

- [OpenInputDesktop](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openinputdesktop)
- [Desktops](https://learn.microsoft.com/windows/win32/winstation/desktops)
- [WM_WTSSESSION_CHANGE](https://learn.microsoft.com/windows/win32/termserv/wm-wtssession-change)
