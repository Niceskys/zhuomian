# ADR-0014: Incremental WPF visuals and conservative first-press consensus

- Status: Proposed
- Date: 2026-10-16
- Owners: Niceskys (acceptance), AI (implementation/evidence)
- Supersedes: none
- Superseded by: none

## Context

UI-I2 is authorized against main `74b497da2d3993e58f1c3a788a86faea37453038` after #41. This coordinated renderer/platform/App packaging change addresses one revertible concern: rendering cost and its input boundary. Core, E3, Focus/token/trajectory ownership and historical ADRs remain unchanged. This proposal does not accept WPF as a production architecture or change product specifications.

## Proposal

Retain the sole HostLease-owned HwndSource. Cache Space chrome by Space ID and Item visuals by (Space ID, Item ID), supporting shared resource references without sharing a WPF parent. Keep flat Canvas stacking compatible with authoritative scene ordering. Update changed dependency properties; never clear/rebuild the root in Submit. Keep expanded chrome collapsed when Idle. SceneSpace carries full Item identity inventory separately from visible Item rectangles, so temporary preview capacity changes hide rather than destroy valid Item visuals. Actual identity removal evicts its visual; space removal/reset/dispose drops all references. Immutable PresentationTheme centralizes presentation metrics; SceneLayout centralizes authoritative grid metrics. Palette comes from the existing resolved scene, not another WPF theme/focus controller.

Keep three distinct concepts: Core logical state, successfully submitted CPU scene, unknown display presentation. Region is applied before synchronous Submit on the same coordinator thread; first-down history is recorded only after Submit succeeds. A failed partial Submit clears renderer references and the coordinator fails closed. Region/layout equality does not prove DWM timing. No CompositionTarget.Rendering subscription, WPF animation state machine or idle frame timer is introduced.

Before native PointerDown dispatch, SceneInputGuard compares complete SurfaceHit identity (including blank/Back/Exit) across recent submitted scenes. The bounded engineering lag window is 250ms, capacity 32 distinct hit geometries; repeated identical geometry refreshes its observation time without filling the cache. Conflicting Down cancels E1's gesture and consumes subsequent Up/DoubleClick delivered to the same HWND. MouseLeave/native CancelMode clears the rejected-press latch so future Hover is not blocked; a late Up cannot activate anything because no E1 press remains. Accepted presses are also unpinned on leave, cancellation, or drag threshold. No null-hit fallback activates a Space. Generation changes reject old candidates; frame IDs remain monotonic through cache clear. Capacity overflow rejects presses until the incomplete coverage leaves the lag window. Accepted presses still enter the existing GestureId/InputGeneration path and freeze the submitted picture using existing Preview pause behavior. No new input controller is introduced.

## Limits and alternatives

**250ms is an unverified engineering assumption, not a maximum WPF/DWM latency guarantee.** After old scenes age out, an arbitrarily delayed display could still show them. The guard proves no retargeting among retained candidates, not identity equality with an unknown presented frame. Windows 11 attended high-load/physical-input/presentation tracing remains a release gate. Cancellation counters expose the cost. A ring without overflow rejection, a fake PresentedFrame, UpdateLayout/display equivalence, pixel reads and global input injection are rejected. Permanently disabling animation clicks is rejected; unchanged targets in moving scenes remain usable.

## Evidence and next gate

[UI-I2 evidence and reproduction](../UI_I2_RENDER_EVIDENCE.md) describes deterministic tests, same-protocol baseline source, resource scope and honest performance gaps. Real keyboard/IME activation, visual polish, mixed DPI and production acceptance remain separate. UI-I3 is not authorized by this record.
