# UI-I1 unified WPF engineering evidence

ENGINEERING ONLY. Main re-fetched before implementation: `31b9d68d5f31953290cfec82c604e21f03dd5897` (unchanged from supplied baseline). Independent branch `feat/ui-i1-wpf-unified-host`; no #31/#32/#33 modification, no cherry-pick, no Accepted ADR.

## Ownership and threading

App STA Main → FallbackDesktopHost coordinator → HostLease → IHostRenderer.Create → WpfHostRenderer.HwndSource. One formal visual top-level HWND; no simultaneously running GDI host, WPF Window, Application, Popup or child HwndHost. Native/GDI remains a separate regression factory selected only by historical tests and native smoke. Configure registers one foreground WinEvent hook and one WTS session subscription; coordinator owns both timers. HwndSource hook delegates only safety/input messages; WPF handles its paint/layout/Dispatcher messages. The WPF DispatcherFrame pumps on the same owning STA. No background thread or CompositionTarget.Rendering subscription is added.

Release: coordinator cancels Core/gestures/completions, kills deadline/frame timers, unhooks WinEvent, unregisters WTS, HostLease calls renderer Dispose, RootVisual clears, source hook detaches, HwndSource disposes. Configure failure uses the same lease rollback. Tests also exercise region/submit failure, work-area change and external DestroyWindow. Dispatcher framework infrastructure belongs to the owner thread and is not a second host; test STA invokes Dispatcher shutdown, app exits its owning thread. WPF framework composition resources are not claimed as a measured process-wide zero count.

## Scene and layout contract

SceneSnapshot: FrameId; HostGeneration (Core InputGeneration, invalidated on host/safety loss); monitor key; PixelsPerDip; ordered immutable Space and Item arrays; VisualBounds; state; hit rectangles; expanded Back/Exit geometry; accessible palette. Array order is the explicit Z-order (Focus last). SpaceDispatcher samples existing VisualBounds/TransitionToken trajectories. Capture never mutates Core or saved placement. WPF does not recalculate authoritative positions or Focus.

Region uses rounded physical Space bounds. HitTest uses identical rounding and reverse stacking. WPF receives the same object and rounds DIP placements using the same physical edges; smoke checks actual Canvas card/item positions and dimensions against the submitted geometry. Captured scenes are held during Preview gestures except safety-generation invalidation, preserving identity through down/up; existing controller cancels stale generations. Visual-only controls receive no WPF input, so they cannot bypass native routing. Hover never reorders items.

| Space | Idle DIP size | Item identities / visible count | Mode |
| --- | --- | --- | --- |
| Work | 320×200 | 8 distinct / 8 | A |
| Study | 260×180 | 6 distinct / 6 | B |
| Play | 380×240 | 12 distinct / 12 | A |

Layout uses 16 DIP horizontal padding, 44 DIP idle title allocation, 64 DIP column spacing, 56 DIP row spacing, 56×52 DIP hit tiles and available-area capacity; ordered ItemIds and future CapacityOverride are honored. Expanded content starts at 136 DIP after Header/Search/Toolbar. All items are fake data under example.invalid, 26 distinct global identities. Fixed fixture parameters are not product defaults. Future data expansion changes the data rather than the WPF view; full scrolling/virtualization is still required before large collections.

Collapsed: title + compact multi-icon preview, no search/settings/sort toolbar. Expanded: header with Back/Exit, visual search, a compact toolbar and full synthetic grid. Toolbar/search are visual placeholders with no keyboard activation, no settings system and no real search. Solid accessible content palette, Segoe UI, colored numbered synthetic icon tiles; no external icon license or Backdrop dependency. Rectangular Region means small opaque corner backing remains around rounded visual cards.

## Actual verification and limits

Windows CI keeps all original 1025 test cases and adds five Platform scene cases (four synthetic DPI scales and unknown-ID refusal), plus two WPF cases (actual hidden integration and wrong-DPI refusal): expected 1032 total. No original test is deleted/skipped/weakened. WPF integration exercises all 26 direct fake clicks/cancellations through messages to the owned hidden HWND, pinned submitted scene, A/B frame samples/return, Reduced Motion, HostLost, NoActivate/non-TopMost, unchanged foreground, actual HWND Region and teardown, five rollback paths, including a throwing renderer submission. The native-close route also exits an actual WPF DispatcherFrame. Existing E2-I2 tests remain the authority for repeated reversal/interruption, stale token, drag pause and WorkAreaGuard variants. Core/E3 sources, tests, historical A/B/C commits, hash and count gates remain untouched. New WPF cases are additive outside the typed Core count gate.

