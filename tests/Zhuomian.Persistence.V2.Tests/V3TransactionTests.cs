using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V3TransactionTests
{
    [Theory]
    [InlineData("Initialize", V2Stage.DuringWrite, 2)]
    [InlineData("Initialize", V2Stage.BeforeFlush, 3)]
    [InlineData("Save", V2Stage.AfterCreate, 2)]
    [InlineData("Save", V2Stage.BeforeFlush, 2)]
    [InlineData("Save", V2Stage.AfterReadback, 2)]
    [InlineData("Save", V2Stage.DuringWrite, 3)]
    [InlineData("Save", V2Stage.BeforeReadback, 3)]
    [InlineData("Restore", V2Stage.BeforeReservation, 0)]
    [InlineData("Restore", V2Stage.AfterReservation, 0)]
    [InlineData("Restore", V2Stage.AfterMain, 0)]
    [InlineData("Recover", V2Stage.DuringWrite, 1)]
    [InlineData("Recover", V2Stage.AfterMain, 0)]
    public void SharedWriteSeamsRetainTheirMeaningInTheNewProtocol(string operation, V2Stage stage, int write)
    {
        using var root = new OwnedDirectory();
        if (operation != "Initialize")
        {
            using var setup = WorkspaceV2Store.ForRecoverableTests(root.Root);
            Assert.True(setup.InitializeNew(V3AuthorityTests.Snapshot(1)).Succeeded);
            if (operation == "Recover")
            { Assert.True(setup.Save(V3AuthorityTests.Snapshot(10)).Succeeded); File.Delete(Path.Combine(root.Root, "state-v2.bin")); }
        }
        var fault = new Cut(stage, write);
        using (var store = WorkspaceV2Store.ForRecoverableTests(root.Root, fault))
        {
            var result = operation switch
            {
                "Initialize" => store.InitializeNew(V3AuthorityTests.Snapshot(10)),
                "Save" => store.Save(V3AuthorityTests.Snapshot(10)),
                "Restore" => store.RestoreRevisionTransaction(1).Transaction,
                _ => store.RecoverAuthority(),
            };
            Assert.True(fault.Triggered);
            Assert.Equal(stage == V2Stage.AfterMain, result.Succeeded);
        }
        long? floor = operation switch
        {
            "Recover" => 10,
            "Restore" => stage == V2Stage.BeforeReservation ? 1 : 2,
            "Initialize" => write == 3 ? 10 : null,
            _ => write == 3 ? 10 : 1,
        };
        using var restarted = WorkspaceV2Store.ForRecoverableTests(root.Root);
        var diskAtCut = Directory.GetFiles(root.Root).GroupBy(p => Path.GetFileName(p).Split('-')[0])
            .Select(g => new { family = g.Key, files = g.Count(), logicalBytes = g.Sum(p => new FileInfo(p).Length) }).ToArray();
        var observedBeforeRepair = restarted.Load();
        Assert.Equal(floor is not null, restarted.RecoverAuthority().Succeeded);
        Assert.Equal(floor, restarted.Load().Workspace?.Revision);
        if (floor is not null) { Assert.Equal(floor + 1, restarted.Restore().Workspace!.Revision); }
        V2TransactionMatrixTests.WriteEvidence($"v3-matrix-{operation}-{stage}-{write}.json", new
        {
            operation,
            stage = stage.ToString(),
            write,
            expectedReservation = floor,
            diskAtCut,
            beforeRepair = new { error = observedBeforeRepair.Error.ToString(), revision = observedBeforeRepair.Workspace?.Revision, readOnly = observedBeforeRepair.IsReadOnly },
            mayResumeAfterRepair = floor is not null,
            windowsNtfs = OperatingSystem.IsWindows(),
            physicalPowerLoss = false,
        });
    }

    [Fact]
    public void DetailedRestoreReportsUnknownWhenActualPostBReadIsObstructed()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var root = new OwnedDirectory();
        using (var setup = WorkspaceV2Store.ForRecoverableTests(root.Root)) { Assert.True(setup.InitializeNew(V3AuthorityTests.Snapshot(1)).Succeeded); }
        using (var fault = new Obstruct(Path.Combine(root.Root, "state-v2.bin")))
        using (var store = WorkspaceV2Store.ForRecoverableTests(root.Root, fault))
        { Assert.Equal(CommitState.Unknown, store.RestoreRevisionTransaction(1).Transaction.Commit); }
        using var restarted = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.Equal(2, restarted.Load().Workspace!.Revision);
    }

    private sealed class Cut(V2Stage target, int write) : IV2FaultInjector
    {
        private int _writes;
        internal bool Triggered { get; private set; }
        public void Check(V2Stage stage)
        {
            if (stage == V2Stage.BeforeWrite) { _writes++; }
            if (stage == target && (write == 0 || write == _writes))
            { Triggered = true; throw new IOException("generated fault"); }
        }
    }
    private sealed class Obstruct(string path) : IV2FaultInjector, IDisposable
    {
        private FileStream? _held;
        public void Check(V2Stage stage)
        {
            if (stage != V2Stage.AfterMain) { return; }
            _held = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new IOException("generated obstruction");
        }
        public void Dispose() => _held?.Dispose();
    }
}
