# E2-1 Windows native engineering fixture

This is a restricted engineering candidate, **not an installer, daily-use internal release or accepted WinUI architecture**. See [ADR-0005](../../docs/ADR/0005-e2-native-engineering-host.md). All P0 gates remain open. The current implementation is new public Win32/GDI C# code, not a WinForms Spike copy.

## Download and start — no code needed

1. Open the PR's latest successful Actions run. Download `e2-1-windows-x64-engineering` and extract the entire ZIP into a writable local folder.
2. Use Windows 11 x64, one physical display, ordinary local desktop session. Multiple displays are rejected; unplug extra displays before starting. Real user-desktop support is **awaiting acceptance**, including the historically tested 150% setting.
3. Double-click `Start-Engineering.cmd`. Keep the console open. The artifact is self-contained .NET 9, so no .NET/Windows App SDK/VC++ package installation is requested. Do not run as administrator. The unsigned executable may be blocked by local policy; record the message instead of disabling protection.
4. Press Win+D to expose the desktop. If Space is absent, capture the console/log and report failure; visibility is not assumed. A normal application should cover the Space. Opaque Space pixels **can obscure desktop icons**. Placement does not inspect personal icon names or move icons.
5. A: enter the visible Space, default 20ms; leave to return (fixture return delay 120ms, not a frozen product default). External foreground + Disarmed must not expand. A blank click may arm intent; leave/re-enter for a new Hover. B: click title/blank to switch to the calculated center instantly. Back returns; Exit quits the entire fixture. Back/Exit are the rightmost title actions, not Preview Items.
6. Preview's fixed fake Item only increments the in-memory counter once per valid click; it never opens example.invalid or any file/application. Second OS double-click follow-up is suppressed. Closing resets all fixture data. Search, keyboard/IME, editing, OLE drop, persistence, discovery, Dock, video and resource launch are disabled.
7. The host never captures native pointer/keyboard. Leaving cancels a pending click. Drag-like held-button entry suppresses Hover; actual OLE drag/drop is not implemented or accepted. Touch/pen and accessibility automation are unverified.
8. Click either Space's Exit. The log should end with `host_closed`, `windows_remaining=0`, `hooks_remaining=0`, `unhook_ok=True`, `session_release_ok=True`. `engineering-session.log` stays in the extracted folder. Log contains synthetic IDs/state/counters/build/DPI only, no filenames, foreground titles or user destinations. Closing the console terminates its owned process via Windows; the orderly resource-release evidence uses Exit.

## Build and dependencies

Repo baseline: PR #25–#30 merged, main `d994b01`. SDK pinned by global.json (9.0.300/latestFeature), Windows 11 x64, Windows User32/GDI32/Dwmapi/Wtsapi32. No WinUI/Windows App SDK/WinForms production dependency. Tests retain the existing xUnit toolchain. Packaging remains Proposed; .NET 9 runtime support and future SDK upgrade are separate release concerns.

```powershell
dotnet build Zhuomian.sln --configuration Release
dotnet publish src/Zhuomian.App --configuration Release --runtime win-x64 --self-contained true --output artifacts/e2-1
```

Build output on Windows includes `Zhuomian.App.exe`; CI publishes a real win-x64 apphost plus runtime/dependencies, executes it with `--native-smoke`, and includes the actual output log and SHA256 manifest. Keep the whole directory; the EXE alone is not a standalone single-file package. Running from Windows build output requires .NET 9 runtime; the CI publish directory does not.

## Evidence boundaries and diagnostics

- Native smoke: real hidden User32 HWND creation, style/mouse-activate response, unchanged foreground, hook and window destruction. It does not inject input or test a real user desktop. Hidden startup timing is a CI diagnostic, **not cold visible-start performance or P0-07 calibration**.
- xUnit: unchanged Core tests plus deterministic event mapping/token/lifecycle/failure tests and Windows-only hidden native checks. Linux's conditional native case does not establish Windows behavior; Windows CI executes it.
- Display/DPI change safely stops the application with exit 2 and `display_changed_restart_required`; relaunch is required. It does not claim hot-plug recovery. Shell HWND/PID changes reset E1/callbacks; TaskbarCreated is advisory. Public host remains independent of Explorer; real Explorer restart delivery/recovery is unverified, with no infinite idle polling fallback.
- Window loss/failure cancels deadlines/completions and stops input, returns Idle/Disarmed/NoKeyboardCapture, and exits safely. Same-process repeated host creation is tested; automatic HWND recovery after arbitrary loss is not promised.
- Input-desktop/foreground/session signals are re-read before native input, E1 timers and completions. Exact public Shell identity alone is desktop; unknown external remains external; borderless covering-window/locked/disconnected/sleep signals suspend. Foreground notifications are event-driven and coalesced into the single owning dispatcher. Cross-desktop UAC/session behavior remains unverified. No continuous render loop or periodic idle pointer polling.
- Z-order: starts at HWND_BOTTOM; only exact desktop availability permits ordinary Z-order reposition below visible windows preceding the Shell. Never TopMost/SetForegroundWindow/WorkerW/SetParent. Public APIs do not guarantee the wallpaper/icon band; some Shell configurations may hide the host. This must be rejected at acceptance if not visibly usable.
- Solid is rendered from AppearanceResolver.Effective and its content palette/backing, after inherited simulated-glass is denied by capability/budget. High contrast is obtained from SPI_GETHIGHCONTRAST/system colors (unknown falls back to black/white). Motion and transparency are unconditionally disabled until trusted platform support and evidence exist. No B0/B1 or Reduced Motion animation claim; instant geometry is an E2-1 temporary engineering presentation.

## Required real acceptance — currently NOT VERIFIED

Record Windows build (`winver` screenshot), display count/resolution/scaling, local/remote session, latest run/commit and full local log. A 30–60s recording should show startup, Win+D, A enter/leave, B click/Back, fake Preview count, an ordinary overlapping window receiving typing/clicks, and Exit. Include a screenshot of icon overlap/Space visibility and a high-contrast run. Do not include private filenames in shared crops.

AI still needs attended evidence for Win+D repeat cycles, Explorer restart, foreground physical event delivery, UAC/lock/sleep/remote boundaries, real DPI changes, icon conflict handling, input/geometry synchronization and accessible controls. Do not force-kill Explorer yourself; that test needs an AI-owned guarded procedure. All remain open P0 risks.

Cold visible startup, static idle process/window/resources, repeated shutdown release, same-protocol before/after performance, two hardware tiers and soak are **not measured here**. CI hidden-resource logs do not prove product idle budgets. Existing [performance tools](../../scripts/performance/README.md) remain the canonical collection chain; the fixture is not S1–S8 product coverage.

Next E2-2: native motion/interpolation with current-bounds reversal, synchronized hit geometry, cancellation/token ownership and B0 evidence; then attended host/input/lifecycle acceptance. It must not enable launch/persistence or call this fixture a daily-use release.
