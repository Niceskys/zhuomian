# Enhanced WorkerW versus public fallback Spike

## Question

Does the current Windows Shell expose a WorkerW surface that is safe enough to consider as an optional enhanced host, and does Zhuomian automatically select the public borderless fallback when any enhanced precondition is missing?

## Candidate contract

A WorkerW is not accepted merely because its class name matches. A candidate must be:

- visible;
- owned by the current Shell process;
- large enough to cover at least 90% of the primary monitor;
- free of `SHELLDLL_DefView`, so it does not itself own desktop icons.

Even then, Enhanced selection additionally requires successful attachment and proof that cross-process parenting preserved the child DPI-awareness context. Only all three conditions together may select Enhanced.

## Current topology result

On Windows 11 build 26100 the Shell exposed 15 WorkerW windows, but:

- visible WorkerW: 0;
- work-area-sized viable WorkerW: 0;
- desktop icons remained under visible `Progman`;
- the private `0x052C` message was delivered but created no new or viable WorkerW.

The probe therefore did **not** call cross-process `SetParent`. Microsoft documents that cross-process `SetParent` can forcibly reset the child process DPI-awareness context, so doing it against an invalid candidate would provide no useful evidence and could destabilize scaling.

## Current result

The repeatability harness passed **20/20 runs**:

- Enhanced selected: 0;
- automatic public fallback: 20/20;
- cross-process `SetParent` attempts: 0;
- fallback remained borderless, NoActivate and non-TopMost;
- fallback client area equalled the complete window area;
- fallback matched the monitor work area and retained a valid DPI;
- foreground was preserved and every probe host was destroyed;
- all five selection-policy failure cases chose fallback.

This is a successful degradation result, not proof that WorkerW hosting works. See [the committed JSON evidence](evidence/windows-11-26100-enhanced-fallback-summary.json).

## Run

The first run sends one undocumented WorkerW request; remaining runs only inspect topology and validate fallback.

```powershell
pwsh ./spikes/EnhancedHosting/run-enhanced-fallback-probe.ps1 -Count 20
```

## Official API basis

- [SetParent](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setparent)
- [GetWindowThreadProcessId](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)
- [GetAncestor](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getancestor)
- [Extended window styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles)

WorkerW class topology and message `0x052C` are not documented Microsoft contracts. They are recorded only as observed private behavior.

## Known limitations

- No valid enhanced candidate existed, so this Spike validates rejection and fallback rather than WorkerW attachment.
- Only one Windows build and one display were available.
- The fallback's technical contract is validated; its final visual usability still needs product assessment.
- This is disposable evidence, not production host code.
