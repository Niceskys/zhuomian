using System.Runtime.InteropServices;

namespace Zhuomian.Spike.ForegroundClassification;

internal static class PhysicalClick
{
    private const uint InputMouse = 0;
    private const uint MouseEventLeftDown = 0x0002;
    private const uint MouseEventLeftUp = 0x0004;
    private static readonly int[] GuardedKeys = [0x01, 0x02, 0x10, 0x11, 0x12];

    public static bool GuardedKeysAreReleased() =>
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
        var sent = NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>());
        return sent == (uint)inputs.Length;
    }
}
