# Explorer recovery Spike

## Question

Can Zhuomian detect an Explorer/Shell process-generation change, enter a safe host-lost state, destroy the stale fallback host, discover the replacement Shell, and rebuild a borderless NoActivate host within the provisional five-second recovery budget?

## Safety protocol

The harness does not kill an arbitrary `explorer` process. Before every cycle it:

1. asks the official Shell window for its owning process ID;
2. resolves that exact process;
3. verifies its process name is `explorer` and its absolute path is `%WINDIR%\explorer.exe`;
4. terminates only that PID;
5. waits for a different Shell process generation;
6. starts the verified Windows Explorer binary only if Windows did not restore the Shell automatically;
7. confirms a Shell exists before proceeding or exiting.

The desktop and taskbar briefly disappear during every cycle. Open File Explorer windows may close.

## Recovery contract

On host loss the probe immediately models:

- `DesktopAvailability = Suspended`;
- `IntentGate = Disarmed`;
- `KeyboardMode = NoKeyboardCapture`;
- `SpaceState = Idle`;
- input capture released;
- animation callbacks stopped;
- media resources released.

After a different Shell PID is confirmed, the probe rebuilds the host and returns to `DesktopAvailable / Disarmed / NoKeyboardCapture / Idle`. The rebuilt host must retain `WS_EX_NOACTIVATE`, never become foreground, and be destroyed during final cleanup.

## Important finding: TaskbarCreated is advisory

The first 20-cycle design incorrectly waited for the registered `TaskbarCreated` broadcast before rebuilding. Only 10/20 cycles observed it; the other ten recovered the Shell but exceeded five seconds while waiting. See [the failed baseline](evidence/windows-11-26100-explorer-recovery-summary.json).

The corrected design treats a new nonzero Shell window owned by a different process ID as the authoritative generation change. `TaskbarCreated` remains useful telemetry but cannot block recovery. Microsoft also documents that Windows 10 may broadcast `TaskbarCreated` when primary-display DPI changes, so it is not sufficient by itself to prove an Explorer restart.

## Current result

On Windows 11 build 26100, x64, the corrected harness passed **20/20 forced Shell restart cycles**:

- minimum recovery: about 0.80 seconds;
- maximum recovery: about 1.25 seconds;
- recovery budget: 5 seconds;
- 0 failed checks;
- final Shell available after every run;
- stale host destroyed and new host rebuilt NoActivate;
- no probe windows remained.

See [the corrected 20-cycle evidence](evidence/windows-11-26100-explorer-recovery-shell-generation-summary.json).

## Run

Close any File Explorer windows whose navigation state you need to preserve, then run:

```powershell
pwsh ./spikes/ExplorerRecovery/run-explorer-recovery-probe.ps1 -Count 20
```

## Official API basis

- [GetWindowThreadProcessId](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getwindowthreadprocessid)
- [Taskbar creation notification](https://learn.microsoft.com/windows/win32/shell/taskbar#taskbar-creation-notification)
- [Extended window styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles)

## Known limitations

- This covers abrupt forced restart of the public fallback host path, not graceful Explorer exit.
- Enhanced WorkerW parent invalidation is still untested.
- Input, animation and media owners are modeled flags because production modules do not exist yet.
- Sleep/resume, lock screen, UAC secure desktop and long-running soak remain separate scenarios.
- This is disposable evidence, not production recovery code.
