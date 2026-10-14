using System.ComponentModel;
using System.Security.Cryptography;
using Zhuomian.Core.WorkspaceModel;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Zhuomian.Persistence.V2Fixture")]
namespace Zhuomian.Persistence;

public enum V2Stage
{
    BeforeWrite, DuringWrite, BeforeFlush, BeforeReadback,
    BeforeReservation, AfterReservation, BeforeMain, AfterMain,
}
public interface IV2FaultInjector { void Check(V2Stage stage); }

/// <summary>Explicit independent engineering engine; never imports or falls back to v1.</summary>
public sealed class WorkspaceV2Store : IWorkspaceStore
{
    private const string State = "state-v2.bin";
    private const int Version = 2;
    private const int MaxSegments = 16384;
    private readonly PhysicalStorage _storage;
    private readonly IV2FaultInjector? _faults;
    private readonly object _gate = new();

    private WorkspaceV2Store(string root, IV2FaultInjector? faults)
    { _storage = new(root, null); _faults = faults; }

    public static WorkspaceV2Store CreateIndependentEngineeringStore()
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("Windows NTFS required."); }
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(local)) { throw new InvalidOperationException("User storage unavailable."); }
        return new(Path.Combine(local, "Zhuomian", "WorkspaceV2"), null);
    }

    internal static WorkspaceV2Store ForTests(string root, IV2FaultInjector? faults = null)
    {
        string full = Path.GetFullPath(root);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.Ordinal) ||
            !Path.GetFileName(full).StartsWith("zhuomian-persistence-", StringComparison.Ordinal))
        { throw new ArgumentException("Owned test root required."); }
        return new(full, faults);
    }

    /// <summary>Only this explicit request may create state; any existing data refuses initialization.</summary>
    public StorageResult InitializeNew(Workspace snapshot) => Run(true, () =>
    {
        if (Directory.EnumerateFileSystemEntries(_storage.Root).Any(p => Path.GetFileName(p) != "workspace.lock"))
        { return new(StorageError.RecoveryRequired); }
        return Commit(snapshot, null);
    });

    public StorageResult Save(Workspace snapshot) => Run(false, () => Commit(snapshot, Verify()));
    // Every selected snapshot already has an immutable recovery record. No duplicate file is needed.
    public StorageResult Backup() => Run(false, () =>
    {
        var verified = Verify();
        return verified.Current is null ? new(StorageError.RecoveryRequired) :
            new(StorageError.None, CommitState.Unchanged, verified.Current.Revision);
    });

    public RecoveryResult Load()
    {
        Workspace? workspace = null;
        var result = Run(false, () =>
        {
            workspace = Verify().Current;
            return workspace is null ? new(StorageError.RecoveryRequired) : new(StorageError.None);
        });
        return new(result.Succeeded ? RecoveryState.Normal : RecoveryState.Unrecoverable,
            result.Error, workspace, readOnly: !result.Succeeded);
    }

    /// <summary>Explicitly republish the last selected immutable snapshot above every reserved revision.</summary>
    public RecoveryResult Restore() => RestoreRevision(null);

    public RecoveryResult RestoreRevision(long? revision)
    {
        Workspace? restored = null;
        var result = Run(false, () =>
        {
            var verified = Verify(revision);
            var source = revision is null ? verified.Current : verified.Recovery;
            if (source is null) { return new(StorageError.RecoveryRequired); }
            if (verified.Latest.Revision == long.MaxValue) { return new(StorageError.RevisionOverflow); }
            restored = Workspace.FromSnapshot(source.Id, source.SchemaVersion, verified.Latest.Revision + 1,
                source.ItemStore, source.Spaces.Values, source.RemovedSpaces.Values);
            return Commit(restored, verified);
        });
        return new(result.Succeeded ? RecoveryState.BackupRecovered : RecoveryState.Unrecoverable,
            result.Error, result.Succeeded ? restored : null, readOnly: !result.Succeeded);
    }

    private StorageResult Commit(Workspace snapshot, Verified? verified)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        byte[] payload = WorkspaceCodec.Encode(snapshot);
        if (payload.Length > PersistenceLimits.MaxFileBytes - 128) { return new(StorageError.TooLarge); }
        Root? root = verified?.Root;
        if (verified is not null)
        {
            if (snapshot.Id != root!.Id) { return new(StorageError.WorkspaceConflict); }
            if (snapshot.Revision < verified.Latest.Revision) { return new(StorageError.StaleRevision); }
            if (snapshot.Revision == verified.Latest.Revision)
            {
                if (!payload.AsSpan().SequenceEqual(WorkspaceCodec.Encode(verified.Latest)))
                { return new(StorageError.RevisionConflict); }
                if (root.Main == root.Count) { return new(StorageError.None, CommitState.Unchanged, snapshot.Revision); }
            }
            else if (root.Count >= MaxSegments) { return new(StorageError.TooLarge); }
        }
        // A segment is a one-record bounded raw history page, linked by exact hash and ordinal.
        // Strictly increasing revisions make streaming conflict validation possible without a dictionary.
        if (verified is null || snapshot.Revision > verified.Latest.Revision)
        {
            long count = (root?.Count ?? 0) + 1;
            byte[] record = Pack(w =>
            {
                w.Write(Version); w.Write(snapshot.Id.ToByteArray()); w.Write(count);
                w.Write(root?.Head ?? new byte[32]); w.Write(root?.Main ?? 0);
                w.Write(payload.Length); w.Write(payload);
            });
            if ((verified?.HistoryBytes ?? 0) + record.Length > 512L * 1024 * 1024)
            { return new(StorageError.TooLarge); }
            string segment = Write(record);
            byte[] hash = SHA256.HashData(record);
            string name = SegmentName(hash);
            if (_storage.Exists(name))
            {
                // A pre-reservation crash may leave this exact immutable dependency.
                // Fresh native read validates identity and bytes; never overwrite a collision.
                if (!_storage.Read(name).AsSpan().SequenceEqual(record))
                { throw new StorageException(StorageError.IntegrityFailure); }
            }
            else { _storage.Publish(segment, name, false); }
            root = new(snapshot.Id, count, hash, root?.Main ?? 0);
            var reserved = PublishRoot(root, reservation: true, snapshot.Revision);
            if (!reserved.Succeeded) { return reserved; }
        }
        return PublishRoot(root! with { Main = root.Count }, reservation: false, snapshot.Revision);
    }

    private StorageResult PublishRoot(Root root, bool reservation, long revision)
    {
        bool submitted = false;
        try
        {
            byte[] bytes = EncodeRoot(root);
            string temp = Write(bytes);
            _faults?.Check(reservation ? V2Stage.BeforeReservation : V2Stage.BeforeMain);
            submitted = true;
            _storage.Publish(temp, State, _storage.Exists(State),
                "root-" + Guid.NewGuid().ToString("N") + ".bin");
            _faults?.Check(reservation ? V2Stage.AfterReservation : V2Stage.AfterMain);
            if (!_storage.Read(State).AsSpan().SequenceEqual(bytes)) { throw new StorageException(StorageError.IntegrityFailure); }
            return new(StorageError.None, reservation ? CommitState.NotCommitted : CommitState.Committed, revision);
        }
        catch (Exception e) when (Failure(e))
        {
            if (submitted)
            {
                try
                {
                    var actual = Verify();
                    if (actual.Root.Count == root.Count && actual.Root.Main == root.Main &&
                        actual.Root.Head.AsSpan().SequenceEqual(root.Head))
                    {
                        // Reservation accepted but main failed: caller may retry identical content only.
                        return reservation ? new(Error(e), CommitState.NotCommitted, revision) :
                            new(StorageError.None, CommitState.Committed, revision);
                    }
                    return new(Error(e), CommitState.NotCommitted, revision);
                }
                catch (Exception read) when (Failure(read)) { return new(Error(read), CommitState.Unknown, revision); }
            }
            return new(Error(e), CommitState.NotCommitted, revision);
        }
    }

    private Verified Verify(long? recoveryRevision = null)
    {
        if (!_storage.Exists(State)) { throw new StorageException(StorageError.RecoveryRequired); }
        using var reader = Reader(_storage.Read(State));
        CheckVersion(reader.ReadInt32());
        var root = new Root(new Guid(reader.ReadBytes(16)), reader.ReadInt64(), reader.ReadBytes(32), reader.ReadInt64());
        End(reader);
        if (root.Count is < 1 or > MaxSegments || root.Main < 0 || root.Main > root.Count)
        { throw new StorageException(StorageError.InvalidStructure); }
        byte[] head = root.Head;
        Workspace? latest = null, current = null, recovery = null;
        long newerRevision = 0;
        long selected = root.Main;
        long readBytes = 0;
        for (long ordinal = root.Count; ordinal >= 1; ordinal--)
        {
            _faults?.Check(V2Stage.BeforeReadback);
            byte[] bytes = _storage.Read(SegmentName(head));
            if (!SHA256.HashData(bytes).AsSpan().SequenceEqual(head)) { throw new StorageException(StorageError.IntegrityFailure); }
            readBytes += bytes.Length;
            if (readBytes > 512L * 1024 * 1024) { throw new StorageException(StorageError.TooLarge); }
            using var page = Reader(bytes);
            CheckVersion(page.ReadInt32());
            if (new Guid(page.ReadBytes(16)) != root.Id) { throw new StorageException(StorageError.WorkspaceConflict); }
            if (page.ReadInt64() != ordinal) { throw new StorageException(StorageError.IntegrityFailure); }
            head = page.ReadBytes(32);
            long previousMain = page.ReadInt64();
            int size = page.ReadInt32();
            if (size < 1 || size > PersistenceLimits.MaxFileBytes - 128) { throw new StorageException(StorageError.TooLarge); }
            var decoded = WorkspaceCodec.Decode(page.ReadBytes(size));
            End(page);
            if (decoded.Error != StorageError.None || decoded.Workspace is null || decoded.IsReadOnly)
            { throw new StorageException(decoded.Error == StorageError.None ? StorageError.ReadOnly : decoded.Error); }
            var workspace = decoded.Workspace;
            if (workspace.Id != root.Id) { throw new StorageException(StorageError.WorkspaceConflict); }
            if (ordinal < root.Count && workspace.Revision >= newerRevision)
            { throw new StorageException(StorageError.RevisionConflict); }
            if (previousMain < 0 || previousMain >= ordinal || (selected != ordinal && selected != previousMain))
            { throw new StorageException(StorageError.IntegrityFailure); }
            if (selected == ordinal && workspace.Revision == recoveryRevision) { recovery = workspace; }
            selected = previousMain;
            latest ??= workspace;
            if (ordinal == root.Main) { current = workspace; }
            newerRevision = workspace.Revision;
        }
        if (head.Any(b => b != 0) || selected != 0) { throw new StorageException(StorageError.IntegrityFailure); }
        return new(root, latest!, current, recovery, readBytes);
    }

    private string Write(byte[] bytes)
    {
        if (bytes.Length > PersistenceLimits.MaxFileBytes) { throw new StorageException(StorageError.TooLarge); }
        _faults?.Check(V2Stage.BeforeWrite);
        string name = "stage-" + Guid.NewGuid().ToString("N") + ".bin";
        string path = _storage.PathFor(name);
        using (var stream = OperatingSystem.IsWindows() ?
            new FileStream(NativeStorage.Open(path, false, 0x40000000, 0, 1), FileAccess.Write) :
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            int half = bytes.Length / 2;
            stream.Write(bytes.AsSpan(0, half)); _faults?.Check(V2Stage.DuringWrite);
            stream.Write(bytes.AsSpan(half)); _faults?.Check(V2Stage.BeforeFlush);
            stream.Flush(true);
        }
        _faults?.Check(V2Stage.BeforeReadback);
        if (!_storage.Read(name).AsSpan().SequenceEqual(bytes)) { throw new StorageException(StorageError.IntegrityFailure); }
        return name;
    }

    private StorageResult Run(bool create, Func<StorageResult> action)
    {
        lock (_gate)
        {
            try
            {
                using var pin = _storage.PinRoot(create);
                using var mutex = AcquireLock();
                return action();
            }
            catch (Exception e) when (Failure(e)) { return new(Error(e)); }
        }
    }

    private FileStream AcquireLock()
    {
        try { return _storage.Lock(); }
        catch (IOException) { throw new StorageException(StorageError.Busy); }
        catch (Win32Exception e) when (e.NativeErrorCode is 32 or 33) { throw new StorageException(StorageError.Busy); }
    }

    private static string SegmentName(byte[] digest) => "segment-" + Convert.ToHexString(digest) + ".bin";
    private static byte[] EncodeRoot(Root root) => Pack(w =>
    { w.Write(Version); w.Write(root.Id.ToByteArray()); w.Write(root.Count); w.Write(root.Head); w.Write(root.Main); });
    private static byte[] Pack(Action<BinaryWriter> write)
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) { write(writer); }
        byte[] body = memory.ToArray();
        memory.Write(SHA256.HashData(body));
        return memory.ToArray();
    }
    private static BinaryReader Reader(byte[] bytes)
    {
        if (bytes.Length < 36 || !SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).AsSpan().SequenceEqual(bytes.AsSpan(bytes.Length - 32)))
        { throw new StorageException(StorageError.IntegrityFailure); }
        return new(new MemoryStream(bytes, 0, bytes.Length - 32, writable: false));
    }
    private static void End(BinaryReader reader)
    { if (reader.BaseStream.Position != reader.BaseStream.Length) { throw new StorageException(StorageError.InvalidStructure); } }
    private static void CheckVersion(int version)
    { if (version != Version) { throw new StorageException(StorageError.UnsupportedVersion); } }
    private static bool Failure(Exception e) => e is StorageException or IOException or UnauthorizedAccessException or Win32Exception or ArgumentException;
    private static StorageError Error(Exception e) => e switch
    {
        StorageException storage => storage.Error,
        UnauthorizedAccessException => StorageError.AccessDenied,
        Win32Exception native when native.NativeErrorCode is 32 or 33 => StorageError.Busy,
        Win32Exception native when native.NativeErrorCode == 5 => StorageError.AccessDenied,
        IOException io when (io.HResult & 0xFFFF) is 32 or 33 => StorageError.Busy,
        IOException io when (io.HResult & 0xFFFF) is 112 or 39 => StorageError.DiskFull,
        ArgumentException => StorageError.InvalidStructure,
        _ => StorageError.IoFailure,
    };
    private sealed record Root(Guid Id, long Count, byte[] Head, long Main);
    private sealed record Verified(Root Root, Workspace Latest, Workspace? Current, Workspace? Recovery, long HistoryBytes);
}
