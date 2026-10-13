# E2-2 Windows native animation engineering fixture

This is a restricted engineering candidate, **not an installer, daily-use internal release or accepted WinUI architecture**. See [ADR-0005](../../docs/ADR/0005-e2-native-engineering-host.md) and [E2-2 motion proposal](../../docs/ADR/0006-e2-native-motion.md). All P0 gates remain open. The current implementation is new public Win32/GDI C# code, not a WinForms Spike copy.

## Download and start — no code needed

1. Open the PR's latest successful Actions run. Download `e2-2-windows-x64-engineering` and extract the entire ZIP into a writable local folder.
2. Use Windows 11 x64, one physical display, ordinary local desktop session. Multiple displays are rejected; unplug extra displays before starting. Real user-desktop support is **awaiting acceptance**, including the historically tested 150% setting.
3. Double-click `Start-Engineering.cmd`. Keep the console open. The artifact is self-contained .NET 9, so no .NET/Windows App SDK/VC++ package installation is requested. Do not run as administrator. The unsigned executable may be blocked by local policy; record the message instead of disabling protection.
4. Press Win+D to expose the desktop. If Space is absent, capture the console/log and report failure; visibility is not assumed. A normal application should cover the Space. Opaque Space pixels **can obscure desktop icons**. Placement does not inspect personal icon names or move icons.
5. A: enter the visible Space, default 20ms; leave to return (fixture return delay 120ms, not a frozen product default). External foreground + Disarmed must not expand. A blank click may arm intent; leave/re-enter for a new Hover. B: click title/blank for the same Space to move and enlarge to the current display work-area center; Back follows the return path. Interruptions restart at the displayed bounds. Motion uses provisional 240ms smoothstep/16ms timer parameters, awaiting real acceptance. Back returns; Exit quits the entire fixture. Back/Exit are the rightmost title actions, not Preview Items.
6. Preview's fixed fake Item only increments the in-memory counter once per valid click; it never opens example.invalid or any file/application. Second OS double-click follow-up is suppressed. Closing resets all fixture data. Search, keyboard/IME, editing, OLE drop, persistence, discovery, Dock, video and resource launch are disabled.
7. The host never captures native pointer/keyboard. Leaving cancels a pending click. Drag-like held-button entry suppresses Hover; actual OLE drag/drop is not implemented or accepted. Touch/pen and accessibility automation are unverified.
8. Click either Space's Exit. The log should end with `host_closed`, `windows_remaining=0`, `hooks_remaining=0`, `unhook_ok=True`, `session_release_ok=True`. `engineering-session.log` stays in the extracted folder. Log contains synthetic IDs/state/counters/build/DPI only, no filenames, foreground titles or user destinations. Closing the console terminates its owned process via Windows; the orderly resource-release evidence uses Exit.

## Build and dependencies

Repo baseline: PR #25–#30 merged, main `d994b01`. SDK pinned by global.json (9.0.300/latestFeature), Windows 11 x64, Windows User32/GDI32/Dwmapi/Wtsapi32. No WinUI/Windows App SDK/WinForms production dependency. Tests retain the existing xUnit toolchain. Packaging remains Proposed; .NET 9 runtime support and future SDK upgrade are separate release concerns.

```powershell
dotnet build Zhuomian.sln --configuration Release
dotnet publish src/Zhuomian.App --configuration Release --runtime win-x64 --self-contained true --output artifacts/e2-2
```

Build output on Windows includes `Zhuomian.App.exe`; CI publishes a real win-x64 apphost plus runtime/dependencies, executes it with `--native-smoke`, and includes the actual output log and SHA256 manifest. Keep the whole directory; the EXE alone is not a standalone single-file package. Running from Windows build output requires .NET 9 runtime; the CI publish directory does not.

## Evidence boundaries and diagnostics

- Native motion smoke: forced animation on a hidden host with a synthetic clock, 13 intermediate frame samples, real HWND Region/hit checks, paint execution, reverse continuity and Reduced Motion/HostLost timer cancellation. This does not prove physical pointer or visible smoothness.
- Native smoke: real hidden User32 HWND creation, style/mouse-activate response, unchanged foreground, hook and window destruction. It does not inject input or test a real user desktop. Hidden startup timing is a CI diagnostic, **not cold visible-start performance or P0-07 calibration**.
- xUnit: unchanged Core tests plus deterministic event mapping/token/lifecycle/failure tests and Windows-only hidden native checks. Linux's conditional native case does not establish Windows behavior; Windows CI executes it.
- Display/DPI change safely stops the application with exit 2 and `display_changed_restart_required`; relaunch is required. It does not claim hot-plug recovery. Shell HWND/PID changes reset E1/callbacks; TaskbarCreated is advisory. Public host remains independent of Explorer; real Explorer restart delivery/recovery is unverified, with no infinite idle polling fallback.
- Window loss/failure cancels deadlines/completions and stops input, returns Idle/Disarmed/NoKeyboardCapture, and exits safely. Same-process repeated host creation is tested; automatic HWND recovery after arbitrary loss is not promised.
- Input-desktop/foreground/session signals are re-read before native input, E1 timers and completions. Exact public Shell identity alone is desktop; unknown external remains external; borderless covering-window/locked/disconnected/sleep signals suspend. Foreground notifications are event-driven and coalesced into the single owning dispatcher. Cross-desktop UAC/session behavior remains unverified. No continuous render loop or periodic idle pointer polling.
- Z-order: starts at HWND_BOTTOM; only exact desktop availability permits ordinary Z-order reposition below visible windows preceding the Shell. Enumeration completes with an independent Shell-found flag; absent Shell or unknown boundary stays at HWND_BOTTOM. Never TopMost/SetForegroundWindow/WorkerW/SetParent. Public APIs do not guarantee the wallpaper/icon band; some Shell configurations may hide the host. This must be rejected at acceptance if not visibly usable.
- Solid is rendered from AppearanceResolver.Effective and its content palette/backing, after inherited simulated-glass is denied by capability/budget. High contrast is obtained from SPI_GETHIGHCONTRAST/system colors (unknown falls back to black/white). Transparency remains disabled. SPI_GETCLIENTAREAANIMATION supplies motion permission; false/unknown/high contrast selects Reduced Motion, and AppearanceResolver.AllowMotion is authoritative. Setting changes cancel interpolation and complete at an exact endpoint. Continuous geometry is an engineering candidate; real B0 smoothness, frame cadence and accessibility remain unverified.

