using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

public sealed class FileStoreTests
{
    [Fact]
    public void ActualDiskCreateReplaceLoadBackupAndRestorePreserveSourceResources()
    {
        using var owned = new OwnedDirectory();
        string source = Path.Combine(owned.Root, "source.txt");
        System.IO.File.WriteAllText(source, "original external resource");
        var store = owned.Store();
        var first = Samples.Full();
        Assert.Equal(CommitState.Committed, store.Save(first).Commit);
        Assert.Equal(first.Revision, store.Load().Workspace!.Revision);
        Assert.True(store.Backup().Succeeded);
        var next = first.RenameSpace(Samples.SpaceA, "Changed");
        Assert.True(store.Save(next).Succeeded);
        Assert.Equal("Changed", store.Load().Workspace!.Spaces[Samples.SpaceA].Name);
        var restored = store.Restore();
        Assert.Equal(RecoveryState.BackupRecovered, restored.State);
        Assert.Equal("A", restored.Workspace!.Spaces[Samples.SpaceA].Name);
        Assert.True(restored.Workspace.Revision > next.Revision);
        Assert.Equal(restored.Workspace.Revision, store.Load().Workspace!.Revision);
        Assert.Equal("original external resource", System.IO.File.ReadAllText(source));
        Assert.NotEmpty(Directory.GetFiles(owned.Root, "evidence-*.json"));
        Assert.NotEmpty(Directory.GetFiles(owned.Root, "backup-*.json"));
    }

