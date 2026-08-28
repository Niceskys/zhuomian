using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.FallbackVisualUsability;

internal static class InputInjector
{
    private const uint InputMouse = 0;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    internal static void Click(Point point)
    {
        if (!NativeMethods.SetCursorPos(point.X, point.Y))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetCursorPos failed.");
        }

        Send(
            Mouse(MouseLeftDown),
            Mouse(MouseLeftUp));
    }

    private static NativeMethods.Input Mouse(uint flags) => new()
    {
        Type = InputMouse,
        Data = new NativeMethods.InputUnion
        {
            Mouse = new NativeMethods.MouseInput { Flags = flags },
        },
    };

    private static void Send(params NativeMethods.Input[] inputs)
    {
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput failed.");
        }
    }
}
