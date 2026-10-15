using System.Security.Cryptography;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2AuthorityLossTests
{
    [Theory]
    [InlineData("missing", V2Stage.BeforeReservation)]
    [InlineData("missing", V2Stage.AfterReservation)]
    [InlineData("missing", V2Stage.AfterMain)]
    [InlineData("corrupt", V2Stage.BeforeReservation)]
    [InlineData("corrupt", V2Stage.AfterReservation)]
    [InlineData("corrupt", V2Stage.AfterMain)]
    [InlineData("future", V2Stage.AfterReservation)]
    public void RootLossNeverSelectsAnOlderWitnessOrDropsPotentialReservations(string damage, V2Stage cut)
    {
        using var root = new OwnedDirectory();
        using var healthy = WorkspaceV2Store.ForTests(root.Root);
        Assert.True(healthy.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded);
        using (var transaction = WorkspaceV2Store.ForTests(root.Root, new Cut(cut)))
        { transaction.Save(V2TransactionMatrixTests.Snapshot(100)); }
        string state = Path.Combine(root.Root, "state-v2.bin");
        if (damage == "missing") { File.Delete(state); }
        else
        {
            byte[] bytes = File.ReadAllBytes(state); bytes[0] = 99;
            if (damage == "future") { SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32); }
            File.WriteAllBytes(state, bytes);
        }
        var evidence = Directory.GetFiles(root.Root).Where(p => Path.GetFileName(p) != "workspace.lock")
            .ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes, StringComparer.Ordinal);
        Assert.NotEmpty(Directory.GetFiles(root.Root, "root-*.bin"));
        Assert.Equal(2, Directory.GetFiles(root.Root, "segment-*.bin").Length);
        for (int restart = 0; restart < 3; restart++)
        {
            using var restarted = WorkspaceV2Store.ForTests(root.Root);
            var loaded = restarted.Load();
            Assert.Null(loaded.Workspace); Assert.True(loaded.IsReadOnly);
            Assert.False(restarted.Save(V2TransactionMatrixTests.Snapshot(100, different: true)).Succeeded);
            Assert.False(restarted.Save(V2TransactionMatrixTests.Snapshot(101)).Succeeded);
            Assert.NotEqual(StorageError.None, restarted.RestoreRevision(1).Error);
            Assert.False(restarted.InitializeNew(V2TransactionMatrixTests.Snapshot(0)).Succeeded);
        }
        V2TransactionMatrixTests.AssertRetained(root, evidence);
        Assert.Equal(evidence.Count + 1, Directory.GetFiles(root.Root).Length);
        V2TransactionMatrixTests.WriteEvidence($"authority-{damage}-{cut}.json", new
        {
            damage,
            cut = cut.ToString(),
            readableRevision = (long?)null,
            trustedRevision = (long?)null,
            recoveryAuthorized = false,
            writesAuthorized = false,
            evidenceUnchanged = true,
            witnesses = Directory.GetFiles(root.Root, "root-*.bin").Length,
            pages = Directory.GetFiles(root.Root, "segment-*.bin").Length,
        });
    }

    [Theory]
    [InlineData("conflicting-content")]
    [InlineData("foreign-workspace")]
    [InlineData("missing-page")]
    [InlineData("corrupt-page")]
    public void AmbiguousOrIncompleteEvidenceIsRetainedAndNeverRepublished(string kind)
    {
        using var root = new OwnedDirectory(); using var other = new OwnedDirectory();
        using var store = WorkspaceV2Store.ForTests(root.Root);
        Assert.True(store.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded);
        // Multiple valid dependency branches do not say which replacement ever accepted them.
        using (var first = WorkspaceV2Store.ForTests(root.Root, new Cut(V2Stage.BeforeReservation)))
        { Assert.False(first.Save(V2TransactionMatrixTests.Snapshot(100)).Succeeded); }
        using (var second = WorkspaceV2Store.ForTests(root.Root, new Cut(V2Stage.AfterReservation)))
        { Assert.False(second.Save(V2TransactionMatrixTests.Snapshot(100, different: true)).Succeeded); }
        if (kind == "foreign-workspace")
        {
            var source = V2TransactionMatrixTests.Snapshot(100);
            var foreign = Zhuomian.Core.WorkspaceModel.Workspace.FromSnapshot(Guid.NewGuid(), source.SchemaVersion,
                source.Revision, source.ItemStore, source.Spaces.Values, source.RemovedSpaces.Values);
            using var foreignStore = WorkspaceV2Store.ForTests(other.Root);
            Assert.True(foreignStore.InitializeNew(foreign).Succeeded);
            foreach (string file in Directory.GetFiles(other.Root, "*.bin"))
            { File.Copy(file, Path.Combine(root.Root, Path.GetFileName(file) == "state-v2.bin" ? "root-foreign.bin" : Path.GetFileName(file))); }
        }
        if (kind is "missing-page" or "corrupt-page")
        {
            string page = Directory.GetFiles(root.Root, "segment-*.bin")[0];
            if (kind == "missing-page") { File.Delete(page); } else { File.WriteAllBytes(page, [1, 2, 3]); }
        }
        File.Delete(Path.Combine(root.Root, "state-v2.bin"));
        var original = V2TransactionMatrixTests.Evidence(root);
        Assert.Null(store.Load().Workspace);
        Assert.False(store.Save(V2TransactionMatrixTests.Snapshot(101)).Succeeded);
        Assert.NotEqual(StorageError.None, store.Restore().Error);
        V2TransactionMatrixTests.AssertRetained(root, original);
        Assert.Equal(original.Count + 1, Directory.GetFiles(root.Root).Length);
    }

    private sealed class Cut(V2Stage target) : IV2FaultInjector
    {
        public void Check(V2Stage stage) { if (stage == target) { throw new IOException("generated"); } }
    }
}