    [Fact]
    public void StaleSameRevisionConflictDuplicateAndWorkspaceConflictAreExplicit()
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store();
        var first = Samples.Full();
        Assert.True(store.Save(first).Succeeded);
        Assert.Equal(CommitState.Unchanged, store.Save(first).Commit);
        var changed = first.RenameSpace(Samples.SpaceA, "Next");
        Assert.True(store.Save(changed).Succeeded);
        Assert.Equal(StorageError.StaleRevision, store.Save(first).Error);
        var conflict = Workspace.FromSnapshot(first.Id, 1, changed.Revision, first.ItemStore, first.Spaces.Values, first.RemovedSpaces.Values);
        Assert.Equal(StorageError.RevisionConflict, store.Save(conflict).Error);
        var foreign = Workspace.FromSnapshot(Guid.NewGuid(), 1, 999, first.ItemStore, first.Spaces.Values);
        Assert.Equal(StorageError.WorkspaceConflict, store.Save(foreign).Error);
        Assert.Equal(changed.Revision, store.Load().Workspace!.Revision);
    }

    [Theory]
    [InlineData(StorageStage.BeforeTempWrite, false)]
    [InlineData(StorageStage.BeforeFlush, false)]
    [InlineData(StorageStage.BeforeReplace, false)]
    [InlineData(StorageStage.BeforeTempWrite, true)]
    [InlineData(StorageStage.BeforeFlush, true)]
    [InlineData(StorageStage.BeforeReplace, true)]
    public void InjectedWriteFlushReplaceAndDiskFullFailuresKeepPreviousMainAndGoodBackups(StorageStage stage, bool diskFull)
    {
        using var owned = new OwnedDirectory();
        var initial = Samples.Full();
        Assert.True(owned.Store().Save(initial).Succeeded);
        byte[] before = System.IO.File.ReadAllBytes(owned.Main);
        var backups = Directory.GetFiles(owned.Root, "backup-*.json").ToDictionary(p => p, System.IO.File.ReadAllBytes);
        var diagnostics = new Diagnostics();
        var failing = owned.Store(new Fault(stage, diskFull), diagnostics);
        var next = initial.RenameSpace(Samples.SpaceA, "Next");
        var result = failing.Save(next);
        Assert.Equal(diskFull ? StorageError.DiskFull : StorageError.IoFailure, result.Error);
        Assert.Equal(CommitState.NotCommitted, result.Commit);
        Assert.Equal(before, System.IO.File.ReadAllBytes(owned.Main));
        foreach (var (path, bytes) in backups) { Assert.Equal(bytes, System.IO.File.ReadAllBytes(path)); }
        Assert.True(owned.Store().Save(next).Succeeded);
        Assert.DoesNotContain(owned.Root, string.Join(',', diagnostics.Events), StringComparison.Ordinal);
        Assert.DoesNotContain("private", string.Join(',', diagnostics.Events), StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorAfterReplaceIsResolvedByCheckingCommittedBytes()
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        Assert.True(owned.Store().Save(first).Succeeded);
        var next = first.RenameSpace(Samples.SpaceA, "Next");
        var result = owned.Store(new Fault(StorageStage.AfterReplace)).Save(next);
        Assert.True(result.Succeeded);
        Assert.Equal(CommitState.Committed, result.Commit);
        Assert.Equal(next.Revision, owned.Store().Load().Workspace!.Revision);
    }

    [Fact]
    public void FirstCreateFailureCanRetryIdenticalReservedRevision()
    {
        using var owned = new OwnedDirectory();
        var workspace = Samples.Full();
        Assert.Equal(StorageError.IoFailure, owned.Store(new Fault(StorageStage.BeforeReplace)).Save(workspace).Error);
        Assert.False(System.IO.File.Exists(owned.Main));
        Assert.True(owned.Store().Save(workspace).Succeeded);
        Assert.Equal(workspace.Revision, owned.Store().Load().Workspace!.Revision);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("truncated")]
    [InlineData("illegal")]
    [InlineData("tampered")]
    [InlineData("half-written")]
    public void CorruptPrimaryLoadsGoodBackupWithoutRewritingEvidenceThenExplicitlyRestores(string fault)
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store();
        var first = Samples.Full();
        Assert.True(store.Save(first).Succeeded);
        Assert.True(store.Save(first.RenameSpace(Samples.SpaceA, "Next")).Succeeded);
        byte[] broken = fault switch
        {
            "empty" => [],
            "truncated" => WorkspaceCodec.Encode(first)[..50],
            "illegal" => Encoding.UTF8.GetBytes("malicious invalid json"),
            "tampered" => Samples.Mutate(r => r["payload"]!["revision"] = 888, false),
            _ => WorkspaceCodec.Encode(first)[..300],
        };
        System.IO.File.WriteAllBytes(owned.Main, broken);
        var backups = Directory.GetFiles(owned.Root, "backup-*.json").ToDictionary(p => p, System.IO.File.ReadAllBytes);
        var result = store.Load();
        Assert.Equal(RecoveryState.BackupRecovered, result.State);
        Assert.True(result.IsReadOnly);
        Assert.Equal(42, result.Workspace!.Revision);
        Assert.NotEqual(StorageError.None, result.PrimaryError);
        Assert.Equal(broken, System.IO.File.ReadAllBytes(owned.Main));
        Assert.Equal(StorageError.RecoveryRequired, store.Save(result.Workspace).Error);
        Assert.False(store.Backup().Succeeded);
        var restored = store.Restore();
        Assert.Equal(RecoveryState.BackupRecovered, restored.State);
        Assert.Equal(44, restored.Workspace!.Revision);
        Assert.Contains(Directory.GetFiles(owned.Root, "evidence-*.json"), p => System.IO.File.ReadAllBytes(p).AsSpan().SequenceEqual(broken));
        foreach (var (path, bytes) in backups) { Assert.Equal(bytes, System.IO.File.ReadAllBytes(path)); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CorruptBackupIsIgnoredButBothCorruptAreUnrecoverable(bool mainCorrupt)
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store();
        Assert.True(store.Save(Samples.Full()).Succeeded);
        foreach (string backup in Directory.GetFiles(owned.Root, "backup-*.json")) { System.IO.File.WriteAllText(backup, "broken"); }
        if (mainCorrupt) { System.IO.File.WriteAllText(owned.Main, "broken"); }
        var result = store.Load();
        Assert.Equal(mainCorrupt ? RecoveryState.Unrecoverable : RecoveryState.Normal, result.State);
        if (mainCorrupt) { Assert.Null(result.Workspace); }
        Assert.Equal(mainCorrupt ? "broken" : Encoding.UTF8.GetString(WorkspaceCodec.Encode(Samples.Full())), System.IO.File.ReadAllText(owned.Main));
    }

    [Theory]
    [InlineData("kind")]
    [InlineData("extra")]
    [InlineData("dangling")]
    public void PartialLoadKeepsDiskBytesAndRefusesLossySaveOrRestore(string fault)
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store();
        Assert.True(store.Save(Samples.Full()).Succeeded);
        byte[] bytes = Samples.Mutate(r =>
        {
            if (fault == "kind") { r["payload"]!["items"]![0]!["kind"] = "future-widget"; }
            else if (fault == "extra") { r["payload"]!["futureLayout"] = new JsonObject { ["x"] = 1 }; }
            else { r["payload"]!["spaces"]![0]!["itemIds"]!.AsArray().Add(Guid.NewGuid()); }
        });
        System.IO.File.WriteAllBytes(owned.Main, bytes);
        var result = store.Load();
        Assert.Equal(RecoveryState.PartiallyQuarantined, result.State);
        Assert.Equal(StorageError.ReadOnly, store.Save(result.Workspace!).Error);
        Assert.Equal(StorageError.ReadOnly, store.Restore().Error);
        Assert.Equal(bytes, System.IO.File.ReadAllBytes(owned.Main));
    }

    [Fact]
    public void FutureSchemaNeverAutomaticallyDowngradesToBackup()
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store();
        Assert.True(store.Save(Samples.Full()).Succeeded);
        byte[] bytes = Samples.Mutate(r => r["formatVersion"] = 2);
        System.IO.File.WriteAllBytes(owned.Main, bytes);
        Assert.Equal(RecoveryState.UnsupportedVersion, store.Load().State);
        Assert.Equal(RecoveryState.UnsupportedVersion, store.Restore().State);
        Assert.Equal(StorageError.RecoveryRequired, store.Save(Samples.Full()).Error);
        Assert.Equal(bytes, System.IO.File.ReadAllBytes(owned.Main));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureVersionFieldsCannotBeMisclassifiedAsV1CorruptionAndDowngraded(bool domainSchema)
    {
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        byte[] future = Samples.Mutate(r =>
        {
            if (domainSchema) { r["payload"]!["schemaVersion"] = 2; }
            else { r["formatVersion"] = 2; }
            r["payload"]!["futureObject"] = new JsonObject { ["$type"] = "future schema field" };
        });
        System.IO.File.WriteAllBytes(owned.Main, future);
        Assert.Equal(RecoveryState.UnsupportedVersion, owned.Store().Load().State);
        Assert.Equal(RecoveryState.UnsupportedVersion, owned.Store().Restore().State);
        Assert.Equal(future, System.IO.File.ReadAllBytes(owned.Main));
        Assert.Empty(Directory.GetFiles(owned.Root, "evidence-*.json"));
    }

    [Fact]
    public void FailedRestoreKeepsExactDamagedMainAndAllBackupAssets()
    {
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        System.IO.File.WriteAllText(owned.Main, "broken");
        var result = owned.Store(new Fault(StorageStage.BeforeReplace)).Restore();
        Assert.Equal(StorageError.IoFailure, result.Error);
        Assert.Equal("broken", System.IO.File.ReadAllText(owned.Main));
        Assert.NotEmpty(Directory.GetFiles(owned.Root, "backup-*.json"));
        Assert.Equal(RecoveryState.BackupRecovered, owned.Store().Restore().State);
    }

    [Fact]
    public async Task ConcurrentInstancesRejectConflictsAndNeverInterleaveBytes()
    {
        using var owned = new OwnedDirectory();
        var first = Samples.Full();
        Assert.True(owned.Store().Save(first).Succeeded);
        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() => owned.Store().Save(first.RenameSpace(Samples.SpaceA, "Writer " + i)))).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, r => r.Succeeded);
        Assert.All(results.Where(r => !r.Succeeded), r => Assert.Contains(r.Error, new[] { StorageError.Busy, StorageError.RevisionConflict }));
        Assert.Equal(43, owned.Store().Load().Workspace!.Revision);
    }

    [Fact]
    public void ExternalProcessStyleExclusiveLockRefusesAllOperationsAndReleasesResources()
    {
        using var owned = new OwnedDirectory();
        using (var external = new FileStream(Path.Combine(owned.Root, "workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(StorageError.Busy, owned.Store().Save(Samples.Full()).Error);
            Assert.Equal(StorageError.Busy, owned.Store().Load().Error);
            Assert.Equal(StorageError.Busy, owned.Store().Backup().Error);
            Assert.Equal(StorageError.Busy, owned.Store().Restore().Error);
        }
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        Assert.True(owned.Store().Backup().Succeeded);
    }

    [Fact]
    public async Task ActualSecondProcessOwnsLockAndCannotBeBypassedByAnotherStore()
    {
        using var owned = new OwnedDirectory();
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lock-fixture", "Zhuomian.Persistence.LockFixture.dll"));
        start.ArgumentList.Add(owned.Root);
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("locked", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal(StorageError.Busy, owned.Store().Save(Samples.Full()).Error);
            Assert.Equal(StorageError.Busy, owned.Store().Restore().Error);
            await child.StandardInput.WriteLineAsync("release");
            await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(0, child.ExitCode);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
        }
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
    }

    [Fact]
    public void RecoveryAdvancesPastFailedPreparedRevisionAndRejectsOldRetry()
    {
        using var owned = new OwnedDirectory();
        var initial = Samples.Full();
        Assert.True(owned.Store().Save(initial).Succeeded);
        var next = initial.RenameSpace(Samples.SpaceA, "Next");
        Assert.False(owned.Store(new Fault(StorageStage.BeforeReplace)).Save(next).Succeeded);
        System.IO.File.WriteAllText(owned.Main, "broken");
        Assert.Equal(44, owned.Store().Restore().Workspace!.Revision);
        Assert.Equal(StorageError.StaleRevision, owned.Store().Save(next).Error);
    }

    [Fact]
    public void MissingMainWithHistoryRequiresRecoveryAndDoesNotInitializeNewWorkspace()
    {
        using var owned = new OwnedDirectory();
        Assert.Equal(RecoveryState.NotFound, owned.Store().Load().State);
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        System.IO.File.Delete(owned.Main);
        Assert.Equal(RecoveryState.BackupRecovered, owned.Store().Load().State);
        Assert.Equal(StorageError.WorkspaceConflict, owned.Store().Save(Workspace.Create(Guid.NewGuid())).Error);
        Assert.Equal(RecoveryState.BackupRecovered, owned.Store().Restore().State);
    }

    [Fact]
    public void CorruptRevisionReservationProtectsUnknownHighWaterMark()
    {
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        System.IO.File.WriteAllText(Directory.GetFiles(owned.Root, "prepared-*.json")[0], "broken");
        Assert.Equal(StorageError.RecoveryRequired, owned.Store().Save(Samples.Full().RenameSpace(Samples.SpaceA, "Next")).Error);
        Assert.Equal(StorageError.RecoveryRequired, owned.Store().Restore().Error);
        Assert.Equal(42, owned.Store().Load().Workspace!.Revision);
    }

    [Fact]
    public void SnapshotStringsAreBoundedBeforeDtoSerializationOrAnyDiskMutation()
    {
        using var owned = new OwnedDirectory();
        var snapshot = Samples.Full().RenameSpace(Samples.SpaceA, new string('x', PersistenceLimits.MaxStringLength + 1));
        Assert.Equal(StorageError.TooLarge, owned.Store().Save(snapshot).Error);
        Assert.False(System.IO.File.Exists(owned.Main));
        Assert.Empty(Directory.GetFiles(owned.Root, "*.json"));
    }

    [Fact]
    public void AggregateSerializationLimitStopsLargeValidSnapshotBeforeCreatingFiles()
    {
        using var owned = new OwnedDirectory();
        string display = new('x', 10000);
        var items = Enumerable.Range(0, 1000).Select(i => new FileItem(Guid.NewGuid(), new(display), new(@"C:\fixtures\large-" + i + ".txt")));
        var snapshot = Workspace.FromSnapshot(Guid.NewGuid(), 1, 1, ItemStore.FromSnapshot(items, []), []);
        Assert.Equal(StorageError.TooLarge, owned.Store().Save(snapshot).Error);
        Assert.Empty(Directory.GetFiles(owned.Root, "*.json"));
    }

    [Fact]
    public void ConflictingBackupsAtEqualRevisionCannotBeChosenArbitrarily()
    {
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        System.IO.File.WriteAllBytes(Path.Combine(owned.Root, "backup-conflict.json"), Samples.Mutate(r => r["payload"]!["spaces"]![0]!["name"] = "conflict"));
        System.IO.File.WriteAllText(owned.Main, "broken");
        Assert.Equal(StorageError.RevisionConflict, owned.Store().Load().Error);
        Assert.Equal(StorageError.RevisionConflict, owned.Store().Restore().Error);
        Assert.Equal("broken", System.IO.File.ReadAllText(owned.Main));
    }

    [Fact]
    public void RetentionQuotaRejectsWorkWithoutDeletingAnyRecoveryFile()
    {
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        for (int i = 0; i < PersistenceLimits.MaxRecoveryFiles; i++)
        { System.IO.File.WriteAllText(Path.Combine(owned.Root, "backup-quota-" + i + ".json"), "private recovery evidence"); }
        Assert.Equal(StorageError.TooLarge, owned.Store().Backup().Error);
        Assert.Equal(StorageError.TooLarge, owned.Store().Restore().Error);
        Assert.Equal(PersistenceLimits.MaxRecoveryFiles + 1, Directory.GetFiles(owned.Root, "backup-*.json").Length);
        Assert.Equal(42, owned.Store().Load().Workspace!.Revision);
    }

    [Fact]
    public void LoadHasNoLaunchAdapterOrRuntimePermissionDependency()
    {
        var assembly = typeof(WorkspaceFileStore).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), a => a.Name!.Contains("Launching", StringComparison.Ordinal));
        Assert.DoesNotContain(assembly.GetTypes().SelectMany(t => t.GetMethods()).SelectMany(m => m.GetParameters()), p => p.ParameterType.Name.Contains("Launch", StringComparison.Ordinal));
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        Assert.IsType<Zhuomian.Core.WorkspaceModel.ApplicationItem>(owned.Store().Load().Workspace!.ItemStore.Items.Values.Single(i => i is ApplicationItem));
    }
}