Local Linux can cross-build Windows-targeted code but cannot run WPF. Windows Runner build/format/tests and packaged EXE native/WPF smoke are required. Test totals, runner OS/build, SDK, source SHA and logs are recorded in BUILD.json/TRX. CI artifacts include self-contained EXE, runtime dependencies/notices, startup script, checksum manifest and OFFSCREEN PNGs generated by actual WPF RenderTargetBitmap. These are synthetic/offscreen evidence, not visible desktop screenshots.

**UNVERIFIED:** actual compositor/display presentation synchronization. Logical state, submitted scene and presented pixels are separate. UpdateLayout/InvalidateVisual do not acknowledge display; test scene consistency proves only submission geometry. A delayed presented frame might receive a click resolved against newer submitted geometry. Target pinning protects after down but cannot by itself identify an older picture at initial down. This is an explicit production acceptance blocker. No 60 FPS, GPU acceleration benefit, B0 fluency or product performance claim. No real desktop screenshot is available from the headless evidence path.

Windows 11 attended gates: visible readability/spacing, physical clicks during A/B interruption/reversal, screen-presented frame/Region alignment, ordinary-app occlusion, Win+D/icons, Explorer restart, UAC/lock/sleep/remote, UIA/high contrast and sustained resource diagnostics. Synthetic 100/125/150/200% scenes are not mixed-DPI hardware support. Single-monitor x64 only; existing display/DPI/work-area changes fail closed and require restart.

## Safety and follow-up interfaces

NoActivate and MA_NOACTIVATE remain; WPF controls cannot set KeyboardMode or launcher. No real launch/store/delete/plugin/elevation, global input injection or new global keyboard hook. Existing safety messages retain generation/token cancellation. A future explicit search action must first acquire existing KeyboardMode via a reviewed activation path, set/clear Searching/IME composition flags and restore NoActivate on release, external foreground and HostLost. UI-I2 should resolve presentation/initial-press identity, then explicit focus/IME/UIA before adding real search. No automatic UI-I2 start.

Revert this PR to restore E2-I2 App rendering; no user data migration. [ADR-0013](ADR/0013-wpf-unified-host.md) remains Proposed. Draft PR and Windows CI links belong in the final engineering handoff; actual Windows 11 acceptance remains the user's separate gate.

## Changed-file inventory

- Build/CI: `.github/workflows/ci.yml`, `Zhuomian.sln`.
- App: `src/Zhuomian.App/Program.cs`, `Zhuomian.App.csproj`, `README.md`, `Start-Engineering.cmd`.
- Platform: `src/Zhuomian.Platform.Windows/AssemblyInfo.cs`, `FallbackDesktopHost.cs`, `SpaceDispatcher.cs`, new `ModernEngineeringFixture.cs`, `SceneSnapshot.cs`.
- New WPF project: `src/Zhuomian.Rendering.Wpf/Zhuomian.Rendering.Wpf.csproj`, `WpfHostRenderer.cs`, `WpfSmoke.cs`.
- Tests: new `tests/Zhuomian.Platform.Tests/SceneSnapshotTests.cs`; new `tests/Zhuomian.Rendering.Wpf.Tests/Zhuomian.Rendering.Wpf.Tests.csproj`, `UnifiedHostTests.cs`.
- Packaging: new `scripts/host/publish-wpf-engineering.ps1`.
- Evidence/ADR: new `docs/UI_I1_WPF_EVIDENCE.md`, `docs/ADR/0013-wpf-unified-host.md`; additive `docs/ADR/README.md` index entry.

## Windows DPI harness correction

Run [38753348904](https://github.com/Niceskys/zhuomian/actions/runs/38753348904), source `8d24865d02dea6eca81901203ef591a66a280c3a`, failed WPF preflight with `dpi_context_rejected`; native Platform76 passed. VSTest does not inherit the App's PerMonitorV2 manifest and WPF establishes a different default DPI context before host Start. The harness now uses public SetThreadDpiAwarenessContext on its isolated STA only, restoring it after Dispatcher shutdown. The product's process/thread guard, manifest, single-monitor checks and original tests are unchanged. An additive negative case intentionally uses an unaware thread and requires rejection before HWND/hook creation. The real self-contained App EXE independently runs WPF smoke with its embedded manifest; passing the harness alone cannot prove packaging correctness. This is recorded Windows Server runner evidence, not mixed-DPI Windows 11 acceptance.
