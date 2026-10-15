# ADR-0006: E2-I2 native continuity engineering candidate

- Status: Proposed
- Date: 2026-10-15
- Owners: Niceskys (future Windows acceptance), AI (implementation and cloud validation)
- Supersedes: none; extends ADR-0005

## Provenance and coordinated scope

Source: original Draft PR #32, immutable `5ea708e0e1b7f760816380dbf56f303d899cc8e6`, including its Proposed ADR-0006. This independently revertible integration starts at main `0cdd6c1868f7c8339fe0343141b8d23f6f054ac5`, after #39/E2-I1 merged. Current E2-I1 source is authoritative; original #31/#32/#33 are not merged, retargeted or rewritten. Their heads and Draft status remain unchanged.

The primary concern is the Windows adapter's geometric motion. A necessary seven-line Core branch enables A Returning re-entry through existing RequestFocus, only with CanHover and no pending focus switch. It retains Armed expiry, external-foreground, input/drag/Preview and safety gates. The thin App only changes engineering labels. These coordinated changes belong together; no second state machine, E3 contract or product specification changes. All relevant ADRs remain Proposed.

## Geometry and ownership

SpaceDispatcher owns trajectories keyed by generation/TransitionToken and layout target. E1 remains the sole Focus/Returning/completion authority. SpaceLayoutEngine supplies A near-original and B current-work-area-centered endpoints; Workspace IDs and saved layout remain unchanged. Each retarget starts at the most recently applied geometry. Smoothstep position/size interpolation uses monotonic elapsed time, exact endpoints, 240ms engineering duration and an active 16ms timer (ID 2), independent of deadline timer ID 1.

A's fixture expansion contains the original rectangle and transition path. Each Space is painted once. Bounds, rectangular HWND Region, outer hit and GDI paint use the same pixel rounding and Focus-first order. Region application and synchronous GDI painting finish on the owner thread before input resumes. Paint failure can reenter a native callback; after that callback closes the host, frame scheduling must stop. Region/paint failures fail closed. This is not compositor presentation synchronization.

Reversal and pause/resume start from the last applied bounds. Preview and external drag freeze geometry and stop frame scheduling. Resume re-times the remaining path from that exact snapshot, without inferring unpainted elapsed motion. This proves position continuity only, not velocity continuity. Generation/token rejection, delayed completions and finished trajectories cannot restart an obsolete path. External preemption supersedes the trajectory; Suspended/HostLost cancel motion, input and deadlines. Both timer IDs and queued completions are released on shutdown.

## Motion policy and safe boundaries

Public SPI_GETCLIENTAREAANIMATION reads the current BOOL preference at startup and settings/theme notifications. Unknown/failed/throwing queries or high contrast prohibit motion; AppearanceResolver.AllowMotion remains authoritative. Reduced Motion snaps to the exact endpoint unless Preview/drag currently pins geometry; then it stays frozen without timer until release. Restoring the setting permits subsequent/restarted trajectories, not resurrection of completed tokens. The program never writes system settings. Internal injected motion preferences/overrides are rejected for visible hosts and exist only for hidden native tests.

E2-I1 GestureId and PointerLeave cancellation stay intact. WorkAreaGuard remains immutable and fail-closed. Settings/display/DPI/TaskbarCreated, foreground signals, input, deadline, completion and new frame timer paths recheck public monitor information. Changed/unavailable coordinates immediately cancel motion/completions/Core deadlines/gestures, destroy HWND, release hook/session/classes and both timers, and retain the existing diagnostic reasons/exit codes. There is no polling or dynamic coordinate repair.

NoActivate, non-TopMost, public ordinary Z-order, single monitor, Solid/no expensive materials, fake resources and owner-thread guards remain. No WPF/WinUI, WorkerW/private Shell/SetParent, real launcher/store, keyboard hook, search/edit/drop/discovery/delete or elevation is introduced.

## Evidence and open gates

See [E2-I2 integration evidence](../E2_I2_INTEGRATION_EVIDENCE.md) and the independent Draft PR's final-head Actions run. Existing 995 tests remain; additive Core/Platform cases cover safety, repeated reversal, 500 switches, pause/Reduced Motion, generation/token rejection, native Region/paint and timer release. Original E2-I1 24 work-area negative cases remain; new active-motion cases exercise both timers and delayed completion.

Synthetic clocks produce geometry samples, not frame-rate measurements. Native hidden HWNDs prove actual Region/GDI/ownership behavior on the recorded Windows Server runner only. No 60fps/B0, GPU/visual fluency, velocity continuity or product performance acceptance is claimed. Visible Windows 11 focus/input, Win+D/icons/ordinary-app occlusion, Explorer recovery, mixed DPI/multimonitor, UAC/lock/sleep/remote, IME/UIA/accessibility, screenshots/recordings and sustained same-protocol performance remain open. Missing hardware does not block this authorized engineering increment or close any gate.

## Rollback and follow-up

Revert this independent integration PR to restore E2-I1 instantaneous engineering presentation. No persistent data is touched. Independent review and user authorization are required before merge; no automatic merge. E2-I3 should review original #33 as a renderer experiment behind the current host boundary, with explicit focus/IME/UIA, Region/hit synchronization and deployment evidence; it must not automatically accept WPF as the production framework.

## References

- [E2-I1 host candidate](0005-e2-native-engineering-host.md)
- [Interaction specification](../INTERACTION_SPEC.md)
- [Performance protocol](../PERFORMANCE_BUDGET.md)
- [SystemParametersInfoW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
