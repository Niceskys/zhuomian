using System.Security.Cryptography;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V3AuthorityTests
{
    internal static Workspace Snapshot(long revision, bool different = false)
    {
        var full = Samples.Full().RenameSpace(Samples.SpaceA, different ? "conflict" : "revision-" + revision);
        return Workspace.FromSnapshot(full.Id, full.SchemaVersion, revision, full.ItemStore,
            full.Spaces.Values, full.RemovedSpaces.Values);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SingleMainLossHasIndependentReservationProof(bool corrupt, bool pending)
    {
        using var root = new OwnedDirectory();
        using (var setup = WorkspaceV2Store.ForRecoverableTests(root.Root))
        { Assert.True(setup.InitializeNew(Snapshot(1)).Succeeded); }
        using (var save = WorkspaceV2Store.ForRecoverableTests(root.Root, pending ? new Cut(V2Stage.AfterReservation) : null))
        { Assert.Equal(!pending, save.Save(Snapshot(10)).Succeeded); }
        string state = Path.Combine(root.Root, "state-v2.bin");
        byte[] damaged = File.ReadAllBytes(state); damaged[^1] ^= 1;
        if (corrupt) { File.WriteAllBytes(state, damaged); } else { File.Delete(state); }
        byte[] accepted = File.ReadAllBytes(Path.Combine(root.Root, "authority-v3.bin"));
        using var restarted = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.Equal(StorageError.RecoveryRequired, restarted.Load().Error);
        Assert.False(restarted.Save(Snapshot(10, true)).Succeeded);
        Assert.Equal(CommitState.Committed, restarted.RecoverAuthority().Commit);
        Assert.Equal(10, restarted.Load().Workspace!.Revision);
        Assert.Equal(accepted, File.ReadAllBytes(Path.Combine(root.Root, "authority-v3.bin")));
        if (corrupt) { Assert.Contains(Directory.GetFiles(root.Root, "damage-*.bin"), p => File.ReadAllBytes(p).SequenceEqual(damaged)); }
        Assert.Equal(StorageError.RevisionConflict, restarted.Save(Snapshot(10, true)).Error);
        Assert.Equal(StorageError.StaleRevision, restarted.Save(Snapshot(9)).Error);
        Assert.Equal(CommitState.Unchanged, restarted.Save(Snapshot(10)).Commit);
        Assert.Equal(11, restarted.RestoreRevision(1).Workspace!.Revision);
        Assert.Equal("revision-1", restarted.Load().Workspace!.Spaces[Samples.SpaceA].Name);
        V2TransactionMatrixTests.WriteEvidence($"v3-recovery-{corrupt}-{pending}.json", new
        { windowsNtfs = OperatingSystem.IsWindows(), reservation = 10, recovered = 10, restored = 11, corrupt, pending, retained = true });
    }

    [Theory]
    [InlineData("authority-missing")]
    [InlineData("authority-corrupt")]
    [InlineData("page-missing")]
    [InlineData("page-corrupt")]
    [InlineData("foreign")]
    [InlineData("future")]
    [InlineData("conflict")]
    [InlineData("truncated-main")]
    [InlineData("extended-main")]
    public void IncompleteOrConflictingEvidenceCannotAuthorizeRepair(string fault)
    {
        using var root = new OwnedDirectory();
        using var store = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        Assert.True(store.Save(Snapshot(10)).Succeeded);
        string authority = Path.Combine(root.Root, "authority-v3.bin");
        string state = Path.Combine(root.Root, "state-v2.bin");
        if (fault == "authority-missing") { File.Delete(authority); }
        else if (fault.StartsWith("page-", StringComparison.Ordinal))
        {
            string page = Directory.GetFiles(root.Root, "segment-*.bin")[0];
            if (fault == "page-missing") { File.Delete(page); }
            else { var bytes = File.ReadAllBytes(page); bytes[^1] ^= 1; File.WriteAllBytes(page, bytes); }
        }
        else if (fault == "truncated-main") { File.WriteAllBytes(state, [3]); }
        else if (fault == "extended-main") { File.WriteAllBytes(state, [.. File.ReadAllBytes(state), 0]); }
        else
        {
            string path = fault == "conflict" ? state : authority;
            var bytes = File.ReadAllBytes(path);
            if (fault == "foreign") { bytes[4] ^= 1; }
            else if (fault == "future") { BitConverter.GetBytes(4).CopyTo(bytes, 0); }
            else { bytes[28] ^= 1; }
            if (fault != "authority-corrupt") { SHA256.HashData(bytes.AsSpan(0, 68)).CopyTo(bytes, 68); }
            File.WriteAllBytes(path, bytes);
        }
        var before = Directory.GetFiles(root.Root).ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes);
        Assert.False(store.RecoverAuthority().Succeeded);
        Assert.False(store.Save(Snapshot(20)).Succeeded);
        Assert.False(store.InitializeNew(Snapshot(20)).Succeeded);
        Assert.Null(store.Restore().Workspace);
        foreach (var file in before) { Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(root.Root, file.Key!))); }
    }

    [Fact]
    public void OldMainSubstitutionRequiresANewPublicationAndDoesNotRollbackTheFloor()
    {
        using var root = new OwnedDirectory(); using var store = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        string state = Path.Combine(root.Root, "state-v2.bin"); byte[] old = File.ReadAllBytes(state);
        Assert.True(store.Save(Snapshot(10)).Succeeded); File.WriteAllBytes(state, old);
        Assert.Equal(StorageError.RecoveryRequired, store.Load().Error);
        Assert.False(store.Save(Snapshot(2)).Succeeded);
        Assert.True(store.RecoverAuthority().Succeeded);
        Assert.Equal(10, store.Load().Workspace!.Revision);
    }

    [Theory]
    [InlineData(V2Stage.BeforeReservation)]
    [InlineData(V2Stage.AfterReservation)]
    [InlineData(V2Stage.BeforeMain)]
    [InlineData(V2Stage.AfterMain)]
    public void AAndBHaveIndependentExpectedHistories(V2Stage stage)
    {
        using var root = new OwnedDirectory();
        using (var setup = WorkspaceV2Store.ForRecoverableTests(root.Root)) { Assert.True(setup.InitializeNew(Snapshot(1)).Succeeded); }
        using (var faulted = WorkspaceV2Store.ForRecoverableTests(root.Root, new Cut(stage))) { faulted.Save(Snapshot(10)); }
        using var restarted = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.True(restarted.RecoverAuthority().Succeeded);
        long expected = stage == V2Stage.BeforeReservation ? 1 : 10;
        Assert.Equal(expected, restarted.Load().Workspace!.Revision);
        Assert.Equal(expected + 1, restarted.RestoreRevision(1).Workspace!.Revision);
    }

    [Fact]
    public void VersionsCannotUpgradeDowngradeOrMix()
    {
        using var oldRoot = new OwnedDirectory(); using var newRoot = new OwnedDirectory();
        using var old = WorkspaceV2Store.ForTests(oldRoot.Root);
        using var modern = WorkspaceV2Store.ForRecoverableTests(newRoot.Root);
        Assert.True(old.InitializeNew(Snapshot(1)).Succeeded);
        Assert.True(modern.InitializeNew(Snapshot(1)).Succeeded);
        using var wrongNew = WorkspaceV2Store.ForRecoverableTests(oldRoot.Root);
        using var wrongOld = WorkspaceV2Store.ForTests(newRoot.Root);
        Assert.False(wrongNew.InitializeNew(Snapshot(1)).Succeeded);
        Assert.Equal(StorageError.UnsupportedVersion, wrongNew.Load().Error);
        Assert.False(wrongNew.RecoverAuthority().Succeeded);
        Assert.False(wrongOld.Save(Snapshot(2)).Succeeded);
        Assert.Equal(StorageError.UnsupportedVersion, wrongOld.Load().Error);
        File.Copy(Path.Combine(newRoot.Root, "authority-v3.bin"), Path.Combine(oldRoot.Root, "authority-v3.bin"));
        Assert.Equal(StorageError.UnsupportedVersion, old.Load().Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthorityLinksCannotAuthorizeWrites(bool hardLink)
    {
        if (hardLink && !OperatingSystem.IsWindows()) { return; }
        using var root = new OwnedDirectory(); using var outside = new OwnedDirectory();
        using var store = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        string authority = Path.Combine(root.Root, "authority-v3.bin");
        byte[] bytes = File.ReadAllBytes(authority);
        string target = Path.Combine(outside.Root, "generated.bin"); File.WriteAllBytes(target, bytes); File.Delete(authority);
        if (hardLink && OperatingSystem.IsWindows()) { WindowsFixtureLinks.HardLink(authority, target); }
        else { File.CreateSymbolicLink(authority, target); }
        Assert.Equal(StorageError.UnsafePath, store.RecoverAuthority().Error);
        Assert.False(store.Save(Snapshot(2)).Succeeded);
        Assert.Equal(bytes, File.ReadAllBytes(target));
    }

    private sealed class Cut(V2Stage target) : IV2FaultInjector
    { public void Check(V2Stage stage) { if (stage == target) { throw new IOException("generated interruption"); } } }

    [Fact]
    public void ExplicitFormat3DoesNotReadOrChangeIndependentV1Data()
    {
        using var legacy = new OwnedDirectory(); using var candidate = new OwnedDirectory();
        Assert.True(legacy.Store().Save(Snapshot(1)).Succeeded);
        var before = Directory.GetFiles(legacy.Root).ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes);
        using var store = WorkspaceV2Store.ForRecoverableTests(candidate.Root);
        Assert.False(store.Load().Error == StorageError.None);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        Assert.True(store.Save(Snapshot(10)).Succeeded);
        File.Delete(Path.Combine(candidate.Root, "state-v2.bin"));
        Assert.True(store.RecoverAuthority().Succeeded);
        Assert.Equal(11, store.Restore().Workspace!.Revision);
        foreach (var file in before) { Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(legacy.Root, file.Key))); }
        Assert.Equal(1, legacy.Store().Load().Workspace!.Revision);
    }

    [Fact]
    public void RepairedMaximumReservationCannotOverflowDuringRestore()
    {
        using var root = new OwnedDirectory(); using var store = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded);
        Assert.True(store.Save(Snapshot(long.MaxValue)).Succeeded);
        File.Delete(Path.Combine(root.Root, "state-v2.bin"));
        Assert.True(store.RecoverAuthority().Succeeded);
        Assert.Equal(StorageError.RevisionOverflow, store.RestoreRevisionTransaction(1).Transaction.Error);
        Assert.Equal(long.MaxValue, store.Load().Workspace!.Revision);
    }

    [Fact]
    public void BothSlotsCannotAuthorizeRepairPastAContradictoryWitness()
    {
        using var root = new OwnedDirectory(); using var store = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded); Assert.True(store.Save(Snapshot(10)).Succeeded);
        string witness = Directory.GetFiles(root.Root, "root-*.bin")[0];
        byte[] bytes = File.ReadAllBytes(witness); bytes[28] ^= 1;
        SHA256.HashData(bytes.AsSpan(0, 68)).CopyTo(bytes, 68); File.WriteAllBytes(witness, bytes);
        Assert.Equal(StorageError.IntegrityFailure, store.Load().Error);
        File.Delete(Path.Combine(root.Root, "state-v2.bin"));
        Assert.Equal(StorageError.IntegrityFailure, store.RecoverAuthority().Error);
        Assert.False(store.Save(Snapshot(20)).Succeeded);
        Assert.Equal(bytes, File.ReadAllBytes(witness));
    }

    [Fact]
    public void AReplacedAuthorityWitnessPreventsTrustingAnOlderValidSlot()
    {
        using var root = new OwnedDirectory();
        using (var setup = WorkspaceV2Store.ForRecoverableTests(root.Root))
        { Assert.True(setup.InitializeNew(Snapshot(1)).Succeeded); Assert.True(setup.Save(Snapshot(10)).Succeeded); }
        string authority = Path.Combine(root.Root, "authority-v3.bin"); byte[] old = File.ReadAllBytes(authority);
        using (var pending = WorkspaceV2Store.ForRecoverableTests(root.Root, new Cut(V2Stage.AfterReservation)))
        { Assert.False(pending.Save(Snapshot(20)).Succeeded); }
        File.WriteAllBytes(authority, old);
        using var restarted = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.Equal(StorageError.RecoveryRequired, restarted.Load().Error);
        Assert.False(restarted.RecoverAuthority().Succeeded);
        Assert.False(restarted.Save(Snapshot(20, true)).Succeeded);
    }

    [Fact]
    public void Format3CannotSkipTheRequiredPreviousMainPublication()
    {
        using var root = new OwnedDirectory(); using var store = WorkspaceV2Store.ForRecoverableTests(root.Root);
        Assert.True(store.InitializeNew(Snapshot(1)).Succeeded); Assert.True(store.Save(Snapshot(10)).Succeeded);
        string authority = Path.Combine(root.Root, "authority-v3.bin"); byte[] a = File.ReadAllBytes(authority);
        string name = "segment-" + Convert.ToHexString(a.AsSpan(28, 32)) + ".bin";
        byte[] page = File.ReadAllBytes(Path.Combine(root.Root, name)); BitConverter.GetBytes(0L).CopyTo(page, 60);
        SHA256.HashData(page.AsSpan(0, page.Length - 32)).CopyTo(page, page.Length - 32);
        byte[] head = SHA256.HashData(page);
        File.WriteAllBytes(Path.Combine(root.Root, "segment-" + Convert.ToHexString(head) + ".bin"), page);
        foreach (string slot in new[] { "authority-v3.bin", "state-v2.bin" })
        {
            string path = Path.Combine(root.Root, slot); byte[] bytes = File.ReadAllBytes(path); head.CopyTo(bytes, 28);
            if (slot == "authority-v3.bin") { BitConverter.GetBytes(0L).CopyTo(bytes, 60); }
            SHA256.HashData(bytes.AsSpan(0, 68)).CopyTo(bytes, 68); File.WriteAllBytes(path, bytes);
        }
        Assert.Equal(StorageError.IntegrityFailure, store.RecoverAuthority().Error);
        Assert.False(store.Save(Snapshot(20)).Succeeded);
    }
}
