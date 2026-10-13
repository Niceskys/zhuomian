using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using Zhuomian.Core.Launching;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Launching.Windows;

/// <summary>Read-only handles pin every directory and leaf against deletion/rename. Leaf also denies writers.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsResourceLease : ILaunchLease
{
    private readonly List<SafeFileHandle> _parents = [];
    private readonly SafeFileHandle _leaf;
    private readonly string? _expectedDigest;
    private readonly CancellationToken _cancellationToken;
    private bool _disposed;

    internal WindowsResourceLease(WindowsPath path, bool directory, string? expectedIdentity, string? expectedDigest,
        LaunchTargetKind kind, ApplicationIdentity? app = null, ApplicationProvenance? provenance = null, CancellationToken cancellationToken = default)
    {
        _expectedDigest = expectedDigest;
        _cancellationToken = cancellationToken;
        try
        {
            string root = path.Value[..3];
            var drive = new DriveInfo(root);
            if (drive.DriveType != DriveType.Fixed || !drive.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) { throw new UnsafeResourceException(); }
            _parents.Add(Open(root, directory: true));
            string[] segments = path.Value[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0) { throw new UnsafeResourceException(); }
            string current = root.TrimEnd('\\');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current += "\\" + segments[i];
                _parents.Add(Open(current, directory: true));
            }
            _leaf = Open(path.Value, directory);
            if (expectedDigest is not null && HasZoneIdentifier(path.Value)) { throw new UnsafeResourceException(); }
            var info = Information(_leaf);
            string identity = Identity(info);
            if (expectedIdentity is not null && expectedIdentity != identity) { throw new ReplacedResourceException(); }
            if (expectedDigest is not null && !DigestMatches(_leaf, expectedDigest, cancellationToken)) { throw new ReplacedResourceException(); }
            Target = new(kind, path.Value, identity, app, provenance);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ResolvedLaunchTarget Target { get; }

    private static SafeFileHandle Open(string path, bool directory)
    {
        // OPEN_REPARSE_POINT inspects the link itself, never follows it. No FILE_SHARE_WRITE or FILE_SHARE_DELETE.
        var handle = NativeFiles.CreateFile(path, directory ? 0u : 0x80000000u,
            1u, 0, 3, 0x00200000u | 0x02000000u, 0);
        try
        {
            if (handle.IsInvalid) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
            var info = Information(handle);
            var finalPath = new char[512];
            uint length = NativeFiles.GetFinalPathNameByHandle(handle, finalPath, (uint)finalPath.Length, 0);
            if (length == 0 || length >= finalPath.Length ||
                !new string(finalPath, 0, (int)length).Equals("\\\\?\\" + path, StringComparison.OrdinalIgnoreCase))
            { throw new UnsafeResourceException(); }
            if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                ((info.Attributes & (uint)FileAttributes.Directory) != 0) != directory ||
                (!directory && info.Links != 1)) { throw new UnsafeResourceException(); }
            return handle;
        }
        catch { handle.Dispose(); throw; }
    }

    private static bool HasZoneIdentifier(string path)
    {
        // Direct CreateProcess must not become an alternate path for marked downloads/SmartScreen.
        // Refuse any MOTW stream; never read, clear or reinterpret its zone value.
        using var stream = NativeFiles.CreateFile(path + ":Zone.Identifier", 0, 1, 0, 3, 0x00200000, 0);
        if (!stream.IsInvalid) { return true; }
        int error = Marshal.GetLastPInvokeError();
        return error == 2 ? false : throw new Win32Exception(error);
    }

    private static NativeFiles.FileInformation Information(SafeFileHandle handle) =>
        NativeFiles.GetFileInformationByHandle(handle, out var info) ? info : throw new Win32Exception(Marshal.GetLastPInvokeError());

    private static string Identity(NativeFiles.FileInformation info) => FormattableString.Invariant($"{info.VolumeSerial:X8}:{info.IndexHigh:X8}{info.IndexLow:X8}");

    private static bool DigestMatches(SafeFileHandle handle, string expected, CancellationToken cancellationToken)
    {
        // Duplicate managed ownership is avoided: RandomAccess reads leave the handle owned by this lease.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[65536];
        long offset = 0;
        int count;
        while ((count = RandomAccess.Read(handle, buffer, offset)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
            offset += count;
            if (offset > 128 * 1024 * 1024) { throw new UnsafeResourceException(); }
        }
        return Convert.ToHexString(hash.GetHashAndReset()) == expected;
    }

    public LaunchResult Revalidate()
    {
        if (_disposed) { return new(LaunchStatus.Denied, LaunchReason.ResourceUnavailable); }
        try
        {
            foreach (var parent in _parents)
            {
                if ((Information(parent).Attributes & (uint)FileAttributes.ReparsePoint) != 0) { return new(LaunchStatus.Denied, LaunchReason.UnsafeTarget); }
            }
            if (_expectedDigest is not null && HasZoneIdentifier(Target.Target))
            { return new(LaunchStatus.Denied, LaunchReason.UnsafeTarget); }
            if (Identity(Information(_leaf)) != Target.ResourceIdentity ||
                (_expectedDigest is not null && !DigestMatches(_leaf, _expectedDigest, _cancellationToken)))
            { return new(LaunchStatus.Denied, LaunchReason.ResourceReplaced); }
            return new(LaunchStatus.Accepted);
        }
        catch (Win32Exception) { return new(LaunchStatus.MissingOrInaccessible, LaunchReason.ResourceUnavailable); }
    }

    public void Dispose()
    {
        if (_disposed) { return; }
        _disposed = true;
        _leaf?.Dispose();
        foreach (var parent in _parents) { parent.Dispose(); }
    }
}

internal sealed class UnsafeResourceException : Exception;
internal sealed class ReplacedResourceException : Exception;
