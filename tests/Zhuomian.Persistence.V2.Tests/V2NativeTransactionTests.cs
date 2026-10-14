using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2NativeTransactionTests
{
    [Theory]
    [InlineData("Initialize")]
    [InlineData("Restore")]
    public void ActualPostMainSharingObstructionNeverClaimsNotCommitted(string operation)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var root = new OwnedDirectory();
        if (operation == "Restore")
        { using var setup = WorkspaceV2Store.ForTests(root.Root); Assert.True(setup.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded); }
        using (var fault = new Block(Path.Combine(root.Root, "state-v2.bin")))
        using (var store = WorkspaceV2Store.ForTests(root.Root, fault))
        {
            if (operation == "Initialize") { Assert.Equal(CommitState.Unknown, store.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Commit); }
            else { var result = store.RestoreRevision(1); Assert.NotEqual(StorageError.None, result.Error); Assert.True(result.IsReadOnly); }
        }
        using var restarted = WorkspaceV2Store.ForTests(root.Root);
        var loaded = restarted.Load();
        Assert.Equal(operation == "Initialize" ? 1 : 2, loaded.Workspace!.Revision);
        Assert.Equal(CommitState.Unchanged, restarted.Save(loaded.Workspace).Commit);
        V2TransactionMatrixTests.WriteEvidence($"native-unknown-{operation}.json", new
        { operation, windowsNtfs = true, postMainReadBlocked = true, restartRevision = loaded.Workspace.Revision, identicalRetry = "Unchanged" });
    }

    [Theory]
    [InlineData("Initialize")]
    [InlineData("Restore")]
    public void ActualCreatePermissionDenialDoesNotRewriteAuthorityOrAcl(string operation)
    { if (OperatingSystem.IsWindows()) { Denied(operation); } }

    [SupportedOSPlatform("windows")]
    private static void Denied(string operation)
    {
        using var root = new OwnedDirectory(); using var setup = WorkspaceV2Store.ForTests(root.Root);
        if (operation == "Restore") { Assert.True(setup.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded); }
        var before = V2TransactionMatrixTests.Evidence(root);
        byte[]? state = operation == "Restore" ? File.ReadAllBytes(Path.Combine(root.Root, "state-v2.bin")) : null;
        var directory = new DirectoryInfo(root.Root);
        string original = directory.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        using var identity = WindowsIdentity.GetCurrent(); var denied = directory.GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(denied);
            using var store = WorkspaceV2Store.ForTests(root.Root);
            var error = operation == "Initialize" ? store.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Error : store.RestoreRevision(1).Error;
            Assert.Equal(StorageError.AccessDenied, error);
            V2TransactionMatrixTests.AssertRetained(root, before);
            if (state is not null) { Assert.Equal(state, File.ReadAllBytes(Path.Combine(root.Root, "state-v2.bin"))); }
        }
        finally
        {
            var restored = new DirectorySecurity(); restored.SetSecurityDescriptorSddlForm(original, AccessControlSections.Access);
            directory.SetAccessControl(restored);
        }
        Assert.Equal(original, directory.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        Assert.Equal(StorageError.None, operation == "Initialize" ? setup.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Error : setup.RestoreRevision(1).Error);
        V2TransactionMatrixTests.WriteEvidence($"native-acl-{operation}.json", new
        { operation, windowsNtfs = true, error = "AccessDenied", authorityPreserved = true, aclRestored = true, retry = "None" });
    }

    [Fact]
    public void ActualRestoreSharingConflictPreservesSelectedMainAndThenRetries()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var root = new OwnedDirectory(); using var store = WorkspaceV2Store.ForTests(root.Root);
        Assert.True(store.InitializeNew(V2TransactionMatrixTests.Snapshot(1)).Succeeded);
        var original = V2TransactionMatrixTests.Evidence(root);
        using (var held = new FileStream(Path.Combine(root.Root, "state-v2.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
        { Assert.Equal(StorageError.Busy, store.RestoreRevision(1).Error); Assert.Equal(1, store.Load().Workspace!.Revision); }
        V2TransactionMatrixTests.AssertRetained(root, original);
        Assert.Equal(2, store.RestoreRevision(1).Workspace!.Revision);
        V2TransactionMatrixTests.WriteEvidence("native-sharing-Restore.json", new
        { operation = "Restore", windowsNtfs = true, error = "Busy", selectedMain = 1, retryRevision = 2, evidenceRetained = true });
    }

    private sealed class Block(string path) : IV2FaultInjector, IDisposable
    {
        private FileStream? _held;
        public void Check(V2Stage stage)
        {
            if (stage != V2Stage.AfterMain) { return; }
            _held = new(path, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new IOException("generated");
        }
        public void Dispose() => _held?.Dispose();
    }
}
