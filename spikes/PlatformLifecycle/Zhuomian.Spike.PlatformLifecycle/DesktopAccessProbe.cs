using System.Runtime.InteropServices;

namespace Zhuomian.Spike.PlatformLifecycle;

internal sealed record DesktopAccessEvidence(bool Accessible, string? Name, int ErrorCode);

internal static class DesktopAccessProbe
{
    private const uint DesktopReadObjects = 0x0001;
    private const uint DesktopSwitchDesktop = 0x0100;
    private const int UoiName = 2;

    internal static DesktopAccessEvidence Capture()
    {
        var desktop = NativeMethods.OpenInputDesktop(
            0,
            false,
            DesktopReadObjects | DesktopSwitchDesktop);
        if (desktop == 0)
        {
            return new(false, null, Marshal.GetLastWin32Error());
        }

        try
        {
            NativeMethods.GetUserObjectInformationW(desktop, UoiName, null, 0, out var needed);
            if (needed == 0)
            {
                return new(true, null, Marshal.GetLastWin32Error());
            }

            var buffer = new char[(needed / sizeof(char)) + 1];
            if (!NativeMethods.GetUserObjectInformationW(
                desktop,
                UoiName,
                buffer,
                (uint)(buffer.Length * sizeof(char)),
                out _))
            {
                return new(true, null, Marshal.GetLastWin32Error());
            }

            return new(true, new string(buffer).TrimEnd('\0'), 0);
        }
        finally
        {
            NativeMethods.CloseDesktop(desktop);
        }
    }
}
