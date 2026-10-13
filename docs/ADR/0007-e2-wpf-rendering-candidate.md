# ADR-0007: E2-3 isolated WPF rendering candidate

- Status: Proposed
- Date: 2026-10-13
- Owners: Niceskys (architecture/visual acceptance), AI implementation (engineering evidence)
- Supersedes: none
- Superseded by: none

## Context and scope

PR #31 remains Draft at `06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678`; PR #32 remains Draft at `5ea708e0e1b7f760816380dbf56f303d899cc8e6`. This branch starts exactly at #32 and targets its branch, not main. The user explicitly authorizes cloud UI development while deferring attended Windows acceptance. Existing E1/E2 tests and the E2-2 EXE remain. No production framework, hosting, composition or packaging ADR is Accepted by this experiment.

The concern is one independently removable candidate module plus its tests/CI/docs. It adds no launcher, persistence, schema, discovery, editable layout or E3/E4 behavior. No disposable Spike source is copied. #31/#32 implementation and branches are unchanged. The local manifest repeats the existing public DPI/asInvoker declarations only.

## Candidate comparison

Each cell is a technical feasibility assessment, **not measured product performance**. A/B/C refer to these candidates; D is the actual #32 baseline. Documentation describes capability, not successful host integration. The links below were checked during this increment.

| Criterion | A: WinUI 3 / Windows App SDK / Composition | B: WPF / public HWND | C: HWND / Direct2D + DirectComposition | D: #32 Win32 / GDI |
|---|---|---|---|---|
| HWND / NoActivate / Z-order / Region | HWND interop exists; XAML focus/presenter/bootstrap and host clipping need a runnable test | HwndSource creation styles and message hook are public; candidate tests styles/MA_NOACTIVATE. Independent window only | Maximum HWND control; COM device/target ownership must be implemented | Existing hidden HWND/Region tests; real Shell band still open |
| B same-Space continuous/reverse | Composition transforms viable; completion/token ownership and hit synchronization must be designed | Reuse #32 current-bounds trajectories, draw same IDs once, no second animation/state machine | Visual transforms/commits viable; readback of current presentation and hit snapshot is extra work | Existing synthetic geometry/reversal; visible presentation unmeasured |
| Geometry agreement | XAML layout and compositor transform can drift from native hit/Region; needs one authority | Immutable snapped host-local scene drives paint/Region/hit. WPF retained presentation is asynchronous, not screen-frame proof | Own scene graph can share geometry; compositor presentation still needs measurement | Shared rounded physical bounds already tested |
| Solid / radius / opacity / glass / image / shadow | Modern controls/materials; actual backdrop is optional and potentially expensive | WPF drawing primitives/brushes/images/effects available. This slice implements opaque Solid/radius only | D2D brushes/bitmaps, DWrite text, DComp effects; more resource/device-loss code | Solid/text basic; modern material/text/effects costly to extend |
| Multi-monitor / PMv2 | Per-monitor windows and mixed DPI require testing | DIP model + HwndSource potential; candidate uses manifest PMv2, snapped pixel edges. Single monitor, display/DPI change closes safely | Full pixel/DIP control, device/monitor mapping is manual | #32 single monitor, changes stop; no mixed-DPI acceptance |
| Idle / GPU / diagnostics | Retained compositor; idle is achievable, not guaranteed; ETW/GPU tools needed | Retained drawings, no CompositionTarget.Rendering or Storyboard. Active/deadline-only timers; WPF tier logged, GPU/CPU unmeasured | GPU pipeline/commits, explicit device loss, no perpetual loop required; PIX/ETW possible | CPU raster + WM_TIMER; idle timers already stop, frame time unmeasured |
| Accessibility / high contrast / focus | Controls/UIA/keyboard available; NoActivate versus explicit search/IME still needs integration | Framework UIA/control support exists; custom drawing DOES NOT supply usable UIA peers. Candidate has platform palette + reduced motion, no keyboard surface | UIA/provider/keyboard integration largely manual | Minimal accessibility; no accepted keyboard surface |
| Build / deploy / maintain | Windows SDK/WinAppSDK/XAML tools; unpackaged framework-dependent bootstrap/runtime and VC++ requirements; self-contained alternative needs its own audit | Existing .NET SDK WindowsDesktop pack; no WinAppSDK bootstrap. Windows CI publishes self-contained x64 directory. Retain inherited .NET 9 only for controlled experiment; production supported-runtime upgrade/servicing decision remains | Public OS APIs, C# interop or audited binding dependency; manual COM/D3D lifetime burden | Small inherited self-contained .NET 9 baseline |
| Free / source licenses / redistribution | WindowsAppSDK source MIT; binary dependencies/redistribution terms and notices still audit-required | dotnet/wpf source MIT; .NET distribution license/notices retained in package. Repo's own license not decided; no extra runtime NuGet binding/font/icon library | OS API usage adds no third-party binding in principle; any chosen binding/license must be audited | OS libraries + .NET; project license/release audit still required |
| E1/E2 fit / rework | Strong long-term candidate; largest new bootstrap/XAML/host boundary in this slice | Low-cost reversible experiment reuses Workspace, controller, layout, appearance and motion. Custom drawing is not full E4 control stack | Best control at highest low-level maintenance cost; avoid broad rewrite now | Useful regression baseline, insufficient proof of visual maturity |

## Proposed experiment, not final selection