## Required real acceptance — currently NOT VERIFIED

Record Windows build (`winver` screenshot), display count/resolution/scaling, local/remote session, latest run/commit and full local log. A 30–60s recording should show startup, Win+D, A enter/leave, B click/Back, fake Preview count, an ordinary overlapping window receiving typing/clicks, and Exit. Include a screenshot of icon overlap/Space visibility and a high-contrast run. Do not include private filenames in shared crops.

AI still needs attended evidence for Win+D repeat cycles, Explorer restart, foreground physical event delivery, UAC/lock/sleep/remote boundaries, real DPI changes, icon conflict handling, input/geometry synchronization and accessible controls. Do not force-kill Explorer yourself; that test needs an AI-owned guarded procedure. All remain open P0 risks.

Cold visible startup, static idle process/window/resources, repeated shutdown release, same-protocol before/after performance, two hardware tiers and soak are **not measured here**. CI hidden-resource logs do not prove product idle budgets. Existing [performance tools](../../scripts/performance/README.md) remain the canonical collection chain; the fixture is not S1–S8 product coverage.

E2-2 adds native interpolation and synchronized geometry with deterministic tests and hidden HWND traces. It does not enable launch/persistence or complete attended host/input/lifecycle acceptance. Do not call this fixture a daily-use release.

## PR #31 Z-order callback regression repair

The old callback returned FALSE on the Shell boundary and mistakenly treated EnumWindows' zero result as API failure. The callback now always returns TRUE; it stops collecting candidates after Shell while completing enumeration. A missing Shell HWND or missing boundary after successful enumeration selects HWND_BOTTOM. External/suspended/lost-host paths do not enumerate or raise. Genuine enumeration failure still throws and fails safely.

Regression tests cover boundary presence/absence, exact desktop availability, unknown/ordinary windows, external/suspended/lost-host states and genuine API failure. Windows CI additionally enumerates a controlled **hidden HWND boundary** through real User32, applies ordinary Z-order with SWP_NOACTIVATE and checks styles/foreground/destruction. Published EXE smoke records the same controlled-boundary path. These are not real Shell-foreground/Win+D or visible ordinary-window-occlusion tests; those remain pending attended acceptance.

Other callbacks audited: production EnumDisplayMonitors always continues; DesktopHosting monitor count and EnhancedHosting window enumeration always continue. Historical disposable MultiMonitorDpi stops on GetMonitorInfo failure, which is an actual failure rather than successful search termination; its outer error attribution can be ambiguous. That Spike is not copied or changed, and its failure path is not reported as new production evidence.

The existing cross-thread rejection test now uses a dedicated Thread and asserts a distinct managed thread ID. Task.Run did not guarantee a distinct thread under the runner's scheduling and produced a false test failure. The rejection assertion is retained and strengthened, without removing/skipping tests or relaxing dispatcher ownership.

## E2-2 future attended animation checklist — deferred until hardware is available

AI prepares the engineering package and validation; the user is not asked to write code. Record the exact commit/artifact, Windows build, monitor resolution/DPI/refresh rate, GPU/driver and session. All the following are **UNVERIFIED** here:

- A enter/leave/re-entry during return; original/expanded/path effective region; edge Clamp and no accidental collapse.
- B original-to-current-display-center trajectory and exact original return; interruption at several points and rapid Back/reactivate; one visible Space identity and no ghost hit area. Exercise 500 alternating switches.
- Preview click before/on/after the 20ms Hover deadline and during animation; exactly one counter increment, no extra click, no wrong target. Drag-like held-button entry freezes geometry and does not activate; actual OLE drop remains disabled.
- Repeat Win+D and desktop exposure; inspect desktop icon overlap and visibility; ordinary overlapping windows retain typing/clicks and fully obscure the host. NoActivate/non-TopMost must hold throughout animation.
- Windows Animation effects off/on mid-transition and high contrast; Reduced Motion uses instantaneous safe endpoints with no timer left. Include separate recordings for normal and reduced motion.
- Alt+Tab/foreground change mid-animation; lock/UAC/sleep/remote boundary and guarded AI-owned Explorer restart; no old token resurrection, input capture, stale hit area or unreleased resources. Display/DPI changes must stop safely; mixed-DPI support is still blocked.
- Visible recordings/screenshots, real frame-presentation/dropped-frame data and canonical same-protocol E2-1/E2-2 resource comparisons on Baseline/Enhanced hardware. Hosted CI does not replace any of these or calibrate product budgets.

Save the full session log and request AI diagnosis for any failure. Screenshot crops must omit private filenames. E2-2 remains Draft with E2/P0 gates open; E3/E4 are out of scope.
