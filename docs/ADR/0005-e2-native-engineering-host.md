# ADR-0005: Restricted E2-1 native engineering host

- Status: Proposed
- Date: 2026-10-13
- Owners: Niceskys (acceptance), AI implementation (engineering evidence)
- Supersedes: none
- Superseded by: none

## Context

Historical #31 baseline main `d994b01` contains PR #25–#30, 330 Core tests, Workspace/Interaction/Layout/Appearance and disposable platform evidence, but no executable integrated host. E2-1 is explicitly authorized to implement a restricted runnable vertical slice without accepting the final framework or closing P0 gates. ADR-0001/0002/0004 remain Proposed.

## E2-I1 integration proposal — 2026-10-15

This record is carried from original Draft PR #31, immutable source `06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678`, into an independent integration branch based on main `6f2d1411aa63d08d2a996d80ec309b05bfd0a691`. Original #31/#32/#33 are not merged, retargeted or rewritten. The historical candidate narrative below describes its original scope, not the current repository's E3 capabilities.

The current increment keeps two new production projects (one primary Windows module and its thin entry), with all existing Core/E3/Spike source and tests unchanged. It remains an engineering verification package, not a production app or framework decision. All ADR statuses remain Proposed.

- Port only the minimum #32 GestureId correction: cancellation uses the current adapter gesture ID. Pointer leave also cancels centrally; late up/double-click follow-up cannot revive it. No E2-2 motion code or Core change is imported.
- WorkAreaGuard rechecks an immutable initial snapshot through an internal injectable query. Actual production queries use public EnumDisplayMonitors/GetMonitorInfoW. Settings, TaskbarCreated, foreground hints and native input/deadline/completion paths recheck without periodic polling. Unchanged settings continue. Changed work area stops with `work_area_changed_restart_required`; invalid/unavailable/throwing queries stop with `work_area_query_failed`. Both exit 2, invalidate E1, kill timers/queued completions and immediately release native ownership. Display/DPI changes remain restart-only even if work area is unchanged. Relaunch queries a fresh snapshot.
- Outer hit testing uses the same rounded pixel rectangle as Region/paint. Native start/run/dispose reject wrong-thread access. Hidden native diagnostics check Region edges, repeated GDI painting, message-driven failure shutdown, initial-query refusal and orderly teardown.
- Current main's launch, v1 persistence and opt-in format 2/3 engineering stores are retained but never referenced by this executable. No E3 permissions, default store, schema, migration or user data behavior changes.
- Fresh integrated Windows CI/artifacts are required; original #31 results cannot validate this branch. See [E2-I1 evidence](../E2_I1_INTEGRATION_EVIDENCE.md). Windows 11 visible desktop, physical input, Win+D/icons/Explorer, multi-DPI, UAC/lock/sleep/remote, accessibility and product performance remain open.

## Options considered

### WinUI 3 / Windows App SDK

Still the product framework candidate in DEVELOPMENT_PLAN. Real XAML/Composition, keyboard/IME and deployment evidence are missing. Framework-dependent unpackaged apps require Windows App SDK runtime deployment and bootstrap; Microsoft's deployment guide also lists VC++ and .NET requirements. A self-contained SDK alternative would require its own published dependency/size/servicing checks. Neither path is adopted or rejected permanently here. No existing production WinUI project can simply be assumed executable.

### Direct Win32 C# / GDI engineering adapter

A narrow message-loop HWND/GDI implementation can execute the existing domain contracts, use public NoActivate/Z-order/region/DPI capabilities and avoid introducing an unvalidated XAML/deployment dependency into this slice. It has limited accessibility/visual capability and does not establish a Shell wallpaper/icon band. Chosen only as an **engineering candidate**, pending actual native CI and attended evidence; no Accepted product architecture is inferred.

### Repackage WinForms Spikes

Rejected. Historical code is disposable proof, not production ownership/input architecture or proof of WinUI integration. This implementation starts new adapter code and uses the existing Core contracts rather than copying Spike classes.

## Engineering proposal and scope

Two production projects, one primary concern: `Zhuomian.Platform.Windows` owns User32 interop, HostLease, FallbackDesktopHost, SpaceDispatcher, fixture and minimal GDI rendering; `Zhuomian.App` is only the executable entry. A single coordinated PR is reviewable/revertible because the tiny entry cannot run without its adapter. No Core/Spike production/test edits or product behavior/spec rewrite. Tests, solution registration, strict CI publish/smoke, evidence boundaries and run instructions ship together.

