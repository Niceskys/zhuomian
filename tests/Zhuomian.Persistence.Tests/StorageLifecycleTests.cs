using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

public sealed class StorageLifecycleTests
{
    private static string Add(OwnedDirectory owned, string prefix, byte[] bytes)
    {
        string path = Path.Combine(owned.Root, prefix + "-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllBytes(path, bytes); return path;
    }
    private static Dictionary<string, byte[]> State(OwnedDirectory owned) => Directory.GetFiles(owned.Root).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
    private static void Unchanged(OwnedDirectory owned, Dictionary<string, byte[]> before)
    {
        var after = State(owned);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, bytes) in before) { Assert.Equal(bytes, after[name]); }
    }

    [Fact]
    public void InventoryClassifiesAllOwnedFamiliesAndExposesOnlyRedactedDiagnostics()
    {
        using var owned = new OwnedDirectory();
        var diagnostics = new Diagnostics();
        var store = owned.Store(diagnostics: diagnostics);
        Assert.True(store.Save(Samples.Full()).Succeeded);
        Add(owned, "temp", WorkspaceCodec.Encode(Samples.Full()));
        Add(owned, "evidence", WorkspaceCodec.Encode(Samples.Full()));
        var before = State(owned);
        var inventory = store.Inventory();
        Assert.True(inventory.IsComplete); Assert.True(inventory.TotalsComplete);
        Assert.Equal(6, inventory.ObservedFiles);
        Assert.Equal(before.Values.Sum(b => (long)b.Length), inventory.ObservedBytes);
        Assert.Equal(Enum.GetValues<StorageFileKind>().Where(k => k != StorageFileKind.Unknown).Order(), inventory.Entries.Select(e => e.Kind).Order());
        Assert.Equal(42, inventory.Health.PreparedHighWaterRevision);
        Assert.True(inventory.Health.HasValidatedMain); Assert.True(inventory.Health.RecoverySelectionProven);
        Assert.Equal(1, inventory.Health.IndependentBackupGenerations);
        string json = JsonSerializer.Serialize(inventory);
        foreach (string secret in new[] { owned.Root, "Private name", "example.test", "payload", "sha256", "workspace.json" })
        { Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase); }
        Assert.DoesNotContain(owned.Root, string.Join(',', diagnostics.Events));
        Assert.All(inventory.Entries, e => { Assert.False(e.CanDelete); Assert.Equal(0, e.EstimatedReclaimedBytes); });
        Unchanged(owned, before);
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("prepared")]
    [InlineData("temp")]
    [InlineData("evidence")]
    public void DamagedHistoryFailsClosedAndExactEvidenceIsPreserved(string prefix)
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        Add(owned, prefix, "private corrupt evidence"u8.ToArray());
        var before = State(owned);
        var inventory = store.Inventory();
        Assert.False(inventory.IsComplete); Assert.False(inventory.Health.RecoverySelectionProven);
        Assert.Contains(inventory.Entries, e => e.Validation == StorageValidation.Corrupt);
        var plan = store.DryRun(); Assert.False(plan.IsReviewable);
        Assert.NotEqual(StorageError.None, store.ValidatePlan(plan).Error);
        Assert.Equal(StorageError.MaintenanceDisabled, store.Apply(plan, true).Error);
        Assert.NotEqual(StorageError.None, store.PlanBackupExport().Error);
        Unchanged(owned, before);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("kind")]
    [InlineData("field")]
    [InlineData("partial")]
    public void UnsupportedAndReadOnlySnapshotsCannotAuthorizeMaintenanceOrExport(string mutation)
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var bytes = Samples.Mutate(r =>
        {
            if (mutation == "future") { r["formatVersion"] = 99; }
            else if (mutation == "kind") { r["payload"]!["items"]![0]!["kind"] = "unknown"; }
            else if (mutation == "field") { r["payload"]!["unknown"] = "private"; }
            else { r["payload"]!["spaces"]![0]!["itemIds"] = 1; }
        });
        Add(owned, "evidence", bytes);
        var before = State(owned);
        var inventory = store.Inventory(); Assert.False(inventory.IsComplete);
        Assert.Contains(inventory.Entries, e => e.Validation is StorageValidation.ReadOnlySnapshot or StorageValidation.UnsupportedVersion);
        Assert.False(store.DryRun().IsReviewable); Assert.NotEqual(StorageError.None, store.PlanBackupExport().Error);
        Unchanged(owned, before);
    }

    [Theory]
    [InlineData("unowned-private-name.json")]
    [InlineData("backup-not-a-guid.json")]
    [InlineData("prepared-00000000000000000000000000000000.JSON")]
    public void NamesDoNotProveOwnershipAndAreNeverPublished(string name)
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        File.WriteAllText(Path.Combine(owned.Root, name), "private unrelated data");
        var before = State(owned);
        var inventory = store.Inventory(); Assert.False(inventory.IsComplete);
        Assert.Contains(inventory.Entries, e => e.Kind == StorageFileKind.Unknown && e.RetainBecause == RetentionReason.UnknownOwnership);
        Assert.DoesNotContain(name, JsonSerializer.Serialize(inventory));
        Assert.All(inventory.Health.Quotas, q =>
        { Assert.Equal(QuotaPressure.Unknown, q.Pressure); Assert.Equal(0, q.RemainingFiles); Assert.Equal(0, q.RemainingScanBytes); });
        Assert.False(store.DryRun().IsReviewable); Unchanged(owned, before);
    }

    [Theory]
    [InlineData(254, QuotaPressure.ApproachingLimit)]
    [InlineData(256, QuotaPressure.AtLimit)]
    [InlineData(257, QuotaPressure.Exceeded)]
    public void QuotaPressureNeverDeletesToMakeRoom(int count, QuotaPressure pressure)
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        byte[] bytes = WorkspaceCodec.Encode(Samples.Full());
        for (int i = 1; i < count; i++) { Add(owned, "prepared", bytes); }
        var before = State(owned);
        var inventory = store.Inventory();
        var quota = Assert.Single(inventory.Health.Quotas, q => q.Kind == StorageFileKind.Prepared);
        Assert.Equal(count, quota.ObservedFiles); Assert.Equal(pressure, quota.Pressure);
        Assert.Equal(Math.Max(0, 256 - count), quota.RemainingFiles);
        Assert.True(inventory.Health.NextSaveMayHitQuota);
        Assert.Equal(count <= 256, inventory.IsComplete);
        if (count >= 256) { Assert.Equal(StorageError.TooLarge, store.Save(Samples.Full().RenameSpace(Samples.SpaceA, "Next")).Error); }
        // Save may archive a predecessor before refusing reservation; retain every original asset.
        foreach (var (name, value) in before) { Assert.Equal(value, File.ReadAllBytes(Path.Combine(owned.Root, name))); }
        var frozen = State(owned); store.DryRun(); store.Apply(store.DryRun(), true); Unchanged(owned, frozen);
    }

    [Fact]
    public void InventoryScanBudgetIsBoundedEvenAcrossDifferentFamilies()
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        for (int i = 0; i < 9; i++)
        {
            string path = Add(owned, i % 2 == 0 ? "temp" : "evidence", []);
            using var stream = File.OpenWrite(path); stream.SetLength(PersistenceLimits.MaxFileBytes);
        }
        var before = State(owned);
        var inventory = store.Inventory(); Assert.False(inventory.IsComplete);
        Assert.Contains(inventory.Entries, e => e.Error == StorageError.TooLarge);
        Assert.InRange(inventory.Health.RemainingInventoryScanBytes, 0, PersistenceLimits.MaxRecoveryScanBytes);
        Assert.False(store.DryRun().IsReviewable); Unchanged(owned, before);
    }

    [Fact]
    public void ExcessEntriesBoundEnumerationWithoutClaimingCompleteTotals()
    {
        using var owned = new OwnedDirectory();
        for (int i = 0; i < 1030; i++) { File.WriteAllText(Path.Combine(owned.Root, "unknown-" + i), ""); }
        var inventory = owned.Store().Inventory();
        Assert.Equal(StorageError.TooLarge, inventory.Error); Assert.False(inventory.IsComplete); Assert.False(inventory.TotalsComplete);
        Assert.Equal(1026, inventory.ObservedFiles);
        Assert.All(inventory.Health.Quotas, q => Assert.Equal(QuotaPressure.Unknown, q.Pressure));
        Assert.Equal(1031, Directory.GetFiles(owned.Root).Length); // coordination lock only
    }

    [Fact]
    public void DryRunRetainsMainAllGenerationsReservationsAndUnfinishedEvidence()
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store(); var first = Samples.Full(); Assert.True(store.Save(first).Succeeded);
        var next = first.RenameSpace(Samples.SpaceA, "Next"); Assert.True(store.Save(next).Succeeded); Assert.True(store.Backup().Succeeded);
        Add(owned, "temp", WorkspaceCodec.Encode(next)); Add(owned, "evidence", WorkspaceCodec.Encode(first));
        var before = State(owned); var plan = store.DryRun();
        Assert.True(plan.IsReviewable); Assert.False(plan.ApplyEnabled); Assert.Equal(0, plan.EstimatedReclaimedBytes);
        Assert.Equal(2, plan.Inventory.Health.IndependentBackupGenerations);
        Assert.Equal(StorageError.None, store.ValidatePlan(plan).Error);
        Assert.Equal(StorageError.AuthorizationRequired, store.Apply(plan, false).Error);
        Assert.Equal(StorageError.MaintenanceDisabled, store.Apply(plan, true).Error);
        Assert.Equal(StorageError.MaintenanceDisabled, store.Apply(plan, true).Error);
        Unchanged(owned, before);
        Assert.Equal(StorageError.StaleRevision, store.Save(first).Error);
        var conflict = first.RenameSpace(Samples.SpaceA, "Conflict");
        Assert.Equal(StorageError.RevisionConflict, store.Save(conflict).Error);
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("content")]
    [InlineData("rename")]
    public void PlansRejectChangesInsteadOfTrustingOldFilenames(string mutation)
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var plan = store.DryRun(); var export = store.PlanBackupExport();
        string backup = Directory.GetFiles(owned.Root, "backup-*.json").Single();
        if (mutation == "add") { Assert.True(store.Backup().Succeeded); }
        else if (mutation == "remove") { File.Delete(backup); }
        else if (mutation == "rename") { File.Move(backup, Path.Combine(owned.Root, "backup-" + Guid.NewGuid().ToString("N") + ".json")); }
        else { File.WriteAllBytes(backup, WorkspaceCodec.Encode(Samples.Full().RenameSpace(Samples.SpaceA, "New"))); }
        var before = State(owned);
        Assert.Equal(StorageError.StalePlan, store.ValidatePlan(plan).Error);
        Assert.Equal(StorageError.StalePlan, store.ValidateExport(export).Error); Unchanged(owned, before);
    }

    [Fact]
    public void PlanIsBoundToItsStoreAndCannotBeReplayedAfterRestart()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var plan = store.DryRun(); var export = store.PlanBackupExport();
        Assert.Equal(StorageError.StalePlan, owned.Store().ValidatePlan(plan).Error);
        Assert.Equal(StorageError.StalePlan, owned.Store().ValidateExport(export).Error);
        Assert.Equal(StorageError.None, store.ValidatePlan(plan).Error);
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("prepared")]
    public void EqualRevisionDifferentContentsInHistoryBlockPlanning(string prefix)
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var changed = Samples.Full().RenameSpace(Samples.SpaceA, "conflict");
        var sameRevision = Workspace.FromSnapshot(changed.Id, 1, 42, changed.ItemStore, changed.Spaces.Values, changed.RemovedSpaces.Values);
        Add(owned, prefix, WorkspaceCodec.Encode(sameRevision)); var before = State(owned);
        Assert.Equal(StorageError.RevisionConflict, store.Inventory().Error);
        Assert.Equal(StorageError.RevisionConflict, store.DryRun().Error);
        Assert.Equal(StorageError.RevisionConflict, store.PlanBackupExport().Error); Unchanged(owned, before);
    }

    [Fact]
    public void ForeignWorkspaceHistoryBlocksPlanningWithoutLeakingItsIdentity()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); var first = Samples.Full(); Assert.True(store.Save(first).Succeeded);
        var foreign = Workspace.FromSnapshot(Guid.NewGuid(), 1, 90, first.ItemStore, first.Spaces.Values);
        Add(owned, "backup", WorkspaceCodec.Encode(foreign));
        var inventory = store.Inventory(); Assert.Equal(StorageError.WorkspaceConflict, inventory.Error);
        Assert.DoesNotContain(foreign.Id.ToString(), JsonSerializer.Serialize(inventory));
    }

    [Fact]
    public void ExportPlanValidatesHighestBackupExactHashAndNeverWritesOrMoves()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var next = Samples.Full().RenameSpace(Samples.SpaceA, "Next"); Assert.True(store.Save(next).Succeeded); Assert.True(store.Backup().Succeeded);
        var before = State(owned); var plan = store.PlanBackupExport();
        Assert.Equal(StorageError.None, plan.Error); Assert.Equal(next.Revision, plan.SourceRevision);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(WorkspaceCodec.Encode(next))), plan.Sha256);
        Assert.Equal(1, plan.FormatVersion); Assert.Equal(1, plan.DomainSchemaVersion); Assert.False(plan.ExternalWriteEnabled);
        Assert.Equal(StorageError.None, store.ValidateExport(plan).Error);
        Assert.Equal(StorageError.AuthorizationRequired, store.Export(plan, false).Error);
        Assert.Equal(StorageError.MaintenanceDisabled, store.Export(plan, true).Error);
        Unchanged(owned, before);
    }

    [Fact]
    public void MissingMainOrLastRecoverySnapshotRefusesRetentionPlan()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store();
        Assert.Equal(StorageError.RecoveryRequired, store.DryRun().Error);
        Assert.Equal(StorageError.RecoveryRequired, store.PlanBackupExport().Error);
        Assert.True(store.Save(Samples.Full()).Succeeded);
        foreach (var backup in Directory.GetFiles(owned.Root, "backup-*.json")) { File.Delete(backup); }
        Assert.Equal(StorageError.RecoveryRequired, store.DryRun().Error);
        Assert.Equal(StorageError.RecoveryRequired, store.PlanBackupExport().Error);
    }

    [Fact]
    public void MissingRootInspectionNeverCreatesDirectories()
    {
        using var owned = new OwnedDirectory(); Directory.Delete(owned.Root);
        Assert.False(owned.Store().Inventory().IsComplete); Assert.False(Directory.Exists(owned.Root));
        Directory.CreateDirectory(owned.Root);
    }

    [Fact]
    public void RecoveryThenMaintenanceKeepsReservationFloorAndCorruptEvidence()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); var first = Samples.Full(); Assert.True(store.Save(first).Succeeded);
        var newer = first.RenameSpace(Samples.SpaceA, "Next"); Assert.True(store.Save(newer).Succeeded);
        File.WriteAllText(owned.Main, "unique corrupt original"); var recovered = store.Restore();
        Assert.True(recovered.Workspace!.Revision > newer.Revision);
        var before = State(owned); var inventory = store.Inventory();
        Assert.False(inventory.IsComplete); Assert.Equal(recovered.Workspace.Revision, inventory.Health.PreparedHighWaterRevision);
        store.Apply(store.DryRun(), true); Unchanged(owned, before);
        Assert.Equal(StorageError.StaleRevision, store.Save(newer).Error);
        Assert.Contains(before.Values, b => b.AsSpan().SequenceEqual("unique corrupt original"u8));
    }

    [Theory]
    [InlineData(StorageStage.BeforeTempWrite)]
    [InlineData(StorageStage.BeforeFlush)]
    [InlineData(StorageStage.BeforeReplace)]
    public void SaveFaultsAndSimulatedFullDiskRemainRecoverableAfterMaintenanceRefusal(StorageStage stage)
    {
        using var owned = new OwnedDirectory(); var first = Samples.Full(); Assert.True(owned.Store().Save(first).Succeeded);
        var next = first.RenameSpace(Samples.SpaceA, "Next");
        Assert.Equal(StorageError.DiskFull, owned.Store(new Fault(stage, true)).Save(next).Error);
        var store = owned.Store(); var before = State(owned);
        store.Inventory(); store.Apply(store.DryRun(), true); store.Export(store.PlanBackupExport(), true); Unchanged(owned, before);
        Assert.Equal(42, store.Load().Workspace!.Revision); Assert.True(store.Save(next).Succeeded);
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("prepared")]
    [InlineData("temp")]
    [InlineData("evidence")]
    public void LinkedInventoryEntriesNeverReadOrModifyOutsideTargets(string prefix)
    {
        using var owned = new OwnedDirectory(); using var outside = new OwnedDirectory();
        var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        string source = Path.Combine(outside.Root, "private-source"); File.WriteAllText(source, "external exact bytes");
        string link = Path.Combine(owned.Root, prefix + "-" + Guid.NewGuid().ToString("N") + ".json"); File.CreateSymbolicLink(link, source);
        try
        {
            Assert.Equal(StorageError.UnsafePath, store.Inventory().Error); Assert.False(store.DryRun().IsReviewable);
            Assert.Equal("external exact bytes", File.ReadAllText(source)); Assert.Single(Directory.GetFiles(outside.Root));
        }
        finally { File.Delete(link); }
    }

    [Fact]
    public async Task ActualSecondProcessLockBlocksInventoryAndPlanValidation()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var plan = store.DryRun(); var export = store.PlanBackupExport(); var before = State(owned);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lock-fixture", "Zhuomian.Persistence.LockFixture.dll")); start.ArgumentList.Add(owned.Root);
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("locked", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal(StorageError.Busy, store.Inventory().Error); Assert.Equal(StorageError.Busy, store.DryRun().Error);
            Assert.Equal(StorageError.Busy, store.ValidatePlan(plan).Error); Assert.Equal(StorageError.Busy, store.ValidateExport(export).Error);
            Assert.Equal(StorageError.Busy, store.Save(Samples.Full()).Error); Assert.Equal(StorageError.Busy, store.Restore().Error);
            Assert.Equal(StorageError.MaintenanceDisabled, store.Apply(plan, true).Error);
            await child.StandardInput.WriteLineAsync("release"); await child.StandardInput.FlushAsync();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(0, child.ExitCode);
        }
        finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
        Assert.Equal(StorageError.None, store.ValidatePlan(plan).Error); Unchanged(owned, before);
    }
    [Fact]
    public async Task MixedConcurrentSaveRestoreInventoryAndDisabledApplyPreserveRecovery()
    {
        using var owned = new OwnedDirectory(); var first = Samples.Full(); Assert.True(owned.Store().Save(first).Succeeded);
        var plan = owned.Store().DryRun();
        var tasks = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var store = owned.Store();
            return (i % 4) switch
            {
                0 => store.Save(first.RenameSpace(Samples.SpaceA, "Writer " + i)).Error,
                1 => store.Restore().Error,
                2 => store.Inventory().Error,
                _ => store.Apply(plan, true).Error,
            };
        }));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, error => Assert.Contains(error, new[] { StorageError.None, StorageError.Busy,
            StorageError.StaleRevision, StorageError.RevisionConflict, StorageError.MaintenanceDisabled }));
        var inventory = owned.Store().Inventory(); Assert.True(inventory.IsComplete);
        Assert.True(inventory.Health.HasValidatedMain); Assert.True(inventory.Health.RecoverySelectionProven);
        Assert.True(inventory.Health.PreparedHighWaterRevision >= 43);
        Assert.Equal(StorageError.StaleRevision, owned.Store().Save(first).Error);
    }

    [Fact]
    public async Task KilledLockOwnerReleasesMutexWithoutChangingRecoveryAssets()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var plan = store.DryRun(); var before = State(owned);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lock-fixture", "Zhuomian.Persistence.LockFixture.dll")); start.ArgumentList.Add(owned.Root);
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("locked", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal(StorageError.Busy, store.Inventory().Error);
            child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
        Assert.Equal(StorageError.None, store.ValidatePlan(plan).Error); Unchanged(owned, before);
        Assert.True(owned.Store().Inventory().IsComplete); Assert.Equal(42, owned.Store().Load().Workspace!.Revision);
    }

}