Implement B as `Zhuomian.Ui.WpfCandidate`, an independent HwndSource popup using `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, MA_NOACTIVATE, PMv2 and non-TopMost. Always place this **candidate** at HWND_BOTTOM after showing; ordinary windows may cover it and Shell may hide it. This deliberately conservative Z-order probe is not #31's complete FallbackDesktopHost integration, wallpaper/icon placement proof or usability acceptance. The candidate can be invisible on an exposed desktop; report it, do not use WorkerW or raise over applications to fix it. E2-2 remains runnable separately and unchanged.

The existing SpaceDispatcher supplies WorkspaceModel, E1 controller, fake counter and E2-2 motion; SpaceLayoutEngine supplies endpoints without altering saved positions. CandidateScene uses IconGridLayout for four synthetic tiles per Space (same single fake resource), AppearanceResolver for Solid/radius/safety and one frame for paint/Region/hit. Focus owner is painted last and hit first. No new behavioral state machine, native keyboard capture, global keyboard hook, command execution, resource launch, network, elevation or data writes. Console diagnostics use synthetic IDs/codes, not private title/path/exception text. `--smoke` writes only fixed CI offscreen image names beside the executable; it cannot take arbitrary output/asset paths.

Public WinEvent notifications coalesce onto the owner dispatcher; authoritative foreground/input-desktop/session/Shell generation is re-read before input, timer and completion. Missing identity/locked/sleeping/full-work-area borderless foreground suspends safely. Input/drag pins existing trajectories. Display/DPI changes stop with a diagnostic exit instead of continuing stale hit mapping. Native create/configure/Region failures close the candidate. Hooks/session/Region/timers/RootVisual/queued callbacks are owned and released, callbacks after disposal do nothing. Idle has no frame timer, pointer polling or Rendering subscription.

A top-level HwndSource is intentionally used instead of child embedding: child-HWND airspace, clipping, DPI, WPF input/focus and coordinated device teardown remain separate integration risks. This experiment validates only the independent boundary. WPF drawing/Region updates are on the same thread with a single scene, but WPF's retained renderer presents later; no exact screen-frame/input synchronization or smoothness claim follows.

## Visual and access boundaries

Deep neutral opaque palette, title/header, rounded outline, vector-letter icon grid, idle Preview label, expanded Content label, Back/Exit and hover outline. Numerical colors/radius/grid/240ms motion remain **engineering parameters**, not user-confirmed defaults. Synthetic fixture labels are not real resources. No search, scroll, editor, text input, installer, Acrylic, shader, opacity blending, glass, image decode or shadow effect is enabled. Although WPF supplies paths for those, they require asset limits, ownership, visual contrast and performance tests before adding. High contrast uses platform Window/WindowText with a validated opaque fallback; reduced motion uses platform client-area animation permission and final appearance policy. Reduced transparency is always Solid. There is no keyboard interaction mode: Esc belongs to external applications; return uses Back. Future explicit keyboard surface and UIA peers are blockers for production accessibility, not silently passed tests.

## Evidence, decisions and next dependency

Full canonical Windows CI builds/formats/tests all existing projects, including new STA native tests, publishes/runs self-contained candidate EXE and preserves actual smoke logs. Tests cover lifecycle/rollback/native styles/foreground/no keyboard, A/B intermediate and reversal, Region/input centers, policies, DPI mapping and teardown. CI uses hidden HWND and synthetic input; no real user pointer or Shell action is injected. Runtime tier is an observation, not proof of hardware acceleration.

`idle-offscreen.png`, `expanded-offscreen.png`, `high-contrast-offscreen.png` are **actual WPF RenderTargetBitmap outputs**, not AI graphics and NOT desktop screenshots. They can identify drawing issues only. Hidden smoke/Windows Server, offscreen rendering and synthetic trajectories cannot satisfy Windows 11 visual/Win+D/ordinary-window occlusion/icons/mixed-DPI/Explorer/lock/UAC/sleep/remote/presentation/performance acceptance. No S1-S8 performance result or P0/E2 gate is closed. Real same-protocol baseline/candidate performance must be collected later; no frame-rate or resource budget claims.

Before production UI: accept an explicitly scoped framework after host integration/visible visual input tests; choose retained geometry versus compositor transforms and completion authority; validate per-monitor host and DPI lifecycle; design explicit keyboard/IME/UIA surface; choose supported runtime, packaging/service/signing and release license/NOTICE. No ADR status is upgraded here. Minimum future E3 dependency is UI-independent reviewed launcher/store contracts, with fake counter until their gates pass. E4 depends on accepted renderer/host/focus boundary, virtualized browse and asset policies; neither is implemented in this PR.

## Rollback

Remove/revert this PR's candidate/test projects, solution entries, new CI steps and ADR/docs entries. #32's GDI host/animation and every pre-existing test remain byte-for-byte unchanged. No user data, assets or schema to migrate. Stop candidate process and use E2-2 engineering package. Do not merge or retarget any stack branch automatically.

## Primary references

- [HwndSourceParameters public creation and focus parameters](https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.hwndsourceparameters?view=windowsdesktop-9.0)
- [WPF retained visual drawing, clipping, text, images](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/graphics-multimedia/wpf-graphics-rendering-overview)
- [Windows App SDK unpackaged deployment and requirements](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-unpackaged-apps)
- [Windows App SDK self-contained deployment](https://learn.microsoft.com/windows/apps/windows-app-sdk/deploy-self-contained-apps)
- [DirectComposition initialization and HWND target](https://learn.microsoft.com/en-us/windows/win32/directcomp/initialize-directcomposition)
- [Public extended window styles](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
- [WPF MIT source license](https://github.com/dotnet/wpf/blob/main/LICENSE.TXT)
- [WindowsAppSDK MIT source license](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE)
- [Existing host proposal](0005-e2-native-engineering-host.md), [motion proposal](0006-e2-native-motion.md)
- [Run and deferred acceptance](../../src/Zhuomian.Ui.WpfCandidate/README.md)