- Single physical monitor, Windows 11 x64 local-session engineering target. PerMonitorV2 verified before HWND creation; work-area-local DIPs map to pixels once. Multiple monitors rejected; display/DPI change safely shuts down. Future per-monitor orchestration is not accepted by this ADR.
- New WS_POPUP HWND, WS_EX_NOACTIVATE/TOOLWINDOW, no TopMost, SW_SHOWNOACTIVATE and MA_NOACTIVATE. Window region is the union of actual Space rectangles; outside has no host surface/input. Default HWND_BOTTOM; exact exposed desktop may reposition below visible windows preceding Shell. Enumeration continues after finding Shell with a separate boundary flag; missing Shell/boundary falls back to HWND_BOTTOM, and actual enumeration failure remains fatal. Real ordinary-window coverage/Win+D/icon coexistence still needs evidence. Opaque Space can cover icons; explicitly warned, no icon enumeration/movement.
- Shell HWND/PID change resets input and callbacks. TaskbarCreated is only an advisory trigger; no private Shell hierarchy, WorkerW request or SetParent. General host loss exits safely; restoring valid Shell starts Idle/Disarmed/NoKeyboardCapture. No guaranteed complete Explorer recovery matrix yet.
- WinEvent notification (not keyboard interception) posts coalesced hints to the single message thread, re-reading authoritative platform state. Native pointer, due timer and completion paths reconcile before E1 processing. Delayed completion contains Core generation/TransitionToken. No second Focus state machine; pointer gesture recognition is adapter-only. Event cadence/debounce real evidence is pending; the existing 50/100ms reference model is not claimed implemented/accepted here.
- Preview IDs are fixed synthetic Workspace identities; fake launcher only increments an in-memory counter. Pointer leave/drag-like movement cancels pinned click. Held-button entry suppresses Hover without implementing OLE drop; OLE/touch/pen/complex routes remain disabled/unverified.
- E1 computes A 20ms intent, B single-Focus transitions; SpaceLayoutEngine computes current-monitor safe targets, never mutating saved placement. This temporary engineering rendering jumps to the target and queues token completion: **not B0/B1 evidence**, not a validated B2 product fallback.
- AppearanceResolver.Effective and readable content backing are authoritative. Capability/budget deny glass/images/shadows; Solid only. SPI high contrast/system palette has safe unknown fallback; motion/transparency disabled unconditionally. No native animation/accessibility acceptance inferred.
- Framework-independent apphost publishing is `.NET 9 / win-x64 / self-contained directory`. This artifact is not MSIX, installation, updates, uninstall, signed release or packaging freeze. Future supported .NET servicing choice requires release review; the current SDK baseline is deliberately retained.

## Evidence and open gates

Historical Spike single-display evidence is contextual only; it is not evidence for this adapter. Automated evidence: deterministic adapter external behavior, actual hidden HWND lifecycle/style/MA_NOACTIVATE/foreground identity/rollback tests on Windows, the original unchanged 330 Core tests (historical baseline; current integrated counts are recorded separately), strict Release build/format/docs/performance-tool CI and published EXE native smoke. Linux conditional native test cannot count as Windows verification.

The hosted runner is not the real user's interactive desktop. All attended screenshots/physical clicks, Win+D/Explorer, desktop-icon overlap/visibility, DPI/topology, lock/UAC/sleep/remote, accessibility, cold visible startup/idle/soak and same-protocol resource comparisons remain **unverified**. No P0-01..07 gate is closed, no thresholds calibrated and no V0.1 support claimed. A failing visible Fallback cannot be accepted merely because hidden smoke passes.

## Threat review and rollback

Native ownership remains entirely in Windows adapter; Core has no HWND/Win32 dependency. Callback exceptions do not cross unmanaged boundary or export private payloads. Hooks/session notifications/classes/regions/brushes/fonts/windows are owned and released; configure failure releases the candidate HWND and unregisters acquired facilities. Native User32 delegate marshalling is isolated and System32 DLL resolution constrained. No network, execution, import, persistence, global keyboard hook, input injection or elevation capability exists. Negative tests cover stale generation, unarmed external Hover, unknown environment material denial, incomplete click and rollback.

Revert this PR to remove App/adapter/tests/CI artifact steps; domain contracts, existing data and Spikes are unchanged. Unverified host/input/display behaviors must remain blocked from daily-use release. E2-2 owns native interpolation/reversal/current-value continuity, hit synchronization, cancellation and attended evidence; separate product framework/packaging acceptance remains required.

## References

- [Desktop hosting candidate](0001-desktop-host-strategy.md)
- [Foreground classification candidate](0002-foreground-classification.md)
- [Incremental gates](0004-incremental-delivery-gates.md)
- [Engineering run/acceptance instructions](../../src/Zhuomian.App/README.md)
- [Microsoft extended styles](https://learn.microsoft.com/windows/win32/winmsg/extended-window-styles)
- [Microsoft SetWinEventHook](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwineventhook)
- [Microsoft SetWindowRgn](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowrgn)
- [Microsoft Windows App SDK unpackaged deployment](https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-unpackaged-apps)
