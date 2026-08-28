using System.Runtime.InteropServices;

namespace Zhuomian.Spike.PlatformLifecycle;

internal static class PhysicalClick
{
    private const uint InputMouse = 0;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private static readonly int[] GuardedKeys = [0x01, 0x02, 0x10, 0x11, 0x12];

    internal static bool GuardedKeysAreReleased() =>
        GuardedKeys.All(key => (NativeMethods.GetAsyncKeyState(key) & 0x8000) == 0);

    internal static bool Click(Point point)
    {
        if (!NativeMethods.SetCursorPos(point.X, point.Y))
        {
            return false;
        }

        NativeMethods.Input[] inputs =
        [
            Mouse(MouseLeftDown),
            Mouse(MouseLeftUp),
        ];
        return NativeMethods.SendInput(
            (uint)inputs.Length,
            inputs,
            Marshal.SizeOf<NativeMethods.Input>()) == inputs.Length;
    }

    private static NativeMethods.Input Mouse(uint flags) => new()
    {
        Type = InputMouse,
        Data = new NativeMethods.InputUnion
        {
            Mouse = new NativeMethods.MouseInput { Flags = flags },
        },
    };
}
