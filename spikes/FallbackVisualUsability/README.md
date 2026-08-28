# Fallback visual usability Spike

## Question

Can a public-Win32 fallback be visibly useful on the exposed desktop, accept a one-click Preview Item action without activating Zhuomian, allow transparent canvas pixels to pass through, and remain covered by an ordinary application window?

This is stricter than the earlier style probe. A valid HWND is not enough: controlled pixels must be present in an actual screen capture and a physical click must reach the intended Item exactly once.

## Candidate strategy

The disposable candidate is a per-monitor, borderless, transparent, `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` canvas. It is raised only while the desktop is exposed. Normal application windows remain above it. Opaque Space rectangles receive pointer input; color-key transparent pixels pass through.

This is managed ordinary-window Z-order, not a documented Shell wallpaper/icon band. It does not use WorkerW, `SetParent`, TopMost or a global input hook.

## Pass criteria

- controlled Space pixels are visible in a real screen capture;
- an ordinary non-TopMost window covers the overlapped Space region;
- one physical click on a visible Preview Item executes its simulated action exactly once;
- that click preserves the ordinary window as foreground;
- a transparent canvas point does not resolve to the Zhuomian host;
- the host is borderless, NoActivate and non-TopMost;
- cleanup destroys the host and restores the prior desktop/window view and pointer.

## Run

The probe briefly shows the desktop, moves the pointer, creates controlled surfaces, captures privacy-safe evidence, and then restores the previous window view and pointer.

```powershell
pwsh ./spikes/FallbackVisualUsability/run-fallback-visual-usability-probe.ps1 -Count 5
```

The committed PNG contains only crops of probe-owned solid surfaces. It intentionally excludes wallpaper, desktop icons, filenames and other personal desktop content.

## Current result

On Windows 11 build 26100, x64, with one 2560 x 1600 display at 150% scaling, the probe passed **5/5** runs:

- controlled Space pixels were visible on the exposed real desktop;
- transparent canvas pixels passed through;
- a physical Preview Item click executed exactly once and preserved the Shell foreground;
- a normal non-TopMost window covered the overlapped Space region;
- the host remained borderless, NoActivate and non-TopMost;
- cleanup restored the prior window view and pointer and left no host window.

See [the structured evidence](evidence/windows-11-26100-fallback-visual-usability-summary.json) and [the privacy-safe controlled-surface proof](evidence/windows-11-26100-fallback-visual-usability.png).

## Product boundary

A pass establishes conditional fallback usability, not an invisible relationship with desktop icons. Opaque Space pixels can cover desktop icons if they overlap. Product code must therefore reserve Space rectangles, detect or communicate overlap, and never claim that the public fallback is guaranteed below icons.

If the host is not visible, does not pass transparent hit testing, or cannot remain below ordinary application windows, this fallback presentation must be rejected rather than shipped as a decorative demo.
