using System.Runtime.InteropServices;

namespace Zhuomian.Spike.MultiMonitorDpi;

internal static class NativeMethods
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal delegate bool MonitorEnumeration(nint monitor, nint deviceContext, nint monitorRect, nint data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClassEx
    {
        internal uint Size;
        internal uint Style;
        internal nint WindowProcedure;
        internal int ClassExtra;
        internal int WindowExtra;
        internal nint Instance;
        internal nint Icon;
        internal nint Cursor;
        internal nint BackgroundBrush;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string? MenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        internal string ClassName;
        internal nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        internal uint Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DisplayDevice
    {
        internal uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceString;
        internal uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        internal string DeviceKey;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    internal static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(nint dpiContext);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetThreadDpiAwarenessContext();

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern int GetAwarenessFromDpiAwarenessContext(nint dpiContext);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AreDpiAwarenessContextsEqual(nint first, nint second);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern ushort RegisterClassExW(ref WindowClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterClassW(string className, nint instance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint DefWindowProcW(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        nint deviceContext,
        nint clipRect,
        MonitorEnumeration enumeration,
        nint data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfoW(nint monitor, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayDevicesW(
        string device,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        nint window,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    internal static extern nint GetWindowDpiAwarenessContext(nint window);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern nint GetWindowLongPtrW(nint window, int index);

    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
    internal static extern nint CreateSolidBrush(uint color);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint graphicsObject);
}
