# E2-3 WPF native visual candidate

Independent Windows 11 x64, one-monitor engineering HWND. Synthetic data/counter only. No real launch, persistence or daily-use release. WPF is Proposed, not the accepted product framework. [ADR-0007 and four-route comparison](../../docs/ADR/0007-e2-wpf-rendering-candidate.md).

## Run

Download latest successful Actions `e2-3-windows-x64-wpf-candidate`, extract the complete directory, launch `Start-Wpf-Candidate.cmd`. Self-contained .NET/WPF directory; no SDK or Windows App SDK bootstrap needed for this experiment. Unsigned, asInvoker, PMv2, no installer/system modification. Close each Space's × to exit, Back returns expanded Space. The diagnostic console can also close the process. Exit code 2 means unsupported arguments/topology change; exit 1 means candidate failure. Display/DPI changes require restart; multiple monitors are rejected. No arbitrary command-line path accepted.

A Ideas Space uses hover 20ms at its original location; B Workspace uses title/blank click to move the same Space to its work-area center. Click a Preview tile increments fake counter once, never opens a resource. Four decorative tiles share the same synthetic resource ID. There is one existing E1 controller and E2-2 geometric animation engine. Text input/search/scroll/edit/E3/E4 are absent. Hover gives a grid outline; no product icon-hover animation is claimed.

This independent window always stays at HWND_BOTTOM, may be hidden by Shell, and does not establish a wallpaper/icon band. Expose desktop to inspect if visible; do not treat inability to see it as successful desktop hosting. It does not replace/embed in #31's FallbackDesktopHost; the unchanged E2-2 package remains a separate host baseline. NoActivate/non-TopMost do not prove Win+D/icon coexistence or real app occlusion until attended tests.

## Cloud evidence

Build: `dotnet build Zhuomian.sln --configuration Release`; full format/test/docs/performance tools remain mandatory. Windows CI publishes `dotnet publish src/Zhuomian.Ui.WpfCandidate -c Release -r win-x64 --self-contained true`, then executes actual EXE `--smoke`, checking its exit code before artifact upload. The EXE smoke checks hidden HWND creation/rollback/destroy, actual native styles/MA_NOACTIVATE/foreground preservation, hook/session teardown, shared Region/hit, synthetic A/B/current-bounds reversal, reduced-motion/contrast policy and timer cancellation. No live pointer/keyboard/Shell input injection.

Package includes runtime, EXE, launcher, README, Proposed ADR, BUILD.txt, SHA256SUMS.txt, actual ci-wpf-smoke.log, runtime license/notice inventory. Standard TRX covers all old and new tests. Runtime tier/DPI/build are logged, not performance results.

Three PNGs named `*-offscreen.png` are actual WPF RenderTargetBitmap outputs from the same hidden-window scene at Idle/Expanded/High Contrast. They are NOT screenshots of an on-screen HWND, and cannot prove compositor presentation, font readability on the user's display or visible frame timing. No AI-generated UI is used. No screen capture/recording exists yet. These remain candidate drawing evidence only. Dark colors, radii, spacing, content capacity and animation parameters are provisional; no design is frozen.

## Deferred Windows 11 acceptance

- Visible independent window and later FallbackDesktopHost integration; physical pointer/Preview single click/Back/A leave and B reverse; interruption and pinned click.
- Win+D, ordinary applications always cover, desktop icons coexist/overlap detection, taskbar and Explorer recovery.
- Mixed-DPI/multiple monitors/negative origins/hot-plug; current candidate safely stops/rejects these configurations.
- High contrast/Reduced Motion/transparency preferences, actual font contrast; future explicit keyboard/IME and UIA peers are required before production accessibility.
- Lock/UAC/sleep/session/remote transitions and real owner resource cleanup.
- Same-protocol actual baseline/candidate process/GPU/frame presentation, idle/soak measurements using existing performance tools. No CPU/GPU/frame-rate/product-budget acceptance reported by hidden smoke.

The user has no Windows hardware currently; these checks are explicitly postponed, not passed. No P0/E2 exit gate closes. Do not merge #31/#32/this Draft automatically. Future production framework/composition/host/runtime/packaging/license choices require separate review. E3 can prepare isolated launcher/store contracts; E4 must wait for the relevant accepted boundaries and add asset/UIA/virtualization tests. Rollback this PR only; unchanged E2-2 host remains.
