using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

public sealed class HistorySafetyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("backup")]
    [InlineData("prepared")]
    public void EarlierContradictoryRevisionBlocksEveryMutationAndReadableMainIsReadOnly(string family)
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        var next = first.RenameSpace(Samples.SpaceA, "Next");
        Assert.True(owned.Store().Save(first).Succeeded);
        Assert.True(owned.Store().Save(next).Succeeded);
        File.WriteAllBytes(HistoryPath(owned, family), Samples.Mutate(r => r["payload"]!["spaces"]![0]!["name"] = "contradiction"));
        var before = Assets(owned);
        var store = owned.Store();
        Assert.Equal(StorageError.RevisionConflict, store.Save(next.RenameSpace(Samples.SpaceA, "Third")).Error);
        Assert.Equal(StorageError.RevisionConflict, store.Backup().Error);
        Assert.Equal(StorageError.RevisionConflict, store.Restore().Error);
        var read = store.Load();
        Assert.Equal(StorageError.RevisionConflict, read.Error);
        Assert.True(read.IsReadOnly);
        Assert.Equal(next.Revision, read.Workspace!.Revision);
        Assert.Equal(StorageError.RevisionConflict, store.Inventory().Error);
        Assert.Equal(StorageError.RevisionConflict, store.DryRun().Error);
        AssertAssets(before, owned);
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("prepared")]
    public void ForeignWorkspaceHistoryCannotMergeEvenBelowRequestedRevision(string family)
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        Assert.True(owned.Store().Save(first).Succeeded);
        var foreign = Workspace.FromSnapshot(Guid.NewGuid(), 1, 2, first.ItemStore, first.Spaces.Values);
        File.WriteAllBytes(HistoryPath(owned, family), WorkspaceCodec.Encode(foreign));
        var before = Assets(owned);
        var store = owned.Store();
        Assert.Equal(StorageError.WorkspaceConflict, store.Save(first.RenameSpace(Samples.SpaceA, "Next")).Error);
        Assert.Equal(StorageError.WorkspaceConflict, store.Backup().Error);
        Assert.Equal(StorageError.WorkspaceConflict, store.Restore().Error);
        Assert.Equal(StorageError.WorkspaceConflict, store.Load().Error);
        Assert.Equal(StorageError.WorkspaceConflict, store.Inventory().Error);
        AssertAssets(before, owned);
    }

    [Theory]
    [InlineData("backup", "corrupt")]
    [InlineData("prepared", "corrupt")]
    [InlineData("backup", "future")]
    [InlineData("prepared", "future")]
    [InlineData("backup", "partial")]
    [InlineData("prepared", "partial")]
    public void UnknownHistoricalCoverageNeverAuthorizesMutation(string family, string fault)
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        Assert.True(owned.Store().Save(first).Succeeded);
        byte[] bytes = fault switch
        {
            "corrupt" => [0, 1, 2],
            "future" => Samples.Mutate(r => r["formatVersion"] = 2),
            _ => Samples.Mutate(r => r["payload"]!["futureField"] = "must retain"),
        };
        File.WriteAllBytes(HistoryPath(owned, family), bytes);
        var before = Assets(owned);
        var store = owned.Store();
        Assert.Equal(StorageError.RecoveryRequired, store.Save(first.RenameSpace(Samples.SpaceA, "Next")).Error);
        Assert.Equal(StorageError.RecoveryRequired, store.Backup().Error);
        Assert.Equal(StorageError.RecoveryRequired, store.Restore().Error);
        Assert.True(store.Load().IsReadOnly);
        Assert.False(store.Inventory().IsComplete);
        AssertAssets(before, owned);
    }

    [Fact]
    public void BackupHighWaterCannotBeIgnoredWhenPreparedRecordIsMissing()
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        Assert.True(owned.Store().Save(first).Succeeded);
        var future = Workspace.FromSnapshot(first.Id, 1, 1000, first.ItemStore, first.Spaces.Values, first.RemovedSpaces.Values);
        File.WriteAllBytes(HistoryPath(owned, "backup"), WorkspaceCodec.Encode(future));
        Assert.Equal(StorageError.StaleRevision, owned.Store().Save(first.RenameSpace(Samples.SpaceA, "Old")).Error);
        var restored = owned.Store().Restore();
        Assert.Equal(1001, restored.Workspace!.Revision);
        Assert.Equal(StorageError.StaleRevision, owned.Store().Save(future).Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GapsAndCanonicalDuplicatesRetainIdenticalRetryAndConflictSemantics(bool reformat)
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        Assert.True(owned.Store().Save(first).Succeeded);
        var reserved = Workspace.FromSnapshot(first.Id, 1, 1000, first.ItemStore, first.Spaces.Values, first.RemovedSpaces.Values);
        byte[] bytes = WorkspaceCodec.Encode(reserved);
        if (reformat)
        {
            using var document = JsonDocument.Parse(bytes);
            // Payload raw bytes stay unchanged: only the envelope gains whitespace.
            bytes = System.Text.Encoding.UTF8.GetBytes(" \n" + document.RootElement.GetRawText() + "\n ");
        }
        File.WriteAllBytes(HistoryPath(owned, "prepared"), bytes);
        File.WriteAllBytes(HistoryPath(owned, "prepared"), WorkspaceCodec.Encode(reserved));
        Assert.Equal(StorageError.StaleRevision, owned.Store().Save(first.RenameSpace(Samples.SpaceA, "gap")).Error);
        var different = Workspace.FromSnapshot(first.Id, 1, 1000, first.ItemStore,
            first.RenameSpace(Samples.SpaceA, "different").Spaces.Values, first.RemovedSpaces.Values);
        Assert.Equal(StorageError.RevisionConflict, owned.Store().Save(different).Error);
        Assert.True(owned.Store().Save(reserved).Succeeded);
        Assert.Equal(CommitState.Unchanged, owned.Store().Save(reserved).Commit);
        Assert.Equal(1001, owned.Store().Restore().Workspace!.Revision);
    }

    [Fact]
    public void CrossFamilyConflictCannotBeHiddenByMissingMain()
    {
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        File.WriteAllBytes(HistoryPath(owned, "prepared"), Samples.Mutate(r => r["payload"]!["spaces"]![0]!["name"] = "conflict"));
        File.Delete(owned.Main);
        var before = Assets(owned);
        Assert.Equal(StorageError.RevisionConflict, owned.Store().Restore().Error);
        Assert.Equal(StorageError.RevisionConflict, owned.Store().Load().Error);
        Assert.Equal(StorageError.RevisionConflict, owned.Store().Save(Samples.Full()).Error);
        AssertAssets(before, owned);
    }

    [Fact]
    public void RevisionOverflowDoesNotPublishOrDiscardHistory()
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        Assert.True(owned.Store().Save(first).Succeeded);
        var last = Workspace.FromSnapshot(first.Id, 1, long.MaxValue, first.ItemStore, first.Spaces.Values, first.RemovedSpaces.Values);
        File.WriteAllBytes(HistoryPath(owned, "prepared"), WorkspaceCodec.Encode(last));
        var before = Assets(owned);
        Assert.Equal(StorageError.RevisionOverflow, owned.Store().Restore().Error);
        AssertAssets(before, owned);
    }

    [Fact]
    public void OneThousandRealSaveAttemptsRecordTheUnresolvedQuotaWithoutDeletingHistory()
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store();
        var initial = Workspace.Create(new("00000099-0000-0000-0000-000000000000"));
        var drive = new DriveInfo(Path.GetPathRoot(owned.Root)!);
        long totalDiskBytes = drive.TotalSize, availableDiskBytes = drive.AvailableFreeSpace;
        var timer = Stopwatch.StartNew();
        var milestones = new List<object>();
        Dictionary<string, string>? atLimit = null;
        int committed = 0, refused = 0;
        long estimatedHistoryBytes = 0;
        for (int attempt = 1; attempt <= 1000; attempt++)
        {
            var snapshot = Workspace.FromSnapshot(initial.Id, 1, attempt - 1, initial.ItemStore, initial.Spaces.Values);
            var saveTimer = Stopwatch.StartNew();
            var result = store.Save(snapshot);
            double saveMs = saveTimer.Elapsed.TotalMilliseconds;
            if (attempt <= 256)
            { Assert.True(result.Succeeded); Assert.Equal(CommitState.Committed, result.Commit); committed++; }
            else
            { Assert.Equal(StorageError.TooLarge, result.Error); Assert.Equal(CommitState.NotCommitted, result.Commit); refused++; }
            // Estimate successful read bytes from the complete immutable history before this attempt.
            // This is not device I/O instrumentation and excludes archive/readback/metadata reads.
            estimatedHistoryBytes += Directory.GetFiles(owned.Root, "backup-*.json").Concat(Directory.GetFiles(owned.Root, "prepared-*.json"))
                .Sum(p => new FileInfo(p).Length);
            if (attempt == 256) { atLimit = Assets(owned); }
            if (attempt is 256 or 257 or 512 or 1000)
            {
                var loadTimer = Stopwatch.StartNew();
                Assert.Equal(255, store.Load().Workspace!.Revision);
                double loadMs = loadTimer.Elapsed.TotalMilliseconds;
                var inventory = store.Inventory();
                Assert.True(inventory.IsComplete);
                milestones.Add(new
                {
                    attempt,
                    committed,
                    refused,
                    elapsedMs = timer.Elapsed.TotalMilliseconds,
                    saveMs,
                    loadMs,
                    reads = store.ReadMetrics,
                    files = inventory.ObservedFiles,
                    bytes = inventory.ObservedBytes,
                    historySnapshotBytes = inventory.Entries.Where(e => e.Kind is StorageFileKind.Backup or StorageFileKind.Prepared).Sum(e => e.Bytes ?? 0)
                });
            }
        }
        Assert.Equal(256, committed); Assert.Equal(744, refused);
        AssertAssets(atLimit!, owned);
        Assert.Equal(StorageError.StaleRevision, store.Save(initial).Error);
        Assert.Equal(CommitState.Unchanged, store.Save(Workspace.FromSnapshot(initial.Id, 1, 255, initial.ItemStore, [])).Commit);
        var restoreTimer = Stopwatch.StartNew();
        var restore = store.Restore();
        double restoreMs = restoreTimer.Elapsed.TotalMilliseconds;
        Assert.Equal(StorageError.TooLarge, restore.Error);
        AssertAssets(atLimit!, owned);
        output.WriteLine("E3_4_CAPACITY=" + JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            attempts = 1000,
            committed,
            refused,
            quotaRelief = false,
            totalDiskBytes,
            availableDiskBytes,
            filesystem = drive.DriveFormat,
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            milestones,
            restoreMs,
            restoreError = restore.Error.ToString(),
            estimatedHistoryBytes,
            scanMeasurement = "sum of retained history byte lengths per attempt; estimate, not actual device IO",
            reads = store.ReadMetrics,
            readMeasurement = "successful storage reads and wildcard enumeration entries; includes Load/Inventory/readback; not physical device IO",
            sourceAssetsUnchanged = true,
            physicalDiskFull = false,
            desktopAcceptance = false
        }));
    }

    private static string HistoryPath(OwnedDirectory owned, string family) => Path.Combine(owned.Root, family + "-" + Guid.NewGuid().ToString("N") + ".json");
    private static Dictionary<string, string> Assets(OwnedDirectory owned) => Directory.GetFiles(owned.Root)
        .ToDictionary(p => Path.GetFileName(p)!, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
    private static void AssertAssets(Dictionary<string, string> expected, OwnedDirectory owned)
    {
        var actual = Assets(owned);
        Assert.Equal(expected.Count, actual.Count);
        foreach (var (name, digest) in expected) { Assert.Equal(digest, actual[name]); }
    }
}
