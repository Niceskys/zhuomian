using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2ReadbackMutationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WrongDependencyOrAuthorityBytesAreNeverAcceptedByPublicationOrRetry(int write)
    {
        using var root = new OwnedDirectory(); using var setup = WorkspaceV2Store.ForTests(root.Root);
        Assert.True(setup.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded);
        var original = V2TransactionMatrixTests.Evidence(root);
        using var faulted = WorkspaceV2Store.ForTests(root.Root, new Mutate(root.Root, write));
        var result = faulted.Save(V2TransactionMatrixTests.Snapshot(100));
        Assert.False(result.Succeeded);
        Assert.Equal(write == 1 ? CommitState.NotCommitted : CommitState.Unknown, result.Commit);
        V2TransactionMatrixTests.AssertRetained(root, original);
        using var restarted = WorkspaceV2Store.ForTests(root.Root);
        if (write == 1)
        {
            Assert.Equal(1, restarted.Load().Workspace!.Revision);
            // The corrupted orphan occupies this exact dependency name; it is never overwritten.
            Assert.Equal(StorageError.IntegrityFailure, restarted.Save(V2TransactionMatrixTests.Snapshot(100)).Error);
        }
        else
        {
            Assert.Null(restarted.Load().Workspace);
            Assert.False(restarted.Save(V2TransactionMatrixTests.Snapshot(100)).Succeeded);
            Assert.NotEqual(StorageError.None, restarted.Restore().Error);
        }
        V2TransactionMatrixTests.WriteEvidence($"native-bytes-write-{write}.json", new
        {
            write,
            windowsNtfs = OperatingSystem.IsWindows(),
            fixtureBytesActuallyChanged = true,
            result = result.Commit.ToString(),
            error = result.Error.ToString(),
            originalEvidenceRetained = true
        });
    }

    private sealed class Mutate(string root, int write) : IV2FaultInjector
    {
        private int _writes;
        public void Check(V2Stage stage)
        {
            if (stage == V2Stage.BeforeWrite) { _writes++; }
            if (stage != V2Stage.AfterReadback || _writes != write) { return; }
            string path = Directory.GetFiles(root, "stage-*.bin").Single();
            byte[] bytes = File.ReadAllBytes(path); bytes[^1] ^= 1;
            File.WriteAllBytes(path, bytes);
        }
    }
}
