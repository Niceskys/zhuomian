# E2-I2 native motion integration evidence

## Provenance and scope

- Authoritative base: main `0cdd6c1868f7c8339fe0343141b8d23f6f054ac5`, verified remotely; #39 merged at that SHA.
- Branch: `feat/e2-i2-native-motion-integration`; [Draft PR #40](https://github.com/Niceskys/zhuomian/pull/40) targeting main.
- Original animation source: #32 `5ea708e0e1b7f760816380dbf56f303d899cc8e6`, including Proposed ADR-0006 and motion tests.
- Original native source: #31 `06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678`. Original #31/#32/#33 heads, targets and Draft status unchanged at intake.
- No production framework adoption, real launch/store/user-file access, search/edit/drop/discovery/delete, keyboard hook or private Shell API. All ADRs remain Proposed.

## Implementation and compatibility

Current E2-I1 files were incrementally edited, never replaced by old #32 files. SpaceDispatcher receives token/generation-owned trajectories, exact endpoints, smoothstep and A/B layout targets from SpaceLayoutEngine. Every reversal starts at the applied frame. Pause/resume re-times from the frozen snapshot; position continuity is required, velocity continuity is not claimed. No saved Workspace layout changes and no second visual Space/state machine.

Core production diff is the minimal Returning PointerEnter branch: CanHover plus no pending Focus, then existing RequestFocus. All Armed/expiry/external/safety/Preview/drag gates remain. New nine-case regression against original Core: **2 failed / 7 passed / 0 skipped**, demonstrating missing ordinary/valid-Armed re-entry; rejected paths remained rejected. The repaired branch is tested with the same cases. A local concurrent build overlapped this deliberate before-fix experiment and observed two expected motion failures; subsequent sequential verification uses restored corrected source. No failed evidence is hidden or test weakened.

Existing GestureId/Leave/Cancel/DoubleClick/Close/Exit logic is retained. Preview/drag freezes geometry and stops timer 2; release resumes from the applied snapshot. Safety supersedes old trajectories. Generation/token completion rejection and finished trajectories prevent delayed completions from restarting motion. Reduced Motion endpoint ownership is retained until E1 consumes completion, so restoring system motion before a delayed completion cannot start a zero-distance animation/timer. Existing E2-I1 test files remain unchanged except additive Core cases in its original test class.

WorkAreaGuard and its original 24 native failure cases remain. New active-motion cases cover 27 query/message combinations, with timers 1 and 2 and delayed completion already owned before failure. Both timers, gestures/Core deadlines/completions/HWND/hook/session/class are released immediately. Original reasons `work_area_changed_restart_required`, `work_area_query_failed` and display/DPI restart policy remain. No polling or dynamic repair.

Each owner-thread frame updates VisualBounds, rectangular Region and synchronous GDI paint before returning to ordinary input. Outer hit and paint use the same pixel rounding and Focus order. Hidden checks inspect real Region edges at intermediate samples, single paint identity/Focus-last ordering, GDI object counts and actual paint execution. Injected Region/paint failure closes the HWND. Nested paint failure cannot schedule another frame after teardown.

Public SPI_GETCLIENTAREAANIMATION, high contrast and AppearanceResolver drive motion policy. Unknown/failed query disables motion; settings/theme notifications refresh it. Tests inject preferences only on hidden hosts, never write system settings and reject visible-host overrides. Reduced Motion is an independent supported path, including pinned Preview/drag, not an animation failure.

## Verification and delivery

Local .NET SDK 9.0.300: Core **460 passed / 0 failed / 0 skipped**, Platform **71 passed / 0 failed / 0 skipped**, App Release build **0 warnings / 0 errors**; project-based format checks succeeded. These tests and synthetic samples are development evidence only. Windows-only conditional bodies must execute on Windows before delivery. Final full Windows CI, actual suite totals, Release warnings/errors, native smoke, runner/OS/display/DPI and artifact are recorded in this Draft PR's delivery record and exact-head BUILD.json. Original #32 or #39 CI cannot substitute for this increment. Any failed/superseded integrated run remains in history.

An additional Windows Platform preflight detects native motion faults before the long complete solution run; it uploads separate TRX and does not replace any full-suite check. The existing CI's E3 baseline fetch, native launch, NTFS, crash/authority/history/immutable comparison/performance collection, strict exit propagation, full Release/format and serial tests remain. The native publishing step is upgraded from E2-I1 to the new E2-I2 label/artifact and retains all its safety gates, adding motion markers and the narrowly documented Core exception to source isolation. It does not publish current motion under an obsolete E2-I1 artifact name.

Integrated Windows run [38692302430](https://github.com/Niceskys/zhuomian/actions/runs/38692302430), source `62a75c13d370ca2f679a0f6bee141738ecb18525`, **failed after full solution tests**. Actual results: Core 460, Launch 37, Persistence v1 171, Persistence V2/format-3 286, Platform 71: **1025 passed / 0 failed / 0 skipped**. Native Platform preflight passed. Complete Release build had **0 warnings / 0 errors**, full format passed. Capacity/crash/authority/legacy-compatibility checks passed, but the outer historical performance wrapper rejected the authorized interaction source/test changes. Strict missing-evidence uploads also failed; no engineering package was published from that failed run.

The infrastructure repair preserves the original immutable SHA `b3b278b0759d5c0af55e224b250c27c33a6bbb10`, all A/B/C SHAs, original detached benchmark execution, six orders and exact historical C freeze. Its outer current-source check accepts only two exact Git blob identities: the original or the reviewed minimum #32 Returning controller, and the original or the exact additive nine-case Core regression file. Every other original source/test file remains byte-identical. No broad Core exclusion, baseline substitution or E3 assertion relaxation is used. New preflight fault tests reject controller/test/other-Core/storage/storage-test mutation and missing original tests. The publisher reuses this check. The single outer history-wrapper tooling adaptation is documented explicitly; E3 production/tests and other history scripts remain unchanged.

Infrastructure preflight run [38696159390](https://github.com/Niceskys/zhuomian/actions/runs/38696159390), source `a576f03647ce40c2fdb2636950b8a5e38c3f0bb3`, **failed** before full solution execution. Exact-blob acceptance, five mutation rejections and missing-test rejection all worked, but the deliberately failing final hash-object left exit 1 as the script's native exit status. The fixture now restores the deleted test and reruns the complete real isolation check successfully before reporting success. No assertion or strict workflow exit handling is removed. Missing evidence uploads remained strict failures.

The final corrected head's full CI/smoke/artifact and any other earlier runs are recorded in Draft PR #40 and exact-head BUILD.json. This failed run's passing tests are not proof of a successful final pipeline or package.

The self-contained win-x64 package includes EXE/runtime, launcher, README, ADR-0005/0006, this record, BUILD.json exact head/checked-out merge/source #32 SHAs, dependencies/runtime license notices, actual published-EXE native-smoke log, full TRX copies, isolation proof and SHA256SUMS. Extract the complete package; no development environment or separate runtime installation required. It remains an unsigned engineering directory, not a formal application/installer. Never disable Windows protection to run it.

## Evidence boundaries and E2-I3

240ms smoothstep and 16ms scheduling are unaccepted engineering parameters. Synthetic frames are geometric samples; GDI draws are not final presented frames. Hidden Windows Server results do not establish 60fps, velocity continuity, GPU performance, visual fluency or B0/product performance acceptance. Resource snapshots and GDI counts are diagnostics only.

Windows 11 physical input/focus, Win+D/icons/ordinary-app occlusion, Explorer crash recovery, mixed DPI/multiple monitors, UAC/lock/sleep/remote, screenshots/recordings, full IME/UIA/accessibility and same-protocol sustained performance remain UNVERIFIED. No P0/E2 gate closes.

Independent review and explicit authorization precede merge. Revert this increment to restore E2-I1; original candidates and user data remain untouched. E2-I3 should independently evaluate original #33's renderer boundary and focus/IME/UIA/Region/deployment evidence, without accepting WPF or starting another app automatically.
