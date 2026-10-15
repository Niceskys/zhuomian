# E2-I2 Windows x64 native engineering verification package

This is an unsigned **engineering fixture**, not an installer, daily-use application or accepted production framework. [ADR-0005](../../docs/ADR/0005-e2-native-engineering-host.md) remains Proposed. [Integration evidence](../../docs/E2_I2_INTEGRATION_EVIDENCE.md) separates current results from original #31 evidence.

## Download and start

1. Download `e2-i2-windows-x64-engineering` from this Draft PR's successful Actions run and extract the whole ZIP into a writable local folder. Keep the EXE and all runtime files together. No development environment or separately installed .NET runtime is required.
2. Engineering target: Windows 11 x64, one physical monitor, local session. Actual hosted CI is Windows Server and is not Windows 11 acceptance. Other displays/topologies are rejected; do not infer support from the version check.
3. Double-click `Start-Engineering.cmd`, without elevation. It writes `engineering-session.log` only in the extracted folder. If Windows or organizational policy blocks this unsigned package, record the refusal; do not disable security protection.
4. Press Win+D to attempt to expose the desktop. Visibility is unverified; if absent, report it. The opaque Spaces may cover icons; the program does not read icon filenames or move icons. Ordinary applications must remain able to cover the fixture and retain keyboard focus.
5. A: hover 20ms after safety gates; leave the expanded effective region to return (fixture delay 120ms once Focused). Back can interrupt expansion immediately. External foreground + Disarmed prevents automatic expansion. A blank click can arm explicit intent. B: title/blank click switches to the current work-area center using the continuous geometry candidate when system policy allows. Back returns, Exit closes the entire fixture. 240ms smoothstep / active 16ms timer are engineering parameters, not 60fps or B0 acceptance. Reduced Motion completes instantly; a pinned Preview/drag freezes geometry without a frame timer until release.
6. Preview clicks only increment FakeLaunchCounter. Cancel/leave/drag-like movement cancel the current gesture; OS double-click follow-up is consumed. A subsequent independent click remains usable. There is no real application/URL/file launch, user-file access, user Workspace write, v1/v2/format-3 store, discovery or real deletion. The extracted package's diagnostic log is the only normal session output.
7. Search, keyboard/IME mode, OLE drop, editing, WPF, WinUI, glass/images/shadows, video and Dock are absent. The visual HWND never captures native keyboard or pointer. Back is the engineering return path; Esc is left to external applications.
8. Prefer the Space's Exit for orderly cleanup. Check `host_closed` for windows/hooks/timers zero and successful unhook/session release. Closing the console is OS termination, not the orderly cleanup test.

## Work-area and display safety

The host uses a single startup work-area/DPI snapshot. Settings, TaskbarCreated, foreground hints and native input/timer/completion dispatch re-read public monitor information. Unrelated settings that leave the work area unchanged continue normally. A changed work area immediately invalidates input/deadlines/completions and releases the host with exit 2 and `work_area_changed_restart_required`. Query failure (including unsupported current topology) gives `work_area_query_failed`, exit 2. Restarting the package reads a fresh snapshot. Display/DPI notifications remain restart-only (`display_changed_restart_required` when the work area is unchanged). There is no dynamic rebuild or continuous polling.

## Build, provenance and dependencies

Integration baseline: main `0cdd6c1868f7c8339fe0343141b8d23f6f054ac5`; original source: #31 `06d6f14520ebc1e7cc6486a4b3cdd90f11d7a678`. Original #31/#32/#33 remain separate. Original animation source: #32 `5ea708e0e1b7f760816380dbf56f303d899cc8e6`. Minimal guarded Core Returning re-entry and native geometric trajectories are integrated; E2-I1 cancellation/work-area fixes remain.

SDK uses repository global.json. Runtime: self-contained .NET 9 win-x64, User32/GDI32/Dwmapi/Wtsapi32 public APIs. No Windows App SDK, WPF, WinUI or WinForms production dependency. `DEPENDENCIES.txt` and runtime notices accompany the package; this does not close the repository release license/NOTICE or runtime servicing decision.

```powershell
dotnet build Zhuomian.sln --configuration Release
./scripts/host/publish-native-engineering.ps1 -OutputDirectory artifacts/e2-i2 -CommitSha (git rev-parse HEAD)
```

`BUILD.json` records source/head SHA, checked-out merge SHA, SDK, Windows/runner build and Actions URL. The EXE prints its informational version/head SHA. `ci-native-smoke.log` contains actual hidden native execution; `SHA256SUMS.txt` covers every other package file. Verify hashes before moving individual files; the manifest intentionally cannot hash itself. Full solution TRX is included under `test-results/` and retained as a separate artifact. [ADR-0006](../../docs/ADR/0006-e2-native-motion.md) remains Proposed.

## Evidence boundaries

Hidden smoke checks NoActivate/ToolWindow/non-TopMost, MA_NOACTIVATE, foreground preservation, controlled Z-order enumeration, real Region versus hit edges, repeated GDI painting, owner-thread rejection, work-area message failure shutdown and resource release. Work-area changes and pointer actions in smoke are **synthetic and hidden**; it never changes the real taskbar, injects physical input, restarts Explorer or captures the desktop.

Startup milliseconds, private/working-set bytes, handles, threads, GDI objects, owned HWND/hook/session and idle timer counts are one CI engineering diagnostic, not cold visible startup, a sustained idle/soak benchmark, frame rate or product budget acceptance. No idle frame timer/pointer polling loop exists. AppearanceResolver honors system motion policy and denies transparency/expensive materials and supplies opaque Solid plus high-contrast backing.

Public fallback Z-order cannot guarantee the wallpaper/icon band. Real Win+D, desktop icons, visible ordinary-app occlusion, Explorer recovery/event delivery, Windows 11 physical focus/input, mixed DPI/hot-plug, UAC, lock, sleep, remote-session boundaries, accessible UI and visual smoothness remain UNVERIFIED. Historical Spike success is not proof for this executable. P0/E2 exit gates remain open.

## Future attended acceptance and rollback

Record Windows build, monitor count/scaling, session, exact BUILD.json and synthetic-only log. Later acceptance needs privacy-safe desktop crops/recordings of A/B/Preview/Back/Exit, ordinary app typing/occlusion, Win+D/icon coexistence, high contrast, lifecycle boundaries and same-protocol resource/soak measurements. No user-written code or disabling protection is required. Do not force-kill Explorer manually.

E2-I3 should independently evaluate renderer/IME/UIA integration while retaining these motion/work-area/gesture tests; no real launch/store/UI-framework acceptance follows automatically. Revert this independent increment to restore E2-I1 presentation and its prior publishing labels; E3/Spikes and user data remain unchanged. The coordinated minimal Core Returning change and its new regression cases revert with it.
