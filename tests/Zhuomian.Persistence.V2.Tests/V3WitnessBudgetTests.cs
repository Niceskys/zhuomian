using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

[Collection("v2 read benchmark")]
public sealed class V3WitnessBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveReservesBothWitnessSlotsBeforeAcceptingAnotherRevision(bool recoverable)
    {
        using var root = new OwnedDirectory();
        using var store = recoverable ? WorkspaceV2Store.ForRecoverableTests(root.Root) : WorkspaceV2Store.ForTests(root.Root);
        Fill(root.Root, store, 32767);
        var evidence = Capture(root.Root);
        Assert.Equal(10, store.Load().Workspace!.Revision);
        var rejected = store.Save(V3AuthorityTests.Snapshot(20));
        Assert.Equal(StorageError.TooLarge, rejected.Error);
        Assert.Equal(CommitState.NotCommitted, rejected.Commit);
        AssertRetained(root.Root, evidence);
        Assert.Equal(10, store.Load().Workspace!.Revision);
        Assert.Equal(CommitState.Unchanged, store.Save(V3AuthorityTests.Snapshot(10)).Commit);
        Assert.Equal(StorageError.RevisionConflict, store.Save(V3AuthorityTests.Snapshot(10, true)).Error);
        var restored = store.RestoreRevisionTransaction(1);
        Assert.Equal(StorageError.TooLarge, restored.Transaction.Error);
        Assert.Equal(CommitState.NotCommitted, restored.Transaction.Commit);
        AssertRetained(root.Root, evidence);
    }

    [Fact]
    public void ExplicitRepairReservesAValidOldMainWitnessBeforePublication()
    {
        using var root = new OwnedDirectory();
        using var store = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Fill(root.Root, store, 32768);
        // A still proves revision 10. This valid previous B needs one new root witness.
        byte[] oldMain = Directory.GetFiles(root.Root, "root-*.bin").Select(File.ReadAllBytes)
            .First(bytes => BitConverter.ToInt64(bytes, 60) == 1);
        File.WriteAllBytes(Path.Combine(root.Root, "state-v2.bin"), oldMain);
        var evidence = Capture(root.Root);
        Assert.Equal(StorageError.RecoveryRequired, store.Load().Error);
        var rejected = store.RecoverAuthority();
        Assert.Equal(StorageError.TooLarge, rejected.Error);
        Assert.Equal(CommitState.NotCommitted, rejected.Commit);
        AssertRetained(root.Root, evidence);
        // Main absence does not add a root witness, so the same intact accepted
        // graph is repairable at the limit. Only generated test damage is removed.
        File.Delete(Path.Combine(root.Root, "state-v2.bin"));
        Assert.Equal(CommitState.Committed, store.RecoverAuthority().Commit);
        Assert.Equal(10, store.Load().Workspace!.Revision);
        Assert.Equal(CommitState.Unchanged, store.RecoverAuthority().Commit);
    }

    private static void Fill(string root, WorkspaceV2Store store, int witnesses)
    {
        Assert.True(store.InitializeNew(V3AuthorityTests.Snapshot(1)).Succeeded);
        Assert.True(store.Save(V3AuthorityTests.Snapshot(10)).Succeeded);
        var originals = Directory.GetFiles(root, "root-*.bin");
        // Repeated valid prior-generation evidence models retained replacement
        // evidence pressure, not extra accepted revisions or a capacity claim.
        byte[] prior = originals.Select(File.ReadAllBytes).First(bytes => BitConverter.ToInt64(bytes, 20) == 1);
        Assert.Equal(1, BitConverter.ToInt64(prior, 20));
        for (int i = originals.Length; i < witnesses; i++)
        { File.WriteAllBytes(Path.Combine(root, "root-generated-budget-" + i + ".bin"), prior); }
    }

    private static Dictionary<string, byte[]> Capture(string root) => Directory.GetFiles(root)
        .ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes);

    private static void AssertRetained(string root, Dictionary<string, byte[]> evidence)
    {
        Assert.Equal(evidence.Keys.Order(StringComparer.Ordinal), Directory.GetFiles(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        foreach (var pair in evidence) { Assert.Equal(pair.Value, File.ReadAllBytes(Path.Combine(root, pair.Key))); }
    }
}
