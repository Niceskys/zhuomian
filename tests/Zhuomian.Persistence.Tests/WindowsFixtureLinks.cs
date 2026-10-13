using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Zhuomian.Persistence.Tests;

/// <summary>Fixture setup only. Never used by production persistence code and never touches user resources.</summary>
[SupportedOSPlatform("windows")]
internal static partial class WindowsFixtureLinks
{
    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, uint inputSize, nint output, uint outputSize, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string newName, string existingName, nint security);

    internal static void HardLink(string path, string target)
    {
        if (!CreateHardLink(path, target, 0)) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    }

    internal static void RemoveJunction(string path)
    {
        // Undo exactly the owned fixture tag before normal directory removal. Do not traverse the target.
        byte[] buffer = new byte[8];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        using (var handle = CreateFile(path, 0x40000000, 7, 0, 3, 0x02200000, 0))
        {
            if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900AC, buffer, (uint)buffer.Length, 0, 0, out _, 0))
            { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        }
        Directory.Delete(path);
    }

    internal static void Junction(string path, string target)
    {
        Directory.CreateDirectory(path);
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        byte[] print = Encoding.Unicode.GetBytes(target);
        byte[] buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        using (var writer = new BinaryWriter(new MemoryStream(buffer)))
        {
            writer.Write(0xA0000003u); // IO_REPARSE_TAG_MOUNT_POINT
            writer.Write(checked((ushort)(buffer.Length - 8)));
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(checked((ushort)substitute.Length));
            writer.Write(checked((ushort)(substitute.Length + 2)));
            writer.Write(checked((ushort)print.Length));
            writer.Write(substitute);
            writer.Write((ushort)0);
            writer.Write(print);
            writer.Write((ushort)0);
        }
        using var handle = CreateFile(path, 0x40000000, 7, 0, 3, 0x02200000, 0);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, buffer, (uint)buffer.Length, 0, 0, out _, 0))
        { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
    }
}
