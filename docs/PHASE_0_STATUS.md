# Phase 0 status

> Updated: 2026-08-30

## Phase 0A — Complete

- Specification and governance baseline merged.
- Active `main` ruleset requires PRs and the strict `docs` check.
- .NET solution, analyzer policy, Core contract and baseline test suite merged.
- CI validates documentation, the performance-evidence contract, the generic process-sampler smoke test, the deterministic per-run sample summarizer and cross-run selector, then restores, builds, formats, tests and uploads TRX evidence with zero annotations.
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

The accepted candidate rule uses exact Shell HWND identity rather than trusting the Explorer process. Real full-screen external-process coverage is recorded below; lock/UAC secure desktop, real event delivery and final production integration remain open, so [ADR-0002](ADR/0002-foreground-classification.md) remains Proposed.

### Foreground event debounce reference model — deterministic pass, OS delivery pending

A web-safe executable reference model now freezes the candidate notification-coalescing semantics and is covered by six deterministic xUnit tests in the normal CI test assembly:

- the first hint after idle requests immediate leading-edge reconciliation;
- burst hints use a 50 ms provisional settle window;
- continuous bursts cannot starve authoritative re-read beyond a 100 ms provisional interval between reconciliation starts;
- every hint advances a generation, invalidating stale classifier results;
- obsolete timer generations cannot consume newer pending work;
- after pending work drains, the reference model has no idle polling work.

This closes only the deterministic debounce/coalescing policy question. The reference model does not install `SetWinEventHook`, observe `EVENT_SYSTEM_FOREGROUND`, measure callback loss/duplication/reordering, cross the production dispatcher, or exercise real HWND churn. Real Windows event delivery and integration therefore remain open. See [the reference-model Spike](../spikes/ForegroundEventDebounce/README.md).

### Full-screen and secure-desktop lifecycle — real classification pass, lifecycle integration pending

The platform-lifecycle probe passed 5/5 automated runs on Windows 11 build 26100, x64, with one physical monitor.

Real platform evidence:

- a separate real borderless process covered the complete primary monitor and became foreground in every run;
- all five windows were detected as external full-screen and classified `Suspended`;
- the live input desktop was accessible and named `Default`;
- WTS session notification registration and unregister cleanup passed;
- every child process exited and the original foreground and pointer were restored.

Deterministic lifecycle-policy evidence from the same runs:

- the modeled Hover gate reported zero expansions after `Suspended` classification;
- modeled pointer capture, keyboard mode, animation and media ownership entered released/stopped state;
- Unlock alone stayed suspended, and Desktop Ready required an accessible `Default` input desktop before safe resume;
- safe resume began `ExternalForeground / Disarmed / NoKeyboardCapture / Idle`.

The Spike does **not** inject Hover into an integrated Zhuomian interaction surface and does **not** own/release real production pointer, keyboard, animation or media resources. The historical JSON fields `SuspendedHoverAttempts`, `SuspendedHoverExpansions` and `RuntimeResourcesReleased` therefore represent policy-model assertions, not live integration evidence.

`CoverageComplete` is false. The unattended probe intentionally did not call `LockWorkStation`, trigger UAC, enter sleep or disconnect the session. Real `WTS_SESSION_LOCK`, `WTS_SESSION_UNLOCK`, `WTS_SESSION_DESKTOP_READY`, Winlogon/UAC, sleep and remote-session transitions require a user-attended compatibility run.

### Enhanced WorkerW comparison and automatic fallback — fallback pass

The enhanced-host probe passed 20/20 runs on Windows 11 build 26100, x64:

- 15 WorkerW windows were observed, but none was visible or work-area-sized;
- the private WorkerW request was delivered without producing a viable candidate;
- cross-process `SetParent` was not attempted against invalid candidates;
- public fallback was selected 20/20 times;
- fallback remained borderless, NoActivate, non-TopMost, work-area mapped and DPI-valid;
- foreground was preserved and every fallback host was destroyed;
- missing candidate, attachment failure and DPI-reset policy cases all selected fallback.

This validates rejection and automatic degradation, not Enhanced WorkerW attachment. WorkerW is now explicitly optional and non-blocking on this environment. Any future cross-build WorkerW attachment remains open; the fallback visual candidate is assessed separately below.

### Fallback visual usability — conditional pass

The public fallback visual-usability probe passed 5/5 runs on Windows 11 build 26100, x64, with one 2560 x 1600 display at 150% scaling:

- controlled Space pixels were visible on the exposed real desktop;
- transparent host pixels passed pointer hit testing through to the desktop;
- a physical click on a visible Preview Item executed its simulated action exactly once;
- the Preview click preserved the Shell foreground;
- a normal non-TopMost application window covered the overlapped Space region;
- the host remained borderless, NoActivate and non-TopMost and was destroyed during cleanup;
- the previous window view and pointer were restored after every run.

This is a conditional product pass. The public fallback has no documented Shell band below desktop icons, so opaque Space rectangles can obscure icons when they overlap. Production work must include placement conflict handling and continuous Z-order maintenance. The committed screenshot proof contains only controlled probe surfaces and excludes wallpaper, icons and filenames.

P0-01 through P0-04 remain incomplete as a group. Still required:

