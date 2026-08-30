# Foreground event debounce reference-model Spike

## Question

Can Zhuomian define a deterministic, bounded foreground-notification coalescing policy that reacts quickly to external foreground changes without creating an idle polling loop?

## Candidate policy

This Spike freezes a **reference model**, not production event delivery:

1. A raw foreground notification is only a hint. The authoritative classifier must re-read current platform state when reconciliation starts; it must not trust a delayed event HWND as the final state.
2. The first hint after idle requests a leading-edge reconciliation immediately.
3. Additional hints after a reconciliation starts are coalesced with a **50 ms provisional settle window**.
4. Continuous hint bursts may not postpone authoritative reconciliation indefinitely: the next reconciliation is bounded to **100 ms provisional maximum between reconciliation starts**.
5. Every hint advances a monotonic generation. A timer callback or classifier result carrying an older generation is discarded and cannot consume or overwrite newer pending work.
6. When pending work drains, no periodic timer or high-frequency polling remains active.

The 50 ms / 100 ms values are Phase 0 candidates. They may change after real Windows event-delivery and latency measurements; the safety semantics above must remain explicit if the values change.

## Executable evidence

The executable reference model lives only in the test assembly:

- `tests/Zhuomian.Core.Tests/Interaction/ForegroundEventDebounceReferenceModel.cs`
- `tests/Zhuomian.Core.Tests/Interaction/ForegroundEventDebouncePolicyTests.cs`

Six deterministic tests cover:

- immediate leading-edge reconciliation;
- trailing settle-window coalescing;
- bounded reconciliation under a continuous burst;
- stale classifier-result rejection;
- stale timer-generation rejection;
- zero pending polling work after the queue drains.

These tests are deterministic: they use logical millisecond timestamps and do not sleep or depend on wall-clock scheduling.

## What this does not prove

This Spike does **not** prove Windows foreground-event delivery. It does not install `SetWinEventHook`, observe `EVENT_SYSTEM_FOREGROUND`, measure callback loss/duplication/reordering, cross a UI dispatcher boundary, or exercise real HWND churn. It also does not integrate the reference model into production code.

Therefore ADR-0002 remains `Proposed`. A Windows-attended follow-up must bind the candidate policy to real event delivery, capture latency/loss evidence, and verify that integrated Hover/input lifecycle behavior still fails safe.

## Handoff

Do not copy the test reference model directly into production. Treat it as an executable contract for a later event-source adapter and scheduler implementation.
