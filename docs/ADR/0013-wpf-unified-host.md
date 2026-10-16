# ADR-0013: Unified Win32 coordinator with WPF rendering

- Status: Proposed
- Date: 2026-10-15
- Owners: Niceskys (acceptance), AI (implementation/evidence)
- Supersedes: none
- Superseded by: none

## Context and scope

UI-I1 is explicitly authorized as a coordinated engineering implementation at main `31b9d68d5f31953290cfec82c604e21f03dd5897`, following #39/#40. WPF is the preferred renderer for verification, not an Accepted production architecture. The new renderer, platform seam and thin App must change together to demonstrate unique HWND ownership. This is one revertible hosting concern across these projects; no E3/Core changes or product specification changes. No code is merged/cherry-picked from #33.

## Proposal

Keep FallbackDesktopHost as Win32 coordinator. An optional IHostRenderer supplies exactly one top-level HwndSource, its RootVisual, Dispatcher frame and source disposal. HostLease calls the same Configure/ReleaseWindow callbacks for either factory. App selects WPF; the existing native/GDI factory remains for historical regression and is never run alongside WPF in the visible app. The coordinator alone owns foreground hook, session subscription, safety, ordinary Z-order, WorkAreaGuard, timers and native input. No Window/Application, popup, TextBox or extra application-controlled HWND is created by the renderer.

WPF may create framework-owned message infrastructure (Dispatcher/composition/IME services). These are not second desktop hosts and receive no Core/hook/session/launch authority. HwndSource disposal detaches visual and hook; the shared thread Dispatcher belongs to the entry/test thread, which shuts it down or exits after host teardown. Native hidden tests must check actual host destruction; counting only the public Handle is insufficient.

SpaceDispatcher remains the sole motion adapter over Core. SceneSnapshot freezes ordered bounds, item identity/order/hit rectangles, state and palette, frame/input generation and monitor/DPI identity. Region and native hit read that same submitted object; renderer consumes its coordinates. While a Preview gesture is pinned, ordinary scene changes wait; safety generation changes supersede it. No second Focus/animation state machine or WPF animation loop.

## Alternatives and limitations

Independent WPF Window over a native host is rejected: duplicated lifecycle/input ownership. Replacing Core with WPF events is rejected. Per-pixel transparent/Backdrop hosting is deferred in favor of opaque Solid with rectangular Region. Small dark corner backing outside rounded cards remains an engineering visual limitation.

WPF Submit/UpdateLayout does not prove actual screen presentation. Submitted-scene/Region/hit consistency is tested; compositor lag can still expose a previous picture. This remains UNVERIFIED and blocks production acceptance until attended Windows 11 testing traces presented frames and physical press/reversal targets. No permanent ban on animation clicks is used.

## Consequences and next gate

See [UI-I1 evidence](../UI_I1_WPF_EVIDENCE.md). Keyboard/IME, UIA, true search, virtualization, editing and saved configuration remain separate. UI-I2 should first resolve presentation/input synchronization and explicit keyboard activation, using existing KeyboardMode and IME interaction flags. No global keyboard hook or automatic activation is introduced. All historical ADRs remain unchanged and Proposed; this proposal does not close desktop or performance gates.
