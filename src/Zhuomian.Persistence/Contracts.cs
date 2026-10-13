using System.Collections.Immutable;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence;

public enum StorageError
{
    None, NotFound, Busy, AccessDenied, IoFailure, DiskFull, UnsafePath, TooLarge,
    InvalidJson, IntegrityFailure, InvalidStructure, UnsupportedVersion, MigrationFailed,
    StaleRevision, RevisionConflict, WorkspaceConflict, ReadOnly, RecoveryRequired, RevisionOverflow,
    MaintenanceDisabled, AuthorizationRequired, StalePlan,
}

public enum RecoveryState { Normal, BackupRecovered, PartiallyQuarantined, Unrecoverable, UnsupportedVersion, NotFound }
public enum StorageOperation { Load, Save, Backup, Restore, Inventory, Plan, ValidatePlan, Apply, ExportPlan, ValidateExport, Export }
public enum CommitState { NotCommitted, Committed, Unchanged, Unknown }
public enum QuarantineReason { InvalidObject, DuplicateIdentity, UnsupportedKind, DanglingReference, UnknownField }

/// <summary>Allowlisted diagnostics only. No exception, path, name or arbitrary string payload.</summary>
public readonly record struct StorageDiagnostic(StorageOperation Operation, StorageError Error);
public interface IStorageDiagnostics { void Report(StorageDiagnostic diagnostic); }

/// <summary>Explicit private recovery evidence; never pass RawJson to logging or telemetry.</summary>
public sealed class QuarantinedObject(string collection, int index, QuarantineReason reason, string rawJson)
{
    public string Collection { get; } = collection;
    public int Index { get; } = index;
    public QuarantineReason Reason { get; } = reason;
    public string RawJson { get; } = rawJson;
    public override string ToString() => nameof(QuarantinedObject);
}

public sealed class RecoveryResult
{
    internal RecoveryResult(RecoveryState state, StorageError error, Workspace? workspace = null,
        ImmutableArray<QuarantinedObject> quarantine = default, string? evidenceId = null,
        StorageError primaryError = StorageError.None, bool readOnly = false)
    {
        State = state; Error = error; Workspace = workspace;
        Quarantine = quarantine.IsDefault ? [] : quarantine;
        EvidenceId = evidenceId; PrimaryError = primaryError; IsReadOnly = readOnly;
    }
    public RecoveryState State { get; }
    public StorageError Error { get; }
    public StorageError PrimaryError { get; }
    public Workspace? Workspace { get; }
    public ImmutableArray<QuarantinedObject> Quarantine { get; }
    /// <summary>Opaque owned evidence filename, never a personal path. Source bytes remain on disk.</summary>
    public string? EvidenceId { get; }
    public bool IsReadOnly { get; }
    public override string ToString() => $"{nameof(RecoveryResult)}({State}, {Error})";
}

public readonly record struct StorageResult(StorageError Error, CommitState Commit = CommitState.NotCommitted, long? Revision = null)
{
    public bool Succeeded => Error == StorageError.None;
}

/// <summary>Trusted composition dependency. Implementations must not derive a root from disk JSON.</summary>
public interface IWorkspaceStore
{
    RecoveryResult Load();
    StorageResult Save(Workspace snapshot);
    StorageResult Backup();
    RecoveryResult Restore();
}

/// <summary>Fixed parsing budgets, including quarantined/unknown data.</summary>
public static class PersistenceLimits
{
    public const int MaxFileBytes = 8 * 1024 * 1024;
    public const int MaxDepth = 32;
    public const int MaxStringLength = 32768;
    public const int MaxArrayElements = 20000;
    public const int MaxItems = 10000;
    public const int MaxTotalReferences = 100000;
    public const int MaxSpaces = 1024;
    public const int MaxProperties = 64;
    public const int MaxObjectBytes = 128 * 1024;
    public const int MaxRecoveryFiles = 256;
    public const int MaxRecoveryScanBytes = 64 * 1024 * 1024;
}

internal sealed class StorageException(StorageError error) : Exception
{
    internal StorageError Error { get; } = error;
}
