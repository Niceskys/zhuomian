# Zhuomian UI-I1 — ENGINEERING ONLY

Windows 11 x64 / one monitor. Self-contained, unsigned engineering fixture.
Run `Start-Engineering.cmd` from the extracted package. Keep Windows security protections enabled.
Use A: hover over Work/Play blank space; B: click Study title/blank space. Any visible fake icon clicks once directly and only increments an in-memory counter. Expanded Back returns; expanded Exit or console close exits.

Work: 320×200 DIP / 8 icons. Study: 260×180 / 6 icons. Play: 380×240 / 12 icons.
Collapsed cards have titles and icon grids. Expanded cards add visual search/toolbar/Back/Exit. Search, sort, settings and view switch are presentation only; they do not activate text or keyboard input.

One HwndSource replaces the GDI HWND in the existing Win32 coordinator. NoActivate, non-TopMost, foreground/WorkAreaGuard and existing A/B animation authority remain. Solid material, no real launch, user Workspace writes, deletion, plugins or elevation. No real user resources are read.

The app can obscure desktop icons and is not accepted daily-use hosting. Display/DPI/work-area changes require restarting. No mixed-DPI/multiple-monitor support claim. No 60 FPS/GPU performance claim.

`--native-smoke` executes the retained hidden GDI regression diagnostics. `--wpf-smoke` executes actual hidden unified-host diagnostics and writes synthetic/offscreen PNGs to `wpf-evidence` in the working directory. Neither command injects global input. Packaged logs and test results describe the recorded Windows Runner only. OFFSCREEN images render the existing submitted WPF visual tree with RenderTargetBitmap; they are not live-window or desktop screenshots. WPF display presentation, physical clicks during animation, Windows 11 desktop coexistence, accessibility and IME remain UNVERIFIED.

See packaged `UI_I1_WPF_EVIDENCE.md`, `0013-wpf-unified-host.md`, `BUILD.json`, test results and `SHA256SUMS.txt`. ADR remains Proposed. The project release license remains undecided; .NET/WPF runtime notices are included separately.


UI-I2 retains the same ENGINEERING ONLY scope and three cards. Incremental visuals and conservative first-press consensus are described in [UI-I2 evidence](../../docs/UI_I2_RENDER_EVIDENCE.md) and [Proposed ADR-0014](../../docs/ADR/0014-wpf-render-input-stability.md). A conflict can cancel a click; real screen presentation remains UNVERIFIED. No real resources or saved Workspace are connected.
