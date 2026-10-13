using System.Collections.Immutable;

namespace Zhuomian.Persistence;

public enum StorageFileKind { Main, Backup, Prepared, Temp, Evidence, Lock, Unknown }
public enum StorageValidation { ValidSnapshot, ReadOnlySnapshot, UnsupportedVersion, Corrupt, Unattributed, Incomplete, CoordinationLock }
public enum RetentionReason { AuthoritativeMain, RecoveryGeneration, RevisionReservation, UnfinishedOperation, UnconfirmedEvidence, CoordinationLock, UnknownOwnership, ValidationFailed }
public enum StorageRisk { Healthy, ProtectedHistory, IncompleteOrUnsafe }
public enum QuotaPressure { Normal, ApproachingLimit, AtLimit, Exceeded, Unknown }

/// <summary>Opaque per-inventory identity. No filename, workspace ID, path, content or content hash.</summary>
public sealed record StorageInventoryEntry(Guid Id, StorageFileKind Kind, long? Bytes,
    StorageValidation Validation, StorageError Error, long? Revision, RetentionReason RetainBecause)
{
    public StorageRisk Risk => Error != StorageError.None ? StorageRisk.IncompleteOrUnsafe :
        Kind is StorageFileKind.Backup or StorageFileKind.Prepared or StorageFileKind.Temp or StorageFileKind.Evidence ?
        StorageRisk.ProtectedHistory : StorageRisk.Healthy;
    public bool CanDelete { get; }
    public long EstimatedReclaimedBytes { get; }
}

/// <summary>Counts are bounded observations. Remaining scan bytes use a conservative aggregate budget.</summary>
public sealed record StorageQuota(StorageFileKind Kind, int ObservedFiles, long ObservedBytes,
    int RemainingFiles, long RemainingScanBytes, QuotaPressure Pressure);

public sealed record StorageHealth(bool HasValidatedMain, int ValidatedBackups, int IndependentBackupGenerations,
    long? MinimumRevision, long? MaximumRevision, long? PreparedHighWaterRevision,
    bool RecoverySelectionProven, bool NextSaveMayHitQuota, long RemainingInventoryScanBytes, ImmutableArray<StorageQuota> Quotas);

public sealed class StorageInventory
{
    internal StorageInventory(StorageError error, bool complete, bool totalsComplete, long bytes,
        ImmutableArray<StorageInventoryEntry> entries, StorageHealth health, string? manifest)
    { Error = error; IsComplete = complete; TotalsComplete = totalsComplete; ObservedBytes = bytes; Entries = entries; Health = health; Manifest = manifest; }
    public Guid Id { get; } = Guid.NewGuid();
    public StorageError Error { get; }
    /// <summary>False for any unsafe/unreadable/unknown/corrupt/partial entry or exceeded scan budget.</summary>
    public bool IsComplete { get; }
    public bool TotalsComplete { get; }
    public long ObservedBytes { get; }
    public int ObservedFiles => Entries.Length;
    public ImmutableArray<StorageInventoryEntry> Entries { get; }
    public StorageHealth Health { get; }
    internal string? Manifest { get; }
    public override string ToString() => $"{nameof(StorageInventory)}({Error}, {ObservedFiles}, {IsComplete})";
}

/// <summary>In-process immutable review artifact; not serializable authorization and not portable between stores.</summary>
public sealed class RetentionPlan
{
    internal RetentionPlan(Guid owner, StorageInventory inventory, StorageError error)
    { Owner = owner; Inventory = inventory; Error = error; }
    internal Guid Owner { get; }
    public Guid Id { get; } = Guid.NewGuid();
    public StorageInventory Inventory { get; }
    public StorageError Error { get; }
    public bool IsReviewable => Error == StorageError.None;
    public bool ApplyEnabled { get; }
    public long EstimatedReclaimedBytes { get; }
    public override string ToString() => $"{nameof(RetentionPlan)}({Error}, ApplyDisabled)";
}

/// <summary>Validated local source manifest only. No payload or destination path. No external writes enabled.</summary>
public sealed class BackupExportPlan
{
    internal BackupExportPlan(Guid owner, StorageInventory inventory, StorageError error,
        Guid? source = null, long? revision = null, string? sha256 = null)
    { Owner = owner; Inventory = inventory; Error = error; SourceId = source; SourceRevision = revision; Sha256 = sha256; }
    internal Guid Owner { get; }
    internal StorageInventory Inventory { get; }
    public StorageError Error { get; }
    public Guid? SourceId { get; }
    public long? SourceRevision { get; }
    public int FormatVersion { get; } = WorkspaceCodec.CurrentFormatVersion;
    public int DomainSchemaVersion { get; } = Zhuomian.Core.WorkspaceModel.Workspace.CurrentSchemaVersion;
    /// <summary>SHA256 of exact source bytes, for explicit local export review only; never telemetry.</summary>
    public string? Sha256 { get; }
    public bool ExternalWriteEnabled { get; }
    public override string ToString() => $"{nameof(BackupExportPlan)}({Error}, ExternalWriteDisabled)";
}

public interface IStorageLifecycle
{
    StorageInventory Inventory();
    RetentionPlan DryRun();
    StorageResult ValidatePlan(RetentionPlan plan);
    StorageResult Apply(RetentionPlan plan, bool explicitlyAuthorized);
    BackupExportPlan PlanBackupExport();
    StorageResult ValidateExport(BackupExportPlan plan);
    StorageResult Export(BackupExportPlan plan, bool explicitlyAuthorized);
}
