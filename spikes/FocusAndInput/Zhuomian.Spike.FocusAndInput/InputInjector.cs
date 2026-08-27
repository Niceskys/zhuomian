using System.Runtime.InteropServices;

namespace Zhuomian.Spike.FocusAndInput;

internal static class InputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const ushort VirtualKeyEscape = 0x1B;

    private static readonly int[] GuardedKeys = [0x01, 0x02, 0x10, 0x11, 0x12];

    public static bool AreGuardedKeysReleased() =>
        GuardedKeys.All(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) == 0);

    public static bool Click(Point screenPoint)
    {
        if (!NativeMethods.SetCursorPos(screenPoint.X, screenPoint.Y))
        {
            return false;
        }

        NativeMethods.Input[] inputs =
        [
            new()
            {
                Type = InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MouseInput { Flags = MouseEventLeftDown },
                },
            },
            new()
            {
                Type = InputMouse,
                Data = new NativeMethods.InputUnion
                {
                    Mouse = new NativeMethods.MouseInput { Flags = MouseEventLeftUp },
                },
            },
        ];

        return Send(inputs);
    }

    public static bool SendUnicode(string value)
    {
        var inputs = new List<NativeMethods.Input>(value.Length * 2);
        foreach (var character in value)
        {
            inputs.Add(CreateKeyboardInput(0, character, KeyEventUnicode));
            inputs.Add(CreateKeyboardInput(0, character, KeyEventUnicode | KeyEventKeyUp));
        }

        return Send([.. inputs]);
    }

    public static bool SendEscape()
    {
        NativeMethods.Input[] inputs =
        [
            CreateKeyboardInput(VirtualKeyEscape, '\0', 0),
            CreateKeyboardInput(VirtualKeyEscape, '\0', KeyEventKeyUp),
        ];

        return Send(inputs);
    }

    private static NativeMethods.Input CreateKeyboardInput(
        ushort virtualKey,
        char scanCode,
        uint flags) =>
        new()
        {
            Type = InputKeyboard,
            Data = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KeyboardInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                },
            },
        };

    private static bool Send(NativeMethods.Input[] inputs)
    {
        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());

        return sent == (uint)inputs.Length;
    }
}
