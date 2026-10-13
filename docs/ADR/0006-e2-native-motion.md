# ADR-0006: E2-2 native continuity engineering candidate

- Status: Proposed
- Date: 2026-10-13
- Owners: Niceskys (future Windows acceptance), AI (implementation and cloud validation)
- Supersedes: none; extends the restricted candidate in ADR-0005

## Context and scope

Stacked on PR #31 at `06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678`; #31 remains Draft and main is unchanged. The user currently has no Windows machine and explicitly authorizes cloud development with attended acceptance deferred. This increment implements E2-2 only, following existing PRODUCT_SPEC/INTERACTION_SPEC. It does not accept the final rendering framework or close E2/P0 gates.

The primary module is the Windows adapter. A small Core correction connects A return-time pointer re-entry to the existing RequestFocus reversal, guarded by CanHover and no pending explicit switch. No additional state machine is introduced. The App entry only changes the engineering label; these coordinated changes must ship with adapter tests and can be reverted together. No product requirements, Workspace schema, launch, persistence, search, editing or E3/E4 work changes.

## Engineering decision

Retain the E2-1 public User32 HWND and Solid GDI rendering for this controlled experiment. A composition/XAML replacement needs separate framework and deployment evidence; it is not adopted in this increment. A single HWND renders each Space exactly once with its original Workspace identity; B is not a duplicate popup or fade-in. This is a continuous-geometry implementation candidate, **not accepted B0 smoothness/performance evidence**.

SpaceDispatcher stores only geometric trajectories keyed by E1 generation/TransitionToken and layout target. E1 owns Focus, Returning, pending switches, availability and completion. SpaceLayoutEngine supplies A near-original and B current-work-area-center endpoints; saved bounds are never mutated. Each new trajectory starts at the last rendered bounds. Smoothstep interpolation applies to position and size, with exact final endpoints. Engineering parameters are 240ms and an active 16ms one-shot timer, not confirmed product defaults or a 60fps promise. Timers sample elapsed monotonic time rather than accumulating frame deltas, so late callbacks finish correctly.

The adapter freezes trajectories during pinned gestures/external held-button suppression, releases the animation timer while frozen, and adjusts elapsed time on resumption. PointerCancel now supplies the actual adapter GestureId to E1 so it releases the pinned click. Safety preemption cancels gestures and supersedes old trajectories. Suspended/HostLost reset geometry to safe idle, discard motion ownership and queued completions, kill both timers and release HWND/hook/session facilities on teardown. Native completion messages reconcile first and E1 rejects obsolete generation/tokens. Queued stale frame timers only re-evaluate authoritative state; they cannot resurrect an old trajectory. Completion at an endpoint does not restart a trajectory while its message is pending.

All visual rectangles are applied to the HWND Region and synchronously painted on the owner thread before returning to input dispatch; paint/Region/outer hit use identical pixel rounding. A fixture expansion contains its saved idle rectangle and the transition path, so it does not leave the activation pointer behind. Region remains the union of actual visible rectangles, not a permanent full-screen input surface. Focus-first stacking is shared by hit and paint. Responsive geometry is supported for the synthetic content only; full Item Grid/search/scroll remain later work.

## Accessibility and fail-safe paths

SPI_GETCLIENTAREAANIMATION reads the Windows BOOL setting at startup and settings/theme change. AppearanceResolver.AllowMotion remains authoritative. False/unknown or high contrast disables interpolation, cancels its timer and snaps to a safe exact endpoint before token completion. A held Preview/drag keeps its geometry pinned without a frame timer until release; the nonanimated endpoint then completes. Reduced transparency remains unconditional, materials/images/shadows still denied. Policy logs record SPI availability, value, high contrast and actual Reduced Motion decision. Reduced Motion is a separate intentional path, never mislabeled as B0 failure or smoothness proof.

NoActivate, TOOLWINDOW, non-TopMost, SWP_NOACTIVATE, MA_NOACTIVATE and ordinary application Z-order protections remain. No keyboard capture, global keyboard hook, Shell hierarchy manipulation, real resource launch, network, elevation or input injection is added. Unknown display/DPI changes still stop safely; single-monitor Windows 11 x64 restriction remains. A settings-query failure disables motion. A timer/Region/paint/native failure closes the engineering host, rather than continuing with stale input geometry. User desktop visibility and icon conflicts remain unverified risks of the original host.

## Evidence and remaining gates

Deterministic adapter tests use injected clocks, not sleeping or inferred frame cadence. They cover A/B intermediate bounds, work-area containment, exact return, repeated reversals, old completion rejection, 500 switches with single Focus, Preview/drag freezes and release, external foreground preemption, Reduced Motion and HostLost. Existing Core and E2-1 tests remain. Windows CI runs full Release build/format/tests and hidden native smoke of the published self-contained EXE.

Hidden native tests use a synthetic clock and explicit adapter input, inspect actual HWND Region versus hit at intermediate-frame edges, execute paint, check no foreground change, reverse from the current frame, disable the motion timer for Reduced Motion/HostLost, and verify window/hook release. A test-only internal override can force motion **only on a hidden host** to exercise both paths without changing the runner's accessibility settings. Logs explicitly say synthetic/hidden and include bounds/token traces. This is not physical mouse input, visible frame presentation, Win+D, Shell-desktop or smoothness validation. Resource/timing diagnostics remain CI diagnostics, not P0-07 benchmarks.

The engineering artifact includes the EXE/runtime, launcher, README, both ADRs, BUILD provenance, native smoke log and SHA256SUMS; TRX is a separate artifact. No screenshots/recordings or same-protocol real before/after performance can be collected here. Follow the [attended checklist](../../src/Zhuomian.App/README.md) after hardware becomes available. Real desktop and visual/performance Definition of Done remains open. Keep both PRs Draft; no automatic merge.

## Rollback

Revert the E2-2 commits on this feature branch to restore the exact E2-1 instant geometry candidate. No persisted data or user resources are touched. Do not retarget to main until E2-1 dependency is accepted through a separate user-directed review.

## References

- [E2-1 host candidate](0005-e2-native-engineering-host.md)
- [Interaction specification](../INTERACTION_SPEC.md)
- [Performance protocol](../PERFORMANCE_BUDGET.md)
- [Windows SystemParametersInfoW](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
