# Focus and input Spike

## Question

Can Zhuomian keep its visual desktop surface non-activating, then enter keyboard mode only after an explicit user-like click on a separate focusable surface?

This Spike tests the transition required by `NoKeyboardCapture -> ZhuomianKeyboardActive`. It does not select the final WinUI implementation.

## Safety contract

The probe aborts before text injection unless all of these are true:

- an external foreground window exists;
- no mouse button or Shift/Ctrl/Alt key is held;
- a physical click on the visual surface preserves the original foreground window;
- a physical click on the keyboard surface makes that surface foreground;
- its text box reports keyboard focus.

Only then does the probe inject the fixed token `ZHUOMIAN42`. It sends Escape, closes both surfaces, restores the original foreground window and pointer position, and checks that no probe window remains.

## Pass criteria

- showing and physically clicking the visual `WS_EX_NOACTIVATE` surface preserves foreground;
- showing the keyboard surface does not activate it by itself;
- an explicit physical click makes the keyboard surface foreground and focuses its text box;
- guarded Unicode input is received only by that text box;
- Escape is handled and closes keyboard mode;
- original foreground and pointer position are restored;
- all probe windows are destroyed.

Any safety abort or failed check makes the executable return exit code 1.

## Current result

On Windows 11 build 26100, x64, one monitor, the repeatability harness passed **5/5 runs**. See [the committed JSON evidence](evidence/windows-11-26100-focus-input-summary.json).

## Run

The pointer briefly moves during this interactive test. Do not type or hold mouse/modifier buttons while it runs.

```powershell
pwsh ./spikes/FocusAndInput/run-focus-input-probe.ps1 -Count 5
```

## Official API basis

- [WM_MOUSEACTIVATE](https://learn.microsoft.com/windows/win32/inputdev/wm-mouseactivate)
- [SetForegroundWindow](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow)
- [SendInput](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput)
- [INPUT](https://learn.microsoft.com/windows/win32/api/winuser/ns-winuser-input)

The explicit transition is driven by a user-like click. It does not use `SetForegroundWindow` to force entry into keyboard mode. `SetForegroundWindow` is used only to restore the original foreground after the probe owns foreground.

## Known limitations

- `SendInput` is subject to UIPI and was tested only at the current integrity level.
- The focusable surface is a disposable WinForms prototype, not the final WinUI surface.
- IME composition, Narrator, touch, high contrast and multiple monitors remain untested.
- This is host/input evidence, not production code.
