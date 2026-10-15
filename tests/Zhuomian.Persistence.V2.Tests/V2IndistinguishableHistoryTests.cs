using System.Security.Cryptography;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2IndistinguishableHistoryTests
{
    [Fact]
    public void OppositeAcceptedPairsLeaveEquivalentEvidenceAfterAuthorityLoss()
    {
        using var first = new OwnedDirectory(); using var second = new OwnedDirectory();
        Build(first.Root, false); Build(second.Root, true);
        // Opaque random GUIDs can be coupled across executions. Compare exact bytes/families,
        // not maximum filename/time. Segment names themselves are exact content-addressed names.
        Assert.Equal(Observation(first.Root), Observation(second.Root));
        foreach (var root in new[] { first, second })
        {
            using var restarted = WorkspaceV2Store.ForTests(root.Root);
            Assert.Null(restarted.Load().Workspace);
            Assert.False(restarted.Save(V3AuthorityTests.Snapshot(10)).Succeeded);
            Assert.False(restarted.Save(V3AuthorityTests.Snapshot(10, true)).Succeeded);
            Assert.False(restarted.InitializeNew(V3AuthorityTests.Snapshot(0)).Succeeded);
        }
    }
    private static void Build(string root, bool acceptedDifferent)
    {
        using (var setup = WorkspaceV2Store.ForTests(root)) { Assert.True(setup.InitializeNew(V3AuthorityTests.Snapshot(1)).Succeeded); }
        foreach (bool different in new[] { false, true })
        {
            using var prepare = WorkspaceV2Store.ForTests(root, new Cut(V2Stage.BeforeReservation, 0));
            Assert.False(prepare.Save(V3AuthorityTests.Snapshot(10, different)).Succeeded);
        }
        using (var other = WorkspaceV2Store.ForTests(root, new Cut(V2Stage.BeforeWrite, 2)))
        { Assert.False(other.Save(V3AuthorityTests.Snapshot(10, !acceptedDifferent)).Succeeded); }
        using (var accepted = WorkspaceV2Store.ForTests(root, new Cut(V2Stage.AfterReservation, 0)))
        { Assert.False(accepted.Save(V3AuthorityTests.Snapshot(10, acceptedDifferent)).Succeeded); }
        using (var verify = WorkspaceV2Store.ForTests(root))
        { Assert.Equal(StorageError.RevisionConflict, verify.Save(V3AuthorityTests.Snapshot(10, !acceptedDifferent)).Error); }
        File.Delete(Path.Combine(root, "state-v2.bin"));
    }
    private static string[] Observation(string root) => Directory.GetFiles(root)
        .Select(p => Path.GetFileName(p).Split('-')[0] + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))
        .Order(StringComparer.Ordinal).ToArray();
    private sealed class Cut(V2Stage target, int write) : IV2FaultInjector
    {
        private int _writes;
        public void Check(V2Stage stage)
        {
            if (stage == V2Stage.BeforeWrite) { _writes++; }
            if (stage == target && (write == 0 || _writes == write)) { throw new IOException("generated fault"); }
        }
    }
}
