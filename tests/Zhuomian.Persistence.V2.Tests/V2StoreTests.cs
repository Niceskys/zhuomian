using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using Zhuomian.Core.WorkspaceModel;

using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2StoreTests(ITestOutputHelper output)
{
    private static readonly JsonSerializerOptions EvidenceOptions = new() { WriteIndented = true };
    private static Workspace Snapshot(long revision, Guid? id = null, bool different = false)
    {
        var full = Samples.Full().RenameSpace(Samples.SpaceA, "revision-" + revision);
        return Workspace.FromSnapshot(id ?? full.Id, full.SchemaVersion, revision, full.ItemStore,
            different ? [] : full.Spaces.Values, full.RemovedSpaces.Values);
    }
    private static WorkspaceV2Store Store(OwnedDirectory root, IV2FaultInjector? faults = null) => WorkspaceV2Store.ForTests(root.Root, faults);

    [Fact]
    public void ExplicitInitializationAndIndependentRootsNeverTouchV1()
    {
        using var v1 = new OwnedDirectory(); using var v2 = new OwnedDirectory();
        Assert.True(v1.Store().Save(Samples.Full()).Succeeded);
        var hashes = Directory.GetFiles(v1.Root).ToDictionary(p => Path.GetFileName(p), p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        Assert.False(Store(v2).Save(Snapshot(1)).Succeeded);
        Assert.False(Store(v2).Load().Error == StorageError.None);
        Assert.True(Store(v2).InitializeNew(Snapshot(1)).Succeeded);
        Assert.False(Store(v2).InitializeNew(Snapshot(2)).Succeeded);
        Assert.True(Store(v2).Save(Snapshot(2)).Succeeded);
        File.Delete(Path.Combine(v2.Root, "state-v2.bin"));
        Assert.False(Store(v2).InitializeNew(Snapshot(3)).Succeeded);
        Assert.False(Store(v2).Save(Snapshot(3)).Succeeded);
        Assert.Null(Store(v2).Load().Workspace);
        foreach (var pair in hashes) { Assert.Equal(pair.Value, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(v1.Root, pair.Key!))))); }
        Assert.Equal(42, v1.Store().Load().Workspace!.Revision);
    }

    [Fact]
    public void NonemptyCoordinationFileIsUncertainDataAndRefusesInitialization()
    {
        using var root = new OwnedDirectory();
        string path = Path.Combine(root.Root, "workspace.lock"); File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Equal(StorageError.RecoveryRequired, Store(root).InitializeNew(Snapshot(1)).Error);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(root.Root));
    }

    [Fact]
    public void GapsConflictsForeignIdsAndOverflowAreRefusedCorrectly()
    {
        using var root = new OwnedDirectory(); var store = Store(root);
        Assert.True(store.InitializeNew(Snapshot(2)).Succeeded);
        Assert.True(store.Save(Snapshot(100)).Succeeded);
        Assert.Equal(StorageError.StaleRevision, store.Save(Snapshot(3)).Error);
        Assert.Equal(StorageError.RevisionConflict, store.Save(Snapshot(100, different: true)).Error);
        Assert.Equal(StorageError.WorkspaceConflict, store.Save(Snapshot(101, Guid.NewGuid())).Error);
        Assert.Equal(CommitState.Unchanged, store.Save(Snapshot(100)).Commit);
        Assert.True(store.Save(Snapshot(long.MaxValue)).Succeeded);
        Assert.Equal(StorageError.RevisionOverflow, store.Restore().Error);
    }

    [Theory]
    [InlineData(V2Stage.BeforeWrite)]
    [InlineData(V2Stage.DuringWrite)]
    [InlineData(V2Stage.BeforeFlush)]
    [InlineData(V2Stage.BeforeReadback)]
    [InlineData(V2Stage.BeforeReservation)]
    [InlineData(V2Stage.AfterReservation)]
    [InlineData(V2Stage.BeforeMain)]
    [InlineData(V2Stage.AfterMain)]
    public void EveryCutRestartsAndRetriesWithoutLosingCommitments(V2Stage stage)
    {
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        var result = Store(root, new Cut(stage)).Save(Snapshot(10));
        var restart = Store(root); var current = restart.Load();
        Assert.Equal(StorageError.None, current.Error);
        bool main = stage == V2Stage.AfterMain;
        Assert.Equal(main ? 10 : 1, current.Workspace!.Revision);
        Assert.Equal(main ? CommitState.Committed : CommitState.NotCommitted, result.Commit);
        bool reserved = stage is V2Stage.AfterReservation or V2Stage.BeforeMain or V2Stage.AfterMain;
        if (reserved) { Assert.Equal(StorageError.RevisionConflict, restart.Save(Snapshot(10, different: true)).Error); }
        Assert.True(restart.Save(Snapshot(10)).Succeeded);
        Assert.Equal(10, Store(root).Load().Workspace!.Revision);
        Assert.Equal(CommitState.Unchanged, restart.Save(Snapshot(10)).Commit);
    }

    [Fact]
    public void PostPublicationUnreadableOutcomeIsUnknownThenRestartResolvesIt()
    {
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        var fault = new Obstruct();
        var result = Store(root, fault).Save(Snapshot(2));
        Assert.Equal(CommitState.Unknown, result.Commit);
        Assert.Equal(2, Store(root).Load().Workspace!.Revision);
        Assert.Equal(CommitState.Unchanged, Store(root).Save(Snapshot(2)).Commit);
    }

    [Fact]
    public void ReservedButUnpublishedRevisionRaisesRestoreFloorAndIsNotRecoveryContent()
    {
        using var root = new OwnedDirectory(); var store = Store(root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        Assert.False(Store(root, new Cut(V2Stage.BeforeMain)).Save(Snapshot(100)).Succeeded);
        Assert.Equal(StorageError.RecoveryRequired, store.RestoreRevision(100).Error);
        var restored = store.RestoreRevision(1);
        Assert.Equal(StorageError.None, restored.Error); Assert.Equal(101, restored.Workspace!.Revision);
        Assert.Equal("revision-1", restored.Workspace.Spaces[Samples.SpaceA].Name);
        Assert.Equal(StorageError.StaleRevision, store.Save(Snapshot(100)).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDamagedAnyHistoryPageIsNeverEmptyHistory(bool damage)
    {
        using var root = new OwnedDirectory(); var store = Store(root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        Assert.True(store.Save(Snapshot(2)).Succeeded);
        var segment = Directory.GetFiles(root.Root, "segment-*.bin")[0];
        if (damage) { File.WriteAllBytes(segment, [1, 2, 3]); } else { File.Delete(segment); }
        Assert.False(Store(root).Save(Snapshot(3)).Succeeded);
        Assert.Null(Store(root).Load().Workspace);
        Assert.False(Store(root).Restore().Error == StorageError.None);
        Assert.False(Store(root).InitializeNew(Snapshot(3)).Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamagedOrFutureRootRefusesDowngrade(bool future)
    {
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        string path = Path.Combine(root.Root, "state-v2.bin");
        byte[] bytes = File.ReadAllBytes(path);
        bytes[0] = 99;
        if (future) { SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32); }
        File.WriteAllBytes(path, bytes);
        Assert.Equal(future ? StorageError.UnsupportedVersion : StorageError.IntegrityFailure, Store(root).Load().Error);
        Assert.False(Store(root).Save(Snapshot(2)).Succeeded);
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("ordinal")]
    [InlineData("future")]
    [InlineData("selector")]
    [InlineData("length")]
    public void InternallyChecksummedButInconsistentGraphRefusesMutation(string kind)
    {
        using var root = new OwnedDirectory(); var store = Store(root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        string state = Path.Combine(root.Root, "state-v2.bin"); byte[] rootBytes = File.ReadAllBytes(state);
        string segment = Directory.GetFiles(root.Root, "segment-*.bin").Single(); byte[] bytes = File.ReadAllBytes(segment);
        if (kind == "selector") { BitConverter.GetBytes(2L).CopyTo(rootBytes, 60); }
        else
        {
            if (kind == "foreign") { Guid.NewGuid().ToByteArray().CopyTo(bytes, 4); }
            if (kind == "ordinal") { BitConverter.GetBytes(2L).CopyTo(bytes, 20); }
            if (kind == "future") { BitConverter.GetBytes(99).CopyTo(bytes, 0); }
            if (kind == "length") { BitConverter.GetBytes(bytes.Length).CopyTo(bytes, 68); }
            SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
            byte[] digest = SHA256.HashData(bytes);
            File.WriteAllBytes(Path.Combine(root.Root, "segment-" + Convert.ToHexString(digest) + ".bin"), bytes);
            digest.CopyTo(rootBytes, 28);
        }
        SHA256.HashData(rootBytes.AsSpan(0, rootBytes.Length - 32)).CopyTo(rootBytes, rootBytes.Length - 32);
        File.WriteAllBytes(state, rootBytes);
        Assert.Null(store.Load().Workspace); Assert.False(store.Save(Snapshot(2)).Succeeded);
        Assert.False(store.Restore().Error == StorageError.None);
    }

    [Fact]
    public void WriteReadbackFailureRetainsOldMainAndNeverReservesAnUnverifiedRecord()
    {
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        Assert.False(Store(root, new Readback()).Save(Snapshot(2)).Succeeded);
        Assert.Equal(1, Store(root).Load().Workspace!.Revision);
        Assert.True(Store(root).Save(Snapshot(2, different: true)).Succeeded);
    }

    private sealed class Readback : IV2FaultInjector
    {
        private bool _written;
        public void Check(V2Stage stage)
        {
            if (stage == V2Stage.BeforeFlush) { _written = true; }
            if (_written && stage == V2Stage.BeforeReadback) { throw new IOException(); }
        }
    }

    [Fact]
    public void Continuous1024RealSavesRetainVerifiedHistoryAndRestartRecovery()
    {
        using var root = new OwnedDirectory(); var store = Store(root);
        var initialSnapshot = Snapshot(0);
        var createTimer = Stopwatch.StartNew();
        Assert.True(store.InitializeNew(initialSnapshot).Succeeded);
        double initializeMs = createTimer.Elapsed.TotalMilliseconds;
        List<object> checkpoints = [];
        var initialTimer = Stopwatch.StartNew(); Assert.Equal(StorageError.None, store.Load().Error);
        double initialLoadMs = initialTimer.Elapsed.TotalMilliseconds;
        initialTimer.Restart(); Assert.Equal(CommitState.Unchanged, store.Backup().Commit);
        double initialBackupMs = initialTimer.Elapsed.TotalMilliseconds;
        double initialRestoreMs;
        using (var initialRecovery = new OwnedDirectory())
        {
            foreach (string path in Directory.GetFiles(root.Root))
            { if (Path.GetFileName(path) != "workspace.lock") { File.Copy(path, Path.Combine(initialRecovery.Root, Path.GetFileName(path))); } }
            var recovery = Store(initialRecovery); initialTimer.Restart();
            var result = recovery.RestoreRevision(0); initialRestoreMs = initialTimer.Elapsed.TotalMilliseconds;
            Assert.Equal(StorageError.None, result.Error); Assert.Equal(1, result.Workspace!.Revision);
        }
        int success = 0;
        double firstSaveMs = 0;
        for (int revision = 1; revision <= 1024; revision++)
        {
            var desired = Snapshot(revision);
            long allocation = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            var result = store.Save(desired); double saveMs = timer.Elapsed.TotalMilliseconds;
            long saveAllocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
            Assert.True(result.Succeeded, $"Save {revision}: {result.Error}/{result.Commit}");
            Assert.Equal(CommitState.Committed, result.Commit); success++;
            if (revision == 1) { firstSaveMs = saveMs; }
            if (revision is 32 or 128 or 256 or 512 or 1000 or 1024)
            {
                timer.Restart(); allocation = GC.GetAllocatedBytesForCurrentThread();
                var loaded = store.Load(); double loadMs = timer.Elapsed.TotalMilliseconds;
                long loadAllocation = GC.GetAllocatedBytesForCurrentThread() - allocation;
                Assert.Equal(StorageError.None, loaded.Error); Assert.Equal(revision, loaded.Workspace!.Revision);
                timer.Restart(); Assert.Equal(CommitState.Unchanged, store.Backup().Commit); double backupMs = timer.Elapsed.TotalMilliseconds;
                // Clones use only this loop's generated files; they measure Restore without
                // altering the authoritative 1024-Save sequence or bypassing graph validation.
                using var recoveryRoot = new OwnedDirectory();
                foreach (string path in Directory.GetFiles(root.Root))
                { if (Path.GetFileName(path) != "workspace.lock") { File.Copy(path, Path.Combine(recoveryRoot.Root, Path.GetFileName(path))); } }
                var recoveryStore = Store(recoveryRoot);
                timer.Restart(); var checkpointRestore = recoveryStore.RestoreRevision(0);
                double restoreMs = timer.Elapsed.TotalMilliseconds;
                Assert.Equal(StorageError.None, checkpointRestore.Error); Assert.Equal(revision + 1, checkpointRestore.Workspace!.Revision);
                timer.Restart(); Assert.Equal(StorageError.StaleRevision, store.Save(Snapshot(0)).Error);
                double refusedSaveMs = timer.Elapsed.TotalMilliseconds;
                var files = Directory.GetFiles(root.Root);
                var disk = new DriveInfo(Path.GetPathRoot(root.Root)!);
                checkpoints.Add(new
                {
                    attempts = revision,
                    successes = success,
                    refusals = 0,
                    currentRevision = revision,
                    historyFiles = files.Count(p => Path.GetFileName(p).StartsWith("segment-", StringComparison.Ordinal)),
                    totalFiles = files.Length,
                    dataBytes = files.Sum(p => new FileInfo(p).Length),
                    freeBytes = disk.AvailableFreeSpace,
                    diskTotalBytes = disk.TotalSize,
                    saveMs,
                    loadMs,
                    backupMs,
                    restoreMs,
                    refusedSaveMs,
                    saveAllocatedBytes = saveAllocation,
                    loadAllocatedBytes = loadAllocation,
                    completeHistoryVerified = true
                });
            }
        }
        store = Store(root); Assert.Equal(1024, store.Load().Workspace!.Revision);
        Assert.Equal(StorageError.StaleRevision, store.Save(Snapshot(256)).Error);
        Assert.Equal(StorageError.RevisionConflict, store.Save(Snapshot(1024, different: true)).Error);
        Assert.Equal(CommitState.Unchanged, store.Save(Snapshot(1024)).Commit);
        var restoreTimer = Stopwatch.StartNew(); var restored = store.RestoreRevision(256);
        double finalRestoreMs = restoreTimer.Elapsed.TotalMilliseconds;
        Assert.Equal(StorageError.None, restored.Error); Assert.Equal(1025, restored.Workspace!.Revision);
        Assert.Equal("revision-256", restored.Workspace.Spaces[Samples.SpaceA].Name);
        Assert.Equal(1025, Store(root).Load().Workspace!.Revision);
        var evidence = new
        {
            platform = Environment.OSVersion.VersionString,
            windowsNtfs = OperatingSystem.IsWindows(),
            filesystem = new DriveInfo(Path.GetPathRoot(root.Root)!).DriveFormat,
            saves = success,
            initializeMs,
            firstSaveMs,
            initialRestoreMs,
            initialLoadMs,
            initialBackupMs,
            checkpoints,
            restoreMs = finalRestoreMs,
            restoredRevision = 1025,
            physicalPowerLossTested = false,
            fullAcceptance = false
        };
        string json = JsonSerializer.Serialize(evidence, EvidenceOptions); output.WriteLine(json);
        string? directory = Environment.GetEnvironmentVariable("E3_V2_EVIDENCE_DIR");
        if (directory is not null) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "v2-capacity.json"), json); }
    }

    private sealed class Cut(V2Stage target) : IV2FaultInjector
    { public void Check(V2Stage stage) { if (stage == target) { throw new IOException("generated fault", unchecked((int)0x8007001D)); } } }
    private sealed class Obstruct : IV2FaultInjector
    {
        private bool _published;
        public void Check(V2Stage stage)
        {
            if (stage == V2Stage.AfterMain) { _published = true; throw new IOException(); }
            if (_published && stage == V2Stage.BeforeReadback) { throw new IOException(); }
        }
    }
}