- real dual-monitor mixed-DPI and hot-plug evidence;
- user-attended lock/UAC, sleep and remote-session lifecycle evidence beyond the validated real full-screen classification path;
- real foreground event delivery plus dispatcher/integration evidence under the now-tested debounce policy;
- integrated lifecycle resource-release evidence;
- enhanced-host and remaining platform lifecycle recovery;
- production fallback Z-order maintenance and desktop-icon overlap handling beyond the validated visual candidate.

See [ADR-0001](ADR/0001-desktop-host-strategy.md), [ADR-0002](ADR/0002-foreground-classification.md), [the Desktop Hosting Spike](../spikes/DesktopHosting/README.md), [the Focus and Input Spike](../spikes/FocusAndInput/README.md), [the Multi-monitor/DPI Spike](../spikes/MultiMonitorDpi/README.md), [the Explorer Recovery Spike](../spikes/ExplorerRecovery/README.md), [the Foreground Classification Spike](../spikes/ForegroundClassification/README.md), [the Foreground Event Debounce Spike](../spikes/ForegroundEventDebounce/README.md), [the Enhanced Hosting Spike](../spikes/EnhancedHosting/README.md), [the Fallback Visual Usability Spike](../spikes/FallbackVisualUsability/README.md) and [the Platform Lifecycle Spike](../spikes/PlatformLifecycle/README.md).

## Phase 0C — Web-safe groundwork only

Phase 0B exit conditions above remain open. The following work is allowed in parallel because it does not depend on unavailable attended hardware and does not advance the Phase 0B exit gate.

### P0-07 performance tooling — contract + sampler + per-run summary + cross-run selection pass, product benchmark pending

The machine-readable evidence contract remains enforced by `scripts/performance/validate-performance-evidence.ps1` and its deterministic self-test:

- full 40-character commit SHA, scenario ID and UTC provenance are required;
- Windows/App SDK/GPU driver, CPU/RAM/GPU and display DPI/refresh metadata are required;
- only Release x64 evidence is accepted;
- default 60s warm-up / 300s measure / 3-run deviations require an explicit reason;
- raw result paths must be relative, exist beside the evidence set and avoid traversal/private home paths;
- metrics require Average/P95/P99/max ordering, and frame presentation requires a bounded dropped-frame ratio or explicit non-applicability;
- `CI` and `Exploratory` machine tiers cannot be marked eligible for threshold calibration;
- CI runs one valid and eleven invalid temporary fixtures to verify required acceptance/rejection behavior, including timestamps, selector semantics and median/worst consistency.

A generic owned-process sampler is now added as separate tooling groundwork:

- one fresh directly owned target process is launched per repetition;
- defaults remain 60s warm-up / 300s measurement / 1000ms interval / 3 repetitions;
- raw CSV records UTC timestamp, monotonic elapsed time, normalized process CPU, Private Bytes, Working Set, handle count and thread count;
- structured arguments use `ProcessStartInfo.ArgumentList` rather than shell concatenation;
- non-empty output, target early exit, invalid samples or owned-process cleanup failure fail the run;
- raw CSV does not persist executable/working-directory/home paths;
- CI smoke-tests two short temporary `pwsh` runs plus failure paths and deletes all smoke data.

The deterministic per-run summarizer is also present:

- `scripts/performance/summarize-process-samples.ps1` consumes contiguous `run-01.csv..run-NN.csv` sampler output and emits an intermediate `summarySchemaVersion: 1` JSON summary;
- each run records sample count plus Average/P95/P99/max for root-process CPU, Private Bytes, Working Set, handle count and thread count;
- P95/P99 use the explicit nearest-rank rule `ceil(p * N)` on ascending samples;
- sampler-format validation covers exact headers, UTC timestamps, strictly increasing elapsed time, finite non-negative CPU/elapsed values, integer resource values and numeric ordering through `run-100.csv`;
- CI uses synthetic temporary fixtures to verify the deterministic math and rejection paths; those fixtures are tooling evidence only and are deleted after the test.

The deterministic cross-run selector is now present:

- `scripts/performance/select-performance-runs.ps1` requires an explicit primary metric, statistic and severity direction instead of guessing them from observed values;
- it ranks runs from best to worst, selects nearest-rank 50% as median, selects the highest-severity run as worst and uses the lowest run number for ties;
- its `selectionSchemaVersion: 1` output preserves the complete ranking, selector/unit/policy and median/worst indices and values;
- final evidence `metrics[]` is defined as the median-run metrics, and the validator cross-checks its selected statistic against `medianValue` plus the worst-value direction;
- CI covers higher/lower-is-worse, odd/even counts, ties, missing/duplicate metrics, run gaps, unit mismatch and non-finite data;
- this freezes tooling semantics only; it does not assemble final evidence or create benchmark claims.

This remains **protocol/tooling evidence only**. The hosted runner is not a Zhuomian Baseline/Enhanced performance machine, the shortened `pwsh` smoke test and synthetic summary/selection fixtures are not product benchmarks, no S1-S8 product scenario is measured and no provisional threshold is frozen.

P0-07 remains incomplete. Still required:

- bind the sampler to repeatable Release x64 Zhuomian S1-S8 scenario orchestration;
- assemble complete final evidence metadata from validated raw samples, per-run summaries and run selection;
- collect real raw measurements for applicable S1-S8 scenarios;
- Baseline and Enhanced real-machine evidence;
- same-protocol median/worst-run comparison;
- threshold calibration from reproducible measurements.
