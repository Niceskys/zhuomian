using System.Security.Cryptography;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2WitnessTests
{
    [Theory]
    [InlineData("rollback")]
    [InlineData("foreign")]
    [InlineData("future")]
    [InlineData("head")]
    [InlineData("selection")]
    [InlineData("corruption")]
    [InlineData("directory")]
    public void ValidCurrentRootDoesNotOverrideContradictoryOrUnreadableAcceptedEvidence(string kind)
    {
        using var root = new OwnedDirectory(); using var store = WorkspaceV2Store.ForTests(root.Root);
        Assert.True(store.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded);
        byte[] old = File.ReadAllBytes(Path.Combine(root.Root, "state-v2.bin"));
        Assert.True(store.Save(V2TransactionMatrixTests.Snapshot(100)).Succeeded);
        string witness = Directory.GetFiles(root.Root, "root-*.bin")[0];
        if (kind == "rollback") { File.WriteAllBytes(Path.Combine(root.Root, "state-v2.bin"), old); }
        else if (kind == "directory") { Directory.CreateDirectory(Path.Combine(root.Root, "root-generated.bin")); }
        else
        {
            byte[] bytes = File.ReadAllBytes(witness);
            if (kind == "foreign") { Guid.NewGuid().ToByteArray().CopyTo(bytes, 4); }
            if (kind == "future") { BitConverter.GetBytes(99).CopyTo(bytes, 0); }
            if (kind == "head") { bytes[28] ^= 1; }
            if (kind == "selection") { BitConverter.GetBytes(999L).CopyTo(bytes, 60); }
            SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)).CopyTo(bytes, bytes.Length - 32);
            if (kind == "corruption") { bytes[^1] ^= 1; }
            File.WriteAllBytes(witness, bytes);
        }
        var before = V2TransactionMatrixTests.Evidence(root);
        using var restarted = WorkspaceV2Store.ForTests(root.Root);
        Assert.Null(restarted.Load().Workspace);
        Assert.False(restarted.Save(V2TransactionMatrixTests.Snapshot(101)).Succeeded);
        Assert.NotEqual(StorageError.None, restarted.RestoreRevision(1).Error);
        V2TransactionMatrixTests.AssertRetained(root, before);
    }

    [Fact]
    public void ADeletedRedundantWitnessDoesNotDeleteACommitmentFromTheVerifiedRootGraph()
    {
        using var root = new OwnedDirectory(); using var store = WorkspaceV2Store.ForTests(root.Root);
        Assert.True(store.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded);
        Assert.True(store.Save(V2TransactionMatrixTests.Snapshot(100)).Succeeded);
        // Test damage only; production never deletes evidence. The current graph still binds both pairs.
        File.Delete(Directory.GetFiles(root.Root, "root-*.bin")[0]);
        Assert.Equal(100, store.Load().Workspace!.Revision);
        Assert.Equal(StorageError.StaleRevision, store.Save(V2TransactionMatrixTests.Snapshot(1, different: true)).Error);
        Assert.Equal(StorageError.RevisionConflict, store.Save(V2TransactionMatrixTests.Snapshot(100, different: true)).Error);
    }
}
