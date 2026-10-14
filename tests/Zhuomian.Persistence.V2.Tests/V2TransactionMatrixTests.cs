using System.Text.Json;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2TransactionMatrixTests
{
    private static readonly JsonSerializerOptions EvidenceOptions = new() { WriteIndented = true };
    public static IEnumerable<object[]> Cases()
    {
        foreach (string operation in new[] { "Initialize", "Save", "Restore" })
        {
            foreach (int write in new[] { 1, 2, 3 })
            {
                foreach (var stage in new[] { V2Stage.BeforeWrite, V2Stage.AfterCreate, V2Stage.DuringWrite,
                    V2Stage.BeforeFlush, V2Stage.AfterFlush, V2Stage.BeforeReadback, V2Stage.AfterReadback })
                { yield return [operation, stage, write]; }
            }
            foreach (var stage in new[] { V2Stage.BeforeSegment, V2Stage.AfterSegment,
                V2Stage.BeforeReservation, V2Stage.AfterReservation, V2Stage.BeforeMain, V2Stage.AfterMain })
            { yield return [operation, stage, 0]; }
        }
    }

    internal static Workspace Snapshot(long revision, bool different = false)
    {
        var source = Samples.Full();
        source = source.RenameSpace(Samples.SpaceA, different ? "conflicting" : "generated-" + revision);
        return Workspace.FromSnapshot(source.Id, source.SchemaVersion, revision, source.ItemStore,
            source.Spaces.Values, source.RemovedSpaces.Values);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void EveryWriteAndCommitCutPreservesEvidenceAndReservationFloor(string operation, V2Stage stage, int write)
    {
        using var root = new OwnedDirectory();
        using var setup = WorkspaceV2Store.ForTests(root.Root);
        if (operation != "Initialize") { Assert.True(setup.InitializeNew(Snapshot(1)).Succeeded); }
        var original = Evidence(root);
        var cut = new Cut(stage, write);
        using var store = WorkspaceV2Store.ForTests(root.Root, cut);
        StorageResult? saved = null;
        RecoveryResult? restored = null;
        if (operation == "Initialize") { saved = store.InitializeNew(Snapshot(10)); }
        else if (operation == "Save") { saved = store.Save(Snapshot(10)); }
        else { restored = store.RestoreRevision(1); }
        Assert.True(cut.Triggered);
        bool committed = stage == V2Stage.AfterMain;
        Assert.Equal(committed, saved?.Succeeded ?? restored!.Error == StorageError.None);
        if (saved is not null) { Assert.Equal(committed ? CommitState.Committed : CommitState.NotCommitted, saved.Value.Commit); }
        AssertRetained(root, original);
        long next = operation == "Restore" ? 2 : 10;
        bool reserved = committed || stage is V2Stage.AfterReservation or V2Stage.BeforeMain || write == 3;
        using var restarted = WorkspaceV2Store.ForTests(root.Root);
        var loaded = restarted.Load();
        long? readable = committed ? next : operation == "Initialize" ? null : 1;
        Assert.Equal(readable, loaded.Workspace?.Revision);
        for (int restart = 0; restart < 2; restart++)
        {
            using var repeated = WorkspaceV2Store.ForTests(root.Root);
            Assert.Equal(readable, repeated.Load().Workspace?.Revision);
        }
        var diskAtCut = Directory.GetFiles(root.Root).GroupBy(p => Path.GetFileName(p).Split('-')[0])
            .Select(g => new { family = g.Key, files = g.Count(), logicalBytes = g.Sum(p => new FileInfo(p).Length) }).ToArray();
        bool canWrite = loaded.Error == StorageError.None || (operation == "Initialize" && reserved);
        if (canWrite)
        {
            if (reserved)
            {
                Assert.Equal(StorageError.RevisionConflict, restarted.Save(Snapshot(next, different: true)).Error);
                if (operation != "Initialize")
                { Assert.Equal(StorageError.StaleRevision, restarted.Save(Snapshot(1)).Error); }
            }
            var retry = operation == "Restore" ? SnapshotFromSource(next) : Snapshot(next);
            Assert.True(restarted.Save(retry).Succeeded);
            Assert.Equal(next, restarted.Load().Workspace!.Revision);
        }
        else
        {
            Assert.False(restarted.Save(Snapshot(next)).Succeeded);
            // BeforeWrite creates only the empty lock; explicit new initialization is still safe.
            Assert.Equal(stage == V2Stage.BeforeWrite && write == 1,
                restarted.InitializeNew(Snapshot(next)).Succeeded);
        }
        WriteEvidence($"matrix-{operation}-{stage}-{write}.json", new
        {
            operation,
            stage = stage.ToString(),
            write,
            fault = "deterministic IOException (not power loss)",
            filesAtCut = diskAtCut,
            readableRevision = readable,
            trustedReservationRevision = reserved ? next : operation == "Initialize" ? (long?)null : 1,
            canWrite,
            requiresReadOnly = !canWrite,
            originalEvidenceRetained = true,
        });
    }

    private static Workspace SnapshotFromSource(long revision)
    {
        var source = Snapshot(1);
        return Workspace.FromSnapshot(source.Id, source.SchemaVersion, revision, source.ItemStore,
            source.Spaces.Values, source.RemovedSpaces.Values);
    }

    private sealed class Cut(V2Stage target, int write) : IV2FaultInjector
    {
        private int _writes;
        internal bool Triggered { get; private set; }
        public void Check(V2Stage stage)
        {
            if (stage == V2Stage.BeforeWrite) { _writes++; }
            if (stage == target && (write == 0 || _writes == write))
            { Triggered = true; throw new IOException("generated fault"); }
        }
    }

    internal static Dictionary<string, byte[]> Evidence(OwnedDirectory root) => Directory.GetFiles(root.Root)
        .Where(p => Path.GetFileName(p) is not "workspace.lock" and not "state-v2.bin")
        .ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes, StringComparer.Ordinal);

    internal static void AssertRetained(OwnedDirectory root, Dictionary<string, byte[]> original)
    {
        foreach (var file in original)
        { Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(root.Root, file.Key))); }
    }

    internal static void WriteEvidence(string name, object value)
    {
        string? directory = Environment.GetEnvironmentVariable("E3_V2_EVIDENCE_DIR");
        if (string.IsNullOrEmpty(directory)) { return; }
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), JsonSerializer.Serialize(value, EvidenceOptions));
    }
}
