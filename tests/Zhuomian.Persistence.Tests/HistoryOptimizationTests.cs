using System.Diagnostics;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

public sealed class HistoryOptimizationTests
{
    [Fact]
    public void WarmStoreAndRestartBothObserveNewHistoricalCorruption()
    {
        using var owned = new OwnedDirectory();
        var store = owned.Store(); var first = Samples.Full();
        Assert.True(store.Save(first).Succeeded); Assert.Equal(StorageError.None, store.Load().Error);
        string prepared = Directory.GetFiles(owned.Root, "prepared-*.json").Single();
        File.WriteAllText(prepared, "broken after successful inspection");
        byte[] main = File.ReadAllBytes(owned.Main);
        foreach (var current in new[] { store, owned.Store() })
        {
            Assert.Equal(StorageError.RecoveryRequired, current.Save(first.RenameSpace(Samples.SpaceA, "Next")).Error);
            Assert.Equal(StorageError.RecoveryRequired, current.Backup().Error);
            Assert.Equal(StorageError.RecoveryRequired, current.Restore().Error);
            Assert.Equal(StorageError.RecoveryRequired, current.Load().Error);
            Assert.True(current.Load().IsReadOnly); Assert.False(current.Inventory().IsComplete);
        }
        Assert.Equal(main, File.ReadAllBytes(owned.Main));
        Assert.Equal("broken after successful inspection", File.ReadAllText(prepared));
    }

    [Fact]
    public void EqualLengthAndRestoredTimestampCannotHideNewRevisionConflict()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); var first = Samples.Full();
        Assert.True(store.Save(first).Succeeded); Assert.Equal(StorageError.None, store.Load().Error);
        string prepared = Directory.GetFiles(owned.Root, "prepared-*.json").Single();
        byte[] original = File.ReadAllBytes(prepared); var timestamp = File.GetLastWriteTimeUtc(prepared);
        var different = Workspace.FromSnapshot(first.Id, 1, first.Revision, first.ItemStore,
            first.RenameSpace(Samples.SpaceA, "Z").Spaces.Values, first.RemovedSpaces.Values);
        byte[] changed = WorkspaceCodec.Encode(different); Assert.Equal(original.Length, changed.Length);
        File.WriteAllBytes(prepared, changed); File.SetLastWriteTimeUtc(prepared, timestamp);
        Assert.Equal(StorageError.RevisionConflict, store.Save(first.RenameSpace(Samples.SpaceA, "Next")).Error);
        Assert.Equal(StorageError.RevisionConflict, store.Load().Error);
        Assert.Equal(changed, File.ReadAllBytes(prepared)); Assert.Equal(first.Revision, store.Load().Workspace!.Revision);
    }

    [Fact]
    public void WarmStoreRejectsForeignSameRevisionReplacement()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); var first = Samples.Full();
        Assert.True(store.Save(first).Succeeded); Assert.Equal(StorageError.None, store.Load().Error);
        string prepared = Directory.GetFiles(owned.Root, "prepared-*.json").Single();
        string replacement = Path.Combine(owned.Root, "replacement");
        File.WriteAllBytes(replacement, WorkspaceCodec.Encode(Workspace.FromSnapshot(Guid.NewGuid(), 1, first.Revision,
            first.ItemStore, first.Spaces.Values, first.RemovedSpaces.Values)));
        File.Replace(replacement, prepared, null);
        Assert.Equal(StorageError.WorkspaceConflict, store.Save(first.RenameSpace(Samples.SpaceA, "Next")).Error);
        Assert.Equal(StorageError.WorkspaceConflict, store.Backup().Error);
        Assert.Equal(StorageError.WorkspaceConflict, store.Restore().Error);
    }

    [Fact]
    public void RestoreRereadDetectsCorruptionInsideOneLockedOperation()
    {
        using var owned = new OwnedDirectory(); Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        string backup = Directory.GetFiles(owned.Root, "backup-*.json").Single(); byte[] main = File.ReadAllBytes(owned.Main);
        var fault = new CorruptOnReread(backup);
        Assert.Equal(StorageError.RecoveryRequired, owned.Store(fault).Restore().Error);
        Assert.True(fault.Fired); Assert.Equal(main, File.ReadAllBytes(owned.Main));
        Assert.Equal("corrupt between reads", File.ReadAllText(backup)); Assert.Empty(Directory.GetFiles(owned.Root, "evidence-*.json"));
    }

    [Fact]
    public async Task ActualSecondProcessChangesHistoryAfterWarmInspectionAndCannotBeIgnored()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); var first = Samples.Full();
        Assert.True(store.Save(first).Succeeded); Assert.Equal(StorageError.None, store.Load().Error);
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "lock-fixture", "Zhuomian.Persistence.LockFixture.dll"));
        start.ArgumentList.Add(owned.Root); start.ArgumentList.Add("corrupt-history");
        using var child = Process.Start(start)!;
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Assert.Equal(0, child.ExitCode);
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
        Assert.Equal(StorageError.RecoveryRequired, store.Save(first.RenameSpace(Samples.SpaceA, "Next")).Error);
        Assert.Equal(StorageError.RecoveryRequired, store.Backup().Error);
        Assert.Equal(StorageError.RecoveryRequired, store.Restore().Error);
        Assert.Equal(first.Revision, store.Load().Workspace!.Revision); Assert.True(store.Load().IsReadOnly);
    }

    [Fact]
    public void ActualWindowsExactDuplicateStillRequiresNativeSingleLinkValidation()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory(); using var outside = new OwnedDirectory();
        var store = owned.Store(); var first = Samples.Full(); Assert.True(store.Save(first).Succeeded);
        Assert.Equal(StorageError.None, store.Load().Error);
        string source = Path.Combine(outside.Root, "source"); byte[] bytes = WorkspaceCodec.Encode(first); File.WriteAllBytes(source, bytes);
        string prepared = Directory.GetFiles(owned.Root, "prepared-*.json").Single(); File.Delete(prepared);
        WindowsFixtureLinks.HardLink(prepared, source);
        Assert.Equal(StorageError.RecoveryRequired, store.Save(first.RenameSpace(Samples.SpaceA, "Next")).Error);
        Assert.Equal(StorageError.RecoveryRequired, store.Backup().Error);
        Assert.Equal(StorageError.RecoveryRequired, store.Restore().Error);
        Assert.Equal(StorageError.UnsafePath, store.Inventory().Error); Assert.Equal(bytes, File.ReadAllBytes(source));
    }

    private sealed class CorruptOnReread(string backup) : IStorageFaultInjector
    {
        private int _reads;
        internal bool Fired { get; private set; }
        public void Check(StorageStage stage)
        {
            // Restore reads main/backup, then independently rereads main/backup in history checks.
            if (stage == StorageStage.BeforeRead && ++_reads == 4)
            { File.WriteAllText(backup, "corrupt between reads"); Fired = true; }
        }
    }
}
