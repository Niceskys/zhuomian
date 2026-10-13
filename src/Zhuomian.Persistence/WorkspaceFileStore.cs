using System.ComponentModel;
using System.Runtime.CompilerServices;
using Zhuomian.Core.WorkspaceModel;

[assembly: InternalsVisibleTo("Zhuomian.Persistence.Tests")]
namespace Zhuomian.Persistence;

/// <summary>Synchronous bounded transactions. OS lock serializes cooperating processes; diagnostics contain enum codes only.</summary>
public sealed partial class WorkspaceFileStore : IWorkspaceStore
{
    private const string MainFile = "workspace.json";
    private readonly PhysicalStorage _storage;
    private readonly IStorageDiagnostics? _diagnostics;
    private readonly object _gate = new();

    private WorkspaceFileStore(string root, IStorageDiagnostics? diagnostics, IStorageFaultInjector? faults)
    { _storage = new(root, faults); _diagnostics = diagnostics; }

    public static WorkspaceFileStore CreateDefault(IStorageDiagnostics? diagnostics = null)
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException("Production storage requires Windows NTFS."); }
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(appData)) { throw new InvalidOperationException("User storage unavailable."); }
        return new(Path.Combine(appData, "Zhuomian", "Workspace"), diagnostics, null);
    }

    // Only tests can supply a path/fault injector; every test owns a private temporary root.
    internal static WorkspaceFileStore ForTests(string ownedRoot, IStorageFaultInjector? faults = null, IStorageDiagnostics? diagnostics = null)
    {
        string full = Path.GetFullPath(ownedRoot);
        string temp = Path.GetFullPath(Path.GetTempPath());
        if (!full.StartsWith(temp, StringComparison.Ordinal) || !Path.GetFileName(full).StartsWith("zhuomian-persistence-", StringComparison.Ordinal))
        { throw new ArgumentException("Fault injection requires an owned test root."); }
        return new(ownedRoot, diagnostics, faults);
    }

    internal StorageReadMetrics ReadMetrics { get { lock (_gate) { return _storage.Metrics; } } }

    public RecoveryResult Load() => RunRecovery(StorageOperation.Load, () => Recover(restore: false));
    public RecoveryResult Restore() => RunRecovery(StorageOperation.Restore, () => Recover(restore: true));

    private RecoveryResult RunRecovery(StorageOperation operation, Func<RecoveryResult> action)
    {
        lock (_gate)
        {
            try
            {
                using var root = _storage.PinRoot();
                using var mutex = AcquireLock();
                var result = action();
                Report(operation, result.Error); return result;
            }
            catch (Exception e) when (IsStorageFailure(e))
            { var error = Error(e); Report(operation, error); return new(RecoveryState.Unrecoverable, error, readOnly: true); }
        }
    }

    public StorageResult Save(Workspace snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return RunWrite(StorageOperation.Save, () =>
        {
            byte[] next = WorkspaceCodec.Encode(snapshot);
            var reservation = ReadHistory(snapshot.Id, snapshot.Revision, next);
            if (_storage.Exists(MainFile))
            {
                byte[] currentBytes = _storage.Read(MainFile);
                var current = WorkspaceCodec.Decode(currentBytes);
                if (current.Workspace is null) { return new(StorageError.RecoveryRequired); }
                if (current.IsReadOnly) { return new(StorageError.ReadOnly); }
                if (current.Workspace.Id != snapshot.Id) { return new(StorageError.WorkspaceConflict); }
                if (reservation.Error != StorageError.None) { return new(reservation.Error); }
                if (snapshot.Revision < current.Workspace.Revision) { return new(StorageError.StaleRevision); }
                if (snapshot.Revision == current.Workspace.Revision)
                {
                    // Canonical DTO content comparison permits harmless JSON formatting differences.
                    return next.AsSpan().SequenceEqual(WorkspaceCodec.Encode(current.Workspace)) ?
                        new(StorageError.None, CommitState.Unchanged, snapshot.Revision) : new(StorageError.RevisionConflict);
                }
                _storage.Archive(currentBytes);
            }
            else
            {
                if (reservation.Error != StorageError.None) { return new(reservation.Error); }
                // A missing main with retained backups is recovery, never a fresh workspace.
                foreach (string name in _storage.Backups())
                {
                    var backup = ReadCandidate(name);
                    if (!reservation.Match || backup.Workspace is null || backup.IsReadOnly ||
                        !WorkspaceCodec.Encode(backup.Workspace).AsSpan().SequenceEqual(next))
                    { return new(StorageError.RecoveryRequired); }
                }
            }
            // Durable conservative revision reservation: failures may burn a revision but can never permit regression.
            if (!reservation.Match) { _storage.Archive(next, "prepared"); }
            bool replace = _storage.Exists(MainFile);
            // First creation has no predecessor: retain the validated initial candidate before publishing main.
            if (!replace && _storage.Backups().Length == 0) { _storage.Archive(next); }
            string temp = _storage.WriteTemp(next);
            try { _storage.Publish(temp, MainFile, replace); }
            catch (Exception e) when (IsStorageFailure(e))
            {
                // OS submission can succeed then report an error. Check the actual committed bytes under the lock.
                try
                {
                    if (_storage.Exists(MainFile) && _storage.Read(MainFile).AsSpan().SequenceEqual(next))
                    { return new(StorageError.None, CommitState.Committed, snapshot.Revision); }
                }
                catch (Exception verification) when (IsStorageFailure(verification))
                {
                    // A failed readback after OS submission cannot prove that nothing committed.
                    return new(Error(verification), CommitState.Unknown, snapshot.Revision);
                }
                throw;
            }
            return new(StorageError.None, CommitState.Committed, snapshot.Revision);
        });
    }

    public StorageResult Backup() => RunWrite(StorageOperation.Backup, () =>
    {
        if (!_storage.Exists(MainFile)) { return new(StorageError.NotFound); }
        byte[] bytes = _storage.Read(MainFile);
        var result = WorkspaceCodec.Decode(bytes);
        if (result.Workspace is null || result.IsReadOnly) { return new(result.Error == StorageError.None ? StorageError.ReadOnly : result.Error); }
        var history = ReadHistory(result.Workspace.Id);
        if (history.Error != StorageError.None) { return new(history.Error); }
        _storage.Archive(bytes);
        return new(StorageError.None, CommitState.Committed, result.Workspace.Revision);
    });

    private RecoveryResult Recover(bool restore)
    {
        bool exists = _storage.Exists(MainFile);
        RecoveryResult primary = exists ? ReadCandidate(MainFile) : new(RecoveryState.NotFound, StorageError.NotFound);
        if (!restore && primary.Workspace is not null)
        {
            if (primary.IsReadOnly) { return WithEvidence(primary, MainFile); }
            // A readable main remains available even if history cannot authorize mutation.
            // Never turn incomplete evidence into a writable, healthy result.
            (StorageError Error, long Revision, bool Match) history;
            try { history = ReadHistory(primary.Workspace.Id); }
            catch (Exception e) when (IsStorageFailure(e)) { history = (Error(e), 0, false); }
            return history.Error == StorageError.None ? WithEvidence(primary, MainFile) :
                new(primary.State, history.Error, primary.Workspace, primary.Quarantine, MainFile, readOnly: true);
        }
        // A future schema must never be downgraded by automatic recovery or restore.
        if (primary.Error is StorageError.UnsupportedVersion or StorageError.UnsafePath or StorageError.AccessDenied or StorageError.IoFailure)
        { return WithEvidence(primary, MainFile); }
        RecoveryResult? best = null;
        string? bestName = null;
        var contents = new HistoryCommitments();
        if (primary.Workspace is not null && !primary.IsReadOnly) { contents.Observe(primary.Workspace); }
        foreach (string name in _storage.Backups())
        {
            var candidate = ReadCandidate(name);
            if (candidate.Workspace is null || candidate.IsReadOnly) { continue; }
            var conflict = contents.Observe(candidate.Workspace);
            if (conflict != StorageError.None)
            { return new(RecoveryState.Unrecoverable, conflict, readOnly: true); }
            if (best is null || candidate.Workspace.Revision > best.Workspace!.Revision)
            { best = candidate; bestName = name; }
        }
        if (best?.Workspace is null)
        {
            if (primary.Workspace is not null) { return WithEvidence(primary, MainFile); }
            return new(exists ? RecoveryState.Unrecoverable : RecoveryState.NotFound, primary.Error, evidenceId: exists ? MainFile : null, readOnly: true);
        }
        var workspace = best.Workspace;
        if (restore)
        {
            if (primary.IsReadOnly && primary.Workspace is not null) { return new(RecoveryState.PartiallyQuarantined, StorageError.ReadOnly, primary.Workspace, primary.Quarantine, MainFile, readOnly: true); }
            var reserved = ReadHistory(workspace.Id);
            if (reserved.Error != StorageError.None) { return new(RecoveryState.Unrecoverable, reserved.Error, readOnly: true); }
            long floor = Math.Max(reserved.Revision, Math.Max(workspace.Revision, primary.Workspace?.Revision ?? 0));
            if (floor == long.MaxValue) { return new(RecoveryState.Unrecoverable, StorageError.RevisionOverflow, evidenceId: bestName, readOnly: true); }
            workspace = Workspace.FromSnapshot(workspace.Id, workspace.SchemaVersion, floor + 1,
                workspace.ItemStore, workspace.Spaces.Values, workspace.RemovedSpaces.Values);
            byte[] next = WorkspaceCodec.Encode(workspace);
            _storage.Archive(next, "prepared");
            string temp = _storage.WriteTemp(next);
            // Atomic replacement preserves the exact damaged source in unique evidence, never in a good backup.
            try
            {
                _storage.Publish(temp, MainFile, replace: exists,
                    evidence: exists ? "evidence-" + Guid.NewGuid().ToString("N") + ".json" : null);
            }
            catch (Exception e) when (IsStorageFailure(e))
            {
                if (!_storage.Exists(MainFile) || !_storage.Read(MainFile).AsSpan().SequenceEqual(next)) { throw; }
            }
        }
        var readError = restore ? StorageError.None : ReadHistory(workspace.Id).Error;
        return new(RecoveryState.BackupRecovered, readError, workspace, evidenceId: bestName,
            primaryError: primary.Error, readOnly: !restore);
    }

    private (StorageError Error, long Revision, bool Match) ReadHistory(Guid workspaceId, long? revision = null, byte[]? next = null)
    {
        var history = new HistoryCommitments();
        if (_storage.Exists(MainFile))
        {
            var main = ReadCandidate(MainFile);
            if (main.Workspace is not null && !main.IsReadOnly) { history.Observe(main.Workspace); }
        }
        // Both families bind revision content. Checking only the requested revision would
        // silently accept contradictory earlier records; a scalar maximum cannot fix that.
        foreach (string prefix in new[] { "backup", "prepared" })
        {
            foreach (string name in _storage.Backups(prefix))
            {
                var candidate = ReadCandidate(name);
                if (candidate.Workspace is null || candidate.IsReadOnly)
                { return (StorageError.RecoveryRequired, history.MaximumRevision, false); }
                var error = history.Observe(candidate.Workspace);
                if (error != StorageError.None) { return (error, history.MaximumRevision, false); }
            }
        }
        return history.Check(workspaceId, revision, next);
    }

    private RecoveryResult ReadCandidate(string name)
    {
        try { return WorkspaceCodec.Decode(_storage.Read(name)); }
        catch (Exception e) when (IsStorageFailure(e))
        { return new(RecoveryState.Unrecoverable, Error(e), readOnly: true); }
    }
    private static RecoveryResult WithEvidence(RecoveryResult result, string name) => new(result.State, result.Error,
        result.Workspace, result.Quarantine, name, result.PrimaryError, result.IsReadOnly);

    private StorageResult RunWrite(StorageOperation operation, Func<StorageResult> action)
    {
        lock (_gate)
        {
            try
            {
                using var root = _storage.PinRoot();
                using var mutex = AcquireLock();
                var result = action(); Report(operation, result.Error); return result;
            }
            catch (Exception e) when (IsStorageFailure(e))
            { var error = Error(e); Report(operation, error); return new(error); }
        }
    }
    private FileStream AcquireLock()
    {
        try { return _storage.Lock(); }
        catch (IOException) { throw new StorageException(StorageError.Busy); }
        catch (Win32Exception e) when (e.NativeErrorCode is 32 or 33) { throw new StorageException(StorageError.Busy); }
    }
    private void Report(StorageOperation operation, StorageError error)
    {
        // Diagnostic sinks are observational: a broken sink cannot change a disk transaction outcome.
        try { _diagnostics?.Report(new(operation, error)); }
        catch (Exception) { }
    }
    private static bool IsStorageFailure(Exception e) => e is StorageException or IOException or UnauthorizedAccessException or Win32Exception;
    private static StorageError Error(Exception e) => e switch
    {
        StorageException storage => storage.Error,
        UnauthorizedAccessException => StorageError.AccessDenied,
        FileNotFoundException => StorageError.NotFound,
        Win32Exception native when native.NativeErrorCode is 5 => StorageError.AccessDenied,
        Win32Exception native when native.NativeErrorCode is 2 or 3 => StorageError.NotFound,
        Win32Exception native when native.NativeErrorCode is 112 or 39 => StorageError.DiskFull,
        IOException io when (io.HResult & 0xFFFF) is 112 or 39 => StorageError.DiskFull,
        _ => StorageError.IoFailure,
    };
}
