# E2-I1 independent native host integration evidence

## Provenance and scope

- Base main: `6f2d1411aa63d08d2a996d80ec309b05bfd0a691`, re-fetched and unchanged on 2026-10-15.
- Branch: `feat/e2-i1-native-host-integration`; [Draft PR #39](https://github.com/Niceskys/zhuomian/pull/39) targeting main.
- Original source: PR #31, `06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678`.
- Minimum GestureId reference: PR #32, `5ea708e0e1b7f760816380dbf56f303d899cc8e6`; no animation or Core code imported.
- Original #31/#32/#33 heads, targets and Draft status must remain unchanged. No automatic merge.
- Added: Zhuomian.Platform.Windows, thin Zhuomian.App, Platform.Tests and host package script. No production WPF/WinUI/WinForms dependency.
- Fixed in-memory Workspace/Fake Item/FakeLaunchCounter only. No real launch, user files, Workspace writes, v1/v2/format-3 store, discovery or deletion authority.

## Integration and conflict handling

The review's merge-tree validation found #31 conflicts in `.github/workflows/ci.yml`, `Zhuomian.sln` and `docs/DEVELOPMENT_PLAN.md`. This branch starts at current main instead of merging the old branch. Solution registration adds three projects; all original workflow steps are retained verbatim and in order, with a pinned-history fetch before baseline preparation and new publish/artifact steps appended; the development plan retains E3 status and appends a scoped E2-I1 entry. The ADR index adds Proposed 0005 without replacing 0008–0012. Existing Core, E3 production/test source, Spikes, history scripts, full-history checkout, strict exit-code checks, Release/format, serial solution tests, immutable baseline/legacy comparisons and real Windows NTFS evidence collectors are unchanged.

## Gesture cancellation before/after

New deterministic tests were first run against the unmodified #31 adapter: **4 failed / 7 passed / 0 skipped**, exit 1. Cancel/Leave/DoubleClick retained Core PointerCaptured; Exit also left a pin. The adapter then received current GestureId on both cancellation call sites and centralized PointerLeave cancellation. Re-running the same unchanged tests passed. Coverage includes stale up, fresh Hover/click, OS double-click suppression, drag cancellation/re-entry, external/Suspended/HostLost preemption, Back/Exit and same-timestamp Hover/Preview priority. No test is removed or skipped to fix the regression.

## Work-area and native ownership

WorkAreaGuard compares every edge with an immutable startup snapshot. Production queries use public EnumDisplayMonitors/GetMonitorInfoW, with invalid/unavailable/throwing queries rejected. WM_SETTINGCHANGE, TaskbarCreated and authoritative foreground/input/timer/completion reconciliation check the snapshot. WM_DISPLAYCHANGE/WM_DPICHANGED also recheck, then retain restart-only policy. Unchanged unrelated settings do not stop the host. Changed work area gives `work_area_changed_restart_required`; failed query gives `work_area_query_failed`, both exit 2. The same message invalidates Core input, cancels the deadline/completion queue and immediately releases HWND/hook/session ownership. Relaunch establishes a fresh snapshot. No idle polling or dynamic rebuild.

Portable tests cover unchanged/changed/invalid/throwing queries. Windows-only hidden smoke uses injected query outcomes across eight actual message paths (24 cases), with a real HWND/hook/session, active deadline, queued completion and pinned gesture. It verifies immediate teardown and rejects late up. Other native checks cover query failure at startup, restart snapshot, unchanged settings/Shell messages, Region/hit edges, repeated GDI painting, owner-thread rejection, NoActivate/styles/foreground and configuration rollback. Synthetic geometry/input/query tests are not physical desktop acceptance.

## Verification status

Local Linux SDK 9.0.300: Platform tests after fixes **50 passed / 0 failed / 0 skipped**; conditional native bodies are not Windows evidence. Original main tests remain unmodified; final integrated Windows counts must come from actual TRX, not a hardcoded total.

First integrated Windows run: [38672328095](https://github.com/Niceskys/zhuomian/actions/runs/38672328095), source `8a3b231b088cb5a360c0eb6568d4ae2ad00726cd`, **failed** before solution build. Actual Windows SDK was 9.0.319. `prepare-v2-round3-baseline.ps1` could not resolve pinned `514a5594f594728d2093cb041ed3552472384472`; full main history omitted squash-merged PR objects. Strict missing-evidence uploads also failed; no dummy TRX/artifact was manufactured. The additive workflow repair fetches and verifies the five exact immutable SHAs already used by existing E3 scripts. No baseline, E3 source/test, comparator or assertion is changed. Local exact-SHA fetch was verified successful. This is infrastructure integration repair, not an E3 algorithm change.

Native negative-path deadlines now use an injected frozen clock, so the active deadline cannot expire due to machine scheduling. Pointer leave/cancel message paths also recheck the work area before updating geometry. Public runtime defaults still use the monotonic clock.

Fresh corrected full Windows CI, exact tested commit, Release warning/error counts, smoke and artifact URLs: **pending corrected commit run**. Original #31 CI is provenance only and cannot satisfy this increment. Every failed integrated run remains in history.

The generated package must contain EXE/self-contained runtime, launcher, README, Proposed ADR, this evidence record, BUILD.json with head/merge provenance, dependency/runtime notices, actual ci-native-smoke.log and SHA256SUMS.txt. TRX and E3 evidence remain separate artifacts. Diagnostic startup/resource/timer/GDI numbers are not product performance.

## Open gates and E2-I2

All relevant ADRs remain Proposed. Windows 11 visible desktop/physical focus/input, Win+D, icons, ordinary-app occlusion, Explorer/lifecycle recovery, mixed DPI/multiple monitors/hot-plug, UAC/lock/sleep/remote, accessible keyboard/IME/UIA, visual fluency and same-protocol sustained performance/soak are UNVERIFIED. No P0/E2 exit gate closes; instant geometry is not accepted B0/B1/B2. This is an unsigned engineering directory, not an installer or daily-use application.

E2-I2 depends on independent review/authorization of this increment. Port #32 motion as a separate reversible concern: A/B endpoints, current-value interruption/reversal, generation/completion ownership, synchronized Region/paint/hit, pinned-click/drag freezes and Reduced Motion. Reconcile its small Core re-entry correction with all current E3 tests. Retain all E2-I1 work-area and cancellation tests. No WPF integration, real launch/store activation or final animation parameter/framework acceptance is implied.
