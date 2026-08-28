# Phase 0 status

> Updated: 2026-08-28

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

### Explicit focus and input transition — partial pass

A separate interactive probe passed 5/5 runs on Windows 11 build 26100, x64, one monitor:

- a physical click on the visual NoActivate surface preserved external foreground;
- merely showing the keyboard surface did not activate it;
- an explicit physical click acquired foreground and text-box focus;
- guarded Unicode input reached the probe only after both focus checks passed;
- Escape closed keyboard mode;
- the original foreground window and pointer position were restored;
- no probe windows remained.

This validates the basic separation of Visual Focus and Keyboard Focus. It does not validate WinUI integration, IME, accessibility, elevated-window UIPI boundaries or multi-monitor behavior.

### Per-monitor host and DPI mapping — partial pass

The disposable monitor/DPI probe passed 10/10 runs on Windows 11 build 26100, x64, with one 2560×1600 display at 150% scaling:

- the process and host were Per-Monitor V2 aware;
- one independently owned host was created for every attached monitor;
- the host exactly matched the 2560×1528 work area;
- the host had no caption, frame, system menu or border, and its client area equalled its full window area;
- stable monitor identity was hashed before evidence was written;
- deterministic 96/144-DPI, negative-origin, missing-monitor migration and visibility-clamp cases passed;
- cleanup destroyed every host.

The probe reports `Passed: true` but `CoverageComplete: false`. A second physical display, a real mixed-DPI pair and hot-plug cycles were not available, so this evidence must not be treated as completion of the multi-monitor exit criterion.

### Explorer restart and fallback-host recovery — pass with remaining scope

The corrected recovery harness passed 20/20 forced Explorer/Shell restart cycles on Windows 11 build 26100, x64:

- host loss was detected from the Shell process-generation change;
- the stale host was destroyed;
- input capture, animation callbacks and media ownership entered the modeled stopped/released state;
- the replacement Shell was discovered and a new NoActivate host was created without becoming foreground;
- final state was `DesktopAvailable / Disarmed / NoKeyboardCapture / Idle`;
- every run ended with Explorer available and no probe windows;
- recovery ranged from about 0.80 to 1.25 seconds against the provisional 5-second budget.

An earlier 20-cycle baseline that gated recovery on `TaskbarCreated` failed 10/20 cycles. The corrected policy treats that broadcast as advisory and the Shell window plus process generation as authoritative. Abrupt fallback recovery is validated; graceful exit, enhanced WorkerW invalidation, sleep/lock/UAC and long-running soak remain open.

### Foreground classification and Hover gate — partial pass

The foreground-classification probe passed 5/5 runs on Windows 11 build 26100, x64:

- 500/500 real external foreground samples blocked Hover expansion;
- the complete 24-case gate matrix passed on every run;
- exact Shell and explicitly activated Zhuomian surfaces classified as `DesktopAvailable`;
- external/disarmed produced zero expansions;
- full-screen, cloaked, missing-window, inaccessible-process and same-Explorer-process/non-Shell cases failed safe;
- foreground and pointer position were restored after every run.

The accepted candidate rule uses exact Shell HWND identity rather than trusting the Explorer process. Real game/video full-screen transitions, lock/UAC secure desktop and event-delivery/debounce remain open, so [ADR-0002](ADR/0002-foreground-classification.md) remains Proposed.

P0-01 and P0-02 remain open. Still required:

- real dual-monitor mixed-DPI and hot-plug evidence;
- real full-screen and secure-desktop foreground lifecycle evidence;
- enhanced-host and remaining platform lifecycle recovery;
- Enhanced WorkerW comparison and automatic fallback;
- fallback visual usability assessment.

See [ADR-0001](ADR/0001-desktop-host-strategy.md), [ADR-0002](ADR/0002-foreground-classification.md), [the Desktop Hosting Spike](../spikes/DesktopHosting/README.md), [the Focus and Input Spike](../spikes/FocusAndInput/README.md), [the Multi-monitor/DPI Spike](../spikes/MultiMonitorDpi/README.md), [the Explorer Recovery Spike](../spikes/ExplorerRecovery/README.md) and [the Foreground Classification Spike](../spikes/ForegroundClassification/README.md).
