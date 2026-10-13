using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Zhuomian.Persistence;

public enum StorageStage { BeforeTempWrite, BeforeFlush, BeforeReplace, AfterReplace }
/// <summary>Trusted fault seam for owned test roots. Never selected by configuration.</summary>
public interface IStorageFaultInjector { void Check(StorageStage stage); }

internal sealed class PhysicalStorage(string root, IStorageFaultInjector? faults)
{
    internal string Root { get; } = ValidateRoot(root);
    private static string ValidateRoot(string root)
    {
        if (!Path.IsPathFullyQualified(root) || root.Contains("..", StringComparison.Ordinal) ||
            (OperatingSystem.IsWindows() && (root.StartsWith("\\\\", StringComparison.Ordinal) || root.Length > 240)))
        { throw new ArgumentException("Invalid controlled storage root."); }
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }
    internal string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['/', '\\', ':']) >= 0 || name is "." or "..")
        { throw new StorageException(StorageError.UnsafePath); }
        return Path.Combine(Root, name);
    }

    internal IDisposable PinRoot()
    {
        var pins = new DirectoryPins();
        try
        {
            string driveRoot = Path.GetPathRoot(Root)!;
            if (OperatingSystem.IsWindows())
            {
                var drive = new DriveInfo(driveRoot);
                if (drive.DriveType != DriveType.Fixed || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                { throw new StorageException(StorageError.UnsafePath); }
            }
            string current = driveRoot;
            pins.Add(current);
            foreach (string segment in Root[driveRoot.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current))
                {
                    if (File.Exists(current)) { throw new StorageException(StorageError.UnsafePath); }
                    Directory.CreateDirectory(current);
                }
                pins.Add(current);
            }
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }

    internal FileStream Lock()
    {
        string path = PathFor("workspace.lock");
        if (OperatingSystem.IsWindows())
        {
            return new(NativeStorage.Open(path, false, 0xC0000000, 0, 4), FileAccess.ReadWrite);
        }
        CheckLeaf(path);
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { CheckLeaf(path); return stream; }
        catch { stream.Dispose(); throw; }
    }

    internal byte[] Read(string name)
    {
        string path = PathFor(name);
        FileStream stream;
        if (OperatingSystem.IsWindows()) { stream = new(NativeStorage.Open(path, false, 0x80000000, 1, 3), FileAccess.Read); }
        else { CheckLeaf(path); stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read); }
        using (stream)
        {
            if (stream.Length > PersistenceLimits.MaxFileBytes) { throw new StorageException(StorageError.TooLarge); }
            using var memory = new MemoryStream();
            byte[] buffer = new byte[65536];
            int count;
            while ((count = stream.Read(buffer)) > 0)
            {
                if (memory.Length + count > PersistenceLimits.MaxFileBytes) { throw new StorageException(StorageError.TooLarge); }
                memory.Write(buffer, 0, count);
            }
            return memory.ToArray();
        }
    }

    internal bool Exists(string name)
    {
        string path = PathFor(name);
        try { CheckLeaf(path); return File.Exists(path); }
        catch (FileNotFoundException) { return false; }
    }

    internal string[] Backups(string prefix = "backup")
    {
        var names = new List<string>();
        long total = 0;
        foreach (string path in Directory.EnumerateFiles(Root, prefix + "-*.json"))
        {
            if (names.Count >= PersistenceLimits.MaxRecoveryFiles) { throw new StorageException(StorageError.TooLarge); }
            // Do not follow a reparse point even while estimating the recovery budget.
            CheckLeaf(path);
            total += new FileInfo(path).Length;
            if (total > PersistenceLimits.MaxRecoveryScanBytes) { throw new StorageException(StorageError.TooLarge); }
            names.Add(Path.GetFileName(path));
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    internal string WriteTemp(byte[] bytes)
    {
        string name = "temp-" + Guid.NewGuid().ToString("N") + ".json";
        string path = PathFor(name);
        faults?.Check(StorageStage.BeforeTempWrite);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            faults?.Check(StorageStage.BeforeFlush);
            stream.Flush(flushToDisk: true);
        }
        byte[] read = Read(name);
        var check = WorkspaceCodec.Decode(read);
        if (check.Error != StorageError.None || !read.AsSpan().SequenceEqual(bytes)) { throw new StorageException(StorageError.IntegrityFailure); }
        return name;
    }

    internal void Publish(string temp, string destination, bool replace, string? evidence = null)
    {
        CheckLeaf(PathFor(destination));
        faults?.Check(StorageStage.BeforeReplace);
        CheckLeaf(PathFor(destination));
        if (replace) { File.Replace(PathFor(temp), PathFor(destination), evidence is null ? null : PathFor(evidence), ignoreMetadataErrors: false); }
        else { File.Move(PathFor(temp), PathFor(destination), overwrite: false); }
        faults?.Check(StorageStage.AfterReplace);
    }
    internal void Archive(byte[] bytes, string prefix = "backup")
    {
        if (Backups(prefix).Length >= PersistenceLimits.MaxRecoveryFiles) { throw new StorageException(StorageError.TooLarge); }
        string temp = WriteTemp(bytes);
        // Unique immutable archive; never replaces any prior backup or recovery evidence.
        File.Move(PathFor(temp), PathFor(prefix + "-" + Guid.NewGuid().ToString("N") + ".json"), overwrite: false);
    }
    private static void CheckLeaf(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) { throw new StorageException(StorageError.UnsafePath); }
        }
        catch (FileNotFoundException) { }
    }

    private sealed class DirectoryPins : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];
        internal void Add(string directory)
        {
            if (OperatingSystem.IsWindows()) { _handles.Add(NativeStorage.Open(directory, true, 0, 1, 3)); }
            else if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { throw new StorageException(StorageError.UnsafePath); }
        }
        public void Dispose() { foreach (var handle in _handles) { handle.Dispose(); } }
    }
}

[SupportedOSPlatform("windows")]
internal static partial class NativeStorage
{
    internal static SafeFileHandle Open(string path, bool directory, uint access, uint share, uint disposition)
    {
        var handle = CreateFile(path, access, share, 0, disposition, 0x00200000 | 0x02000000, 0);
        try
        {
            if (handle.IsInvalid) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            if (!GetFileInformationByHandle(handle, out var info)) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            char[] final = new char[1024];
            uint length = GetFinalPathNameByHandle(handle, final, (uint)final.Length, 0);
            string expected = "\\\\?\\" + Path.TrimEndingDirectorySeparator(path);
            if (length == 0 || length >= final.Length || !Path.TrimEndingDirectorySeparator(new string(final, 0, (int)length)).Equals(expected, StringComparison.OrdinalIgnoreCase) ||
                (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                ((info.Attributes & (uint)FileAttributes.Directory) != 0) != directory || (!directory && info.Links != 1))
            { throw new StorageException(StorageError.UnsafePath); }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle file, [Out] char[] path, uint length, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal uint CreationLow;
        internal uint CreationHigh;
        internal uint AccessLow;
        internal uint AccessHigh;
        internal uint WriteLow;
        internal uint WriteHigh;
        internal uint VolumeSerial;
        internal uint SizeHigh;
        internal uint SizeLow;
        internal uint Links;
        internal uint IndexHigh;
        internal uint IndexLow;
    }
}
