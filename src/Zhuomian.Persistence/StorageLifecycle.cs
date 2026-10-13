using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace Zhuomian.Persistence;

public sealed partial class WorkspaceFileStore : IStorageLifecycle
{
    private readonly Guid _lifecycleOwner = Guid.NewGuid();
    // Four bounded file families, plus main and lock. Unknown entries consume the same bound.
    private const int MaxInventoryEntries = 4 * PersistenceLimits.MaxRecoveryFiles + 2;

    public StorageInventory Inventory() => Inspect(StorageOperation.Inventory).Inventory;

    public RetentionPlan DryRun()
    {
        var inventory = Inspect(StorageOperation.Plan).Inventory;
        var error = inventory.Error;
        if (error == StorageError.None && (!inventory.Health.HasValidatedMain || !inventory.Health.RecoverySelectionProven))
        { error = StorageError.RecoveryRequired; }
        return new(_lifecycleOwner, inventory, error);
    }

    public StorageResult ValidatePlan(RetentionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Owner != _lifecycleOwner) { return LifecycleResult(StorageOperation.ValidatePlan, StorageError.StalePlan); }
        if (!plan.IsReviewable) { return LifecycleResult(StorageOperation.ValidatePlan, plan.Error); }
        return Revalidate(StorageOperation.ValidatePlan, plan.Inventory);
    }

    public StorageResult Apply(RetentionPlan plan, bool explicitlyAuthorized)
    {
        ArgumentNullException.ThrowIfNull(plan);
        // Deliberately no filesystem call: authorization cannot turn an unproven deletion protocol on.
        return LifecycleResult(StorageOperation.Apply,
            explicitlyAuthorized ? StorageError.MaintenanceDisabled : StorageError.AuthorizationRequired);
    }

    public BackupExportPlan PlanBackupExport()
    {
        var scan = Inspect(StorageOperation.ExportPlan);
        if (!scan.Inventory.IsComplete) { return new(_lifecycleOwner, scan.Inventory, scan.Inventory.Error); }
        if (!scan.Inventory.Health.RecoverySelectionProven) { return new(_lifecycleOwner, scan.Inventory, StorageError.RecoveryRequired); }
        var source = scan.Inventory.Entries.Where(e => e.Kind == StorageFileKind.Backup && e.Validation == StorageValidation.ValidSnapshot)
            .OrderByDescending(e => e.Revision).First();
        return new(_lifecycleOwner, scan.Inventory, StorageError.None, source.Id, source.Revision, scan.Hashes[source.Id]);
    }

    public StorageResult ValidateExport(BackupExportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Owner != _lifecycleOwner) { return LifecycleResult(StorageOperation.ValidateExport, StorageError.StalePlan); }
        if (plan.Error != StorageError.None) { return LifecycleResult(StorageOperation.ValidateExport, plan.Error); }
        return Revalidate(StorageOperation.ValidateExport, plan.Inventory);
    }

    public StorageResult Export(BackupExportPlan plan, bool explicitlyAuthorized)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return LifecycleResult(StorageOperation.Export,
            explicitlyAuthorized ? StorageError.MaintenanceDisabled : StorageError.AuthorizationRequired);
    }

    private StorageResult Revalidate(StorageOperation operation, StorageInventory previous)
    {
        var current = Inspect(operation).Inventory;
        if (current.Error != StorageError.None) { return new(current.Error); }
        return LifecycleResult(operation, previous.Manifest == current.Manifest ? StorageError.None : StorageError.StalePlan);
    }

    private StorageResult LifecycleResult(StorageOperation operation, StorageError error)
    { Report(operation, error); return new(error); }

    private (StorageInventory Inventory, Dictionary<Guid, string> Hashes) Inspect(StorageOperation operation)
    {
        lock (_gate)
        {
            try
            {
                // Unlike Save/Load, inspection never creates missing directories.
                using var root = _storage.PinRoot(createMissing: false);
                using var mutex = AcquireLock();
                var scan = Scan(mutex);
                Report(operation, scan.Inventory.Error);
                return scan;
            }
            catch (Exception e) when (IsStorageFailure(e))
            {
                var error = Error(e);
                Report(operation, error);
                return (new(error, false, false, 0, [], new(false, 0, 0, null, null, null, false, true, 0, []), null), []);
            }
        }
    }

    private (StorageInventory Inventory, Dictionary<Guid, string> Hashes) Scan(FileStream mutex)
    {
        var entries = ImmutableArray.CreateBuilder<StorageInventoryEntry>();
        var hashes = new Dictionary<Guid, string>();
        var counts = new Dictionary<StorageFileKind, int>();
        var sizes = new Dictionary<StorageFileKind, long>();
        var contents = new Dictionary<long, string>();
        var generations = new HashSet<long>();
        Guid? workspaceId = null;
        long? minimum = null, maximum = null, prepared = null;
        long bytes = 0, scannedBytes = 0;
        StorageError error = StorageError.None;
        bool enumerated = true, mainValid = false;
        int validBackups = 0;
        using var manifest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var held = new List<FileStream>();
        var names = Directory.EnumerateFileSystemEntries(_storage.Root).Select(Path.GetFileName)
            .Take(MaxInventoryEntries + 1).ToArray();
        if (names.Length > MaxInventoryEntries) { enumerated = false; error = StorageError.TooLarge; }
        var selected = names.Take(MaxInventoryEntries).Order(StringComparer.Ordinal).ToArray();
        try
        {
            foreach (string? value in selected)
            {
                string name = value!;
                var kind = Classify(name);
                Guid id = Guid.NewGuid();
                long? length = null, revision = null;
                var validation = kind == StorageFileKind.Lock ? StorageValidation.CoordinationLock : StorageValidation.Incomplete;
                var entryError = StorageError.None;
                string identity = "", hash = "";
                try
                {
                    if (kind == StorageFileKind.Lock)
                    {
                        length = mutex.Length;
                        identity = OperatingSystem.IsWindows() ? NativeStorage.Identity(mutex.SafeFileHandle) : "lock";
                    }
                    else
                    {
                        // Unknown names are never interpreted as v1, and never returned to the caller.
                        var capture = _storage.Capture(name, read: kind != StorageFileKind.Unknown &&
                            scannedBytes < PersistenceLimits.MaxRecoveryScanBytes, PersistenceLimits.MaxRecoveryScanBytes - scannedBytes, held);
                        length = capture.Length;
                        identity = capture.Identity;
                        if (kind == StorageFileKind.Unknown)
                        { validation = StorageValidation.Unattributed; entryError = StorageError.RecoveryRequired; }
                        else if (capture.Bytes is null || scannedBytes + capture.Length > PersistenceLimits.MaxRecoveryScanBytes)
                        { entryError = StorageError.TooLarge; }
                        else
                        {
                            scannedBytes += capture.Length;
                            hash = Convert.ToHexString(SHA256.HashData(capture.Bytes));
                            hashes[id] = hash;
                            var decoded = WorkspaceCodec.Decode(capture.Bytes);
                            entryError = decoded.Error;
                            validation = decoded.Error == StorageError.UnsupportedVersion ? StorageValidation.UnsupportedVersion :
                                decoded.Workspace is null ? StorageValidation.Corrupt :
                                decoded.IsReadOnly ? StorageValidation.ReadOnlySnapshot : StorageValidation.ValidSnapshot;
                            if (validation == StorageValidation.ReadOnlySnapshot) { entryError = StorageError.ReadOnly; }
                            if (validation == StorageValidation.ValidSnapshot)
                            {
                                revision = decoded.Workspace!.Revision;
                                minimum = minimum is null ? revision : Math.Min(minimum.Value, revision.Value);
                                maximum = maximum is null ? revision : Math.Max(maximum.Value, revision.Value);
                                if (kind == StorageFileKind.Main) { mainValid = true; }
                                if (kind == StorageFileKind.Backup) { validBackups++; generations.Add(revision.Value); }
                                if (kind == StorageFileKind.Prepared) { prepared = Math.Max(prepared ?? 0, revision.Value); }
                                if (kind is StorageFileKind.Main or StorageFileKind.Backup or StorageFileKind.Prepared)
                                {
                                    if (workspaceId is not null && workspaceId != decoded.Workspace.Id) { entryError = StorageError.WorkspaceConflict; }
                                    workspaceId ??= decoded.Workspace.Id;
                                    string canonical = Convert.ToHexString(SHA256.HashData(WorkspaceCodec.Encode(decoded.Workspace)));
                                    if (contents.TryGetValue(revision.Value, out string? previous) && previous != canonical)
                                    { entryError = StorageError.RevisionConflict; }
                                    contents[revision.Value] = canonical;
                                }
                            }
                        }
                    }
                }
                catch (Exception e) when (IsStorageFailure(e))
                {
                    entryError = Error(e);
                    if (kind == StorageFileKind.Temp && IsSharingViolation(e))
                    { validation = StorageValidation.InUse; entryError = StorageError.Busy; }
                }
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
                sizes[kind] = sizes.GetValueOrDefault(kind) + (length ?? 0);
                bytes += length ?? 0;
                if (kind is StorageFileKind.Backup or StorageFileKind.Prepared or StorageFileKind.Temp or StorageFileKind.Evidence &&
                    (counts[kind] > PersistenceLimits.MaxRecoveryFiles || sizes[kind] > PersistenceLimits.MaxRecoveryScanBytes))
                { entryError = StorageError.TooLarge; }
                if (error == StorageError.None && entryError != StorageError.None) { error = entryError; }
                var reason = entryError != StorageError.None && kind != StorageFileKind.Unknown ? RetentionReason.ValidationFailed : kind switch
                {
                    StorageFileKind.Main => RetentionReason.AuthoritativeMain,
                    StorageFileKind.Backup => RetentionReason.RecoveryGeneration,
                    StorageFileKind.Prepared => RetentionReason.RevisionReservation,
                    StorageFileKind.Temp => RetentionReason.UnfinishedOperation,
                    StorageFileKind.Evidence => RetentionReason.UnconfirmedEvidence,
                    StorageFileKind.Lock => RetentionReason.CoordinationLock,
                    _ => RetentionReason.UnknownOwnership,
                };
                entries.Add(new(id, kind, length, validation, entryError, revision, reason));
                // Length-prefixed names avoid delimiter ambiguity. Fingerprint remains private, never diagnostics.
                Append(manifest, name); Append(manifest, identity); Append(manifest, hash);
                Append(manifest, length?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown");
            }
            if (enumerated)
            {
                var after = Directory.EnumerateFileSystemEntries(_storage.Root).Select(Path.GetFileName)
                    .Take(MaxInventoryEntries + 1).Order(StringComparer.Ordinal).ToArray();
                if (!selected.SequenceEqual(after)) { error = StorageError.StalePlan; enumerated = false; }
            }
        }
        finally { foreach (var file in held) { file.Dispose(); } }
        var quotas = ImmutableArray.CreateBuilder<StorageQuota>();
        foreach (var kind in new[] { StorageFileKind.Backup, StorageFileKind.Prepared, StorageFileKind.Temp, StorageFileKind.Evidence })
        {
            int count = counts.GetValueOrDefault(kind);
            long size = sizes.GetValueOrDefault(kind);
            bool sizeProven = enumerated && entries.Where(e => e.Kind == kind).All(e => e.Bytes is not null);
            int remaining = enumerated ? Math.Max(0, PersistenceLimits.MaxRecoveryFiles - count) : 0;
            long scanRemaining = sizeProven ? Math.Max(0, PersistenceLimits.MaxRecoveryScanBytes - size) : 0;
            var pressure = !sizeProven ? QuotaPressure.Unknown : count > PersistenceLimits.MaxRecoveryFiles || size > PersistenceLimits.MaxRecoveryScanBytes ?
                QuotaPressure.Exceeded : remaining == 0 || scanRemaining == 0 ? QuotaPressure.AtLimit :
                remaining <= 2 || scanRemaining <= PersistenceLimits.MaxFileBytes ? QuotaPressure.ApproachingLimit : QuotaPressure.Normal;
            quotas.Add(new(kind, count, size, remaining, scanRemaining, pressure));
        }
        bool complete = enumerated && error == StorageError.None;
        bool recovery = complete && validBackups > 0;
        bool nextSaveRisk = !complete || PersistenceLimits.MaxRecoveryScanBytes - scannedBytes <= PersistenceLimits.MaxFileBytes || quotas.Any(q => q.Kind is StorageFileKind.Backup or StorageFileKind.Prepared && q.Pressure != QuotaPressure.Normal);
        var health = new StorageHealth(mainValid, validBackups, generations.Count, minimum, maximum, prepared, recovery, nextSaveRisk, Math.Max(0, PersistenceLimits.MaxRecoveryScanBytes - scannedBytes), quotas.ToImmutable());
        return (new(error, complete, enumerated && entries.All(e => e.Bytes is not null), bytes, entries.ToImmutable(), health,
            complete ? Convert.ToHexString(manifest.GetHashAndReset()) : null), hashes);
    }

    private static bool IsSharingViolation(Exception error) => error switch
    {
        System.ComponentModel.Win32Exception native => native.NativeErrorCode is 32 or 33,
        IOException io => (io.HResult & 0xFFFF) is 32 or 33,
        _ => false,
    };

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
    }

    private static StorageFileKind Classify(string name)
    {
        if (name == MainFile) { return StorageFileKind.Main; }
        if (name == "workspace.lock") { return StorageFileKind.Lock; }
        foreach (var (prefix, kind) in new[] { ("backup", StorageFileKind.Backup), ("prepared", StorageFileKind.Prepared),
            ("temp", StorageFileKind.Temp), ("evidence", StorageFileKind.Evidence) })
        {
            if (name.Length == prefix.Length + 38 && name.StartsWith(prefix + "-", StringComparison.Ordinal) &&
                name.EndsWith(".json", StringComparison.Ordinal) && Guid.TryParseExact(name.AsSpan(prefix.Length + 1, 32), "N", out _))
            { return kind; }
        }
        return StorageFileKind.Unknown;
    }
}
