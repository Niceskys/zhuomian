# Phase 0 status

> Updated: 2026-08-27

## Phase 0A — Complete

- Specification and governance baseline merged.
- Active `main` ruleset requires PRs and the strict `docs` check.
- .NET solution, analyzer policy, Core contract and 15 tests merged.
- CI restores, builds, formats, tests and uploads TRX evidence with zero annotations.
- Diagnostic field/privacy contract established.

## Phase 0B — In progress

### Fallback Desktop Host / NoActivate — partial pass

The first disposable public-Win32 fallback probe passed 20/20 runs on Windows 11 build 26100, x64, one monitor:

- foreground preserved after show;
- host never became foreground;
- `WS_EX_NOACTIVATE` and `WS_EX_TOOLWINDOW` present;
- `WS_EX_TOPMOST` absent;
- `WM_MOUSEACTIVATE` returned `MA_NOACTIVATE`;
- bottom placement and monitor work-area mapping succeeded;
- cleanup destroyed the host.

This does not close P0-01 or P0-02. Still required:

- physical click and explicit keyboard-mode end-to-end test;
- per-monitor mixed-DPI orchestration;
- Explorer restart and repeated host recovery;
- Enhanced WorkerW comparison and automatic fallback;
- fallback visual usability assessment.

See [ADR-0001](ADR/0001-desktop-host-strategy.md) and [the Spike](../spikes/DesktopHosting/README.md).
