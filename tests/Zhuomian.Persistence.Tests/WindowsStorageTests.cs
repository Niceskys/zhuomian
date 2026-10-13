using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

public sealed class WindowsStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymbolicLeafAndStorageRootNeverRedirectWritesToExternalData(bool rootLink)
    {
        using var owned = new OwnedDirectory();
        using var outside = new OwnedDirectory();
        string source = Path.Combine(outside.Root, "source.json");
        System.IO.File.WriteAllText(source, "external source must remain exact");
        if (rootLink)
        {
            Directory.Delete(owned.Root);
            Directory.CreateSymbolicLink(owned.Root, outside.Root);
        }
        else { System.IO.File.CreateSymbolicLink(owned.Main, source); }
        try
        {
            Assert.Equal(StorageError.UnsafePath, owned.Store().Save(Samples.Full()).Error);
            Assert.Equal(StorageError.UnsafePath, owned.Store().Load().Error);
            Assert.Equal(StorageError.UnsafePath, owned.Store().Restore().Error);
            Assert.Equal("external source must remain exact", System.IO.File.ReadAllText(source));
            Assert.Single(Directory.GetFiles(outside.Root));
        }
        finally
        {
            if (rootLink) { Directory.Delete(owned.Root); Directory.CreateDirectory(owned.Root); }
            else { System.IO.File.Delete(owned.Main); }
        }
    }

    [Fact]
    public void DanglingStorageRootLinkCannotCreateAnExternalTarget()
    {
        using var owned = new OwnedDirectory();
        using var outside = new OwnedDirectory();
        string missingTarget = Path.Combine(outside.Root, "missing");
        Directory.Delete(owned.Root);
        Directory.CreateSymbolicLink(owned.Root, missingTarget);
        try
        {
            Assert.Equal(StorageError.UnsafePath, owned.Store().Save(Samples.Full()).Error);
            Assert.False(Directory.Exists(missingTarget));
        }
        finally
        {
            if (OperatingSystem.IsWindows()) { Directory.Delete(owned.Root); }
            else { System.IO.File.Delete(owned.Root); }
            Directory.CreateDirectory(owned.Root);
        }
    }

    [Fact]
    public void ActualWindowsJunctionCannotRedirectStorageIntoAnotherOwnedTarget()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory();
        using var outside = new OwnedDirectory();
        Directory.Delete(owned.Root);
        WindowsFixtureLinks.Junction(owned.Root, outside.Root);
        try
        {
            Assert.Equal(StorageError.UnsafePath, owned.Store().Save(Samples.Full()).Error);
            Assert.Empty(Directory.GetFiles(outside.Root));
        }
        finally { WindowsFixtureLinks.RemoveJunction(owned.Root); Directory.CreateDirectory(owned.Root); }
    }

    [Fact]
    public void ActualWindowsHardLinkedMainAndLockAreRejectedWithoutModifyingSource()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory();
        using var outside = new OwnedDirectory();
        string source = Path.Combine(outside.Root, "source.json");
        System.IO.File.WriteAllBytes(source, WorkspaceCodec.Encode(Samples.Full()));
        byte[] expected = System.IO.File.ReadAllBytes(source);
        WindowsFixtureLinks.HardLink(owned.Main, source);
        Assert.Equal(StorageError.UnsafePath, owned.Store().Save(Samples.Full()).Error);
        System.IO.File.Delete(owned.Main);
        System.IO.File.Delete(Path.Combine(owned.Root, "workspace.lock"));
        WindowsFixtureLinks.HardLink(Path.Combine(owned.Root, "workspace.lock"), source);
        Assert.Equal(StorageError.UnsafePath, owned.Store().Load().Error);
        Assert.Equal(expected, System.IO.File.ReadAllBytes(source));
    }

    [Fact]
    public void OwnedRootValidationRejectsExternalAndTraversalInjection()
    {
        Assert.Throws<ArgumentException>(() => WorkspaceFileStore.ForTests("relative"));
        Assert.Throws<ArgumentException>(() => WorkspaceFileStore.ForTests(Path.Combine(Path.GetTempPath(), "outside")));
        using var owned = new OwnedDirectory();
        Assert.Throws<ArgumentException>(() => WorkspaceFileStore.ForTests(Path.Combine(owned.Root, "..", Path.GetFileName(owned.Root))));
    }

    [Fact]
    public void OversizedPhysicalFileIsRejectedWithoutReadAllBytesOrReset()
    {
        using var owned = new OwnedDirectory();
        using (var file = System.IO.File.Create(owned.Main)) { file.SetLength(PersistenceLimits.MaxFileBytes + 1); }
        Assert.Equal(StorageError.TooLarge, owned.Store().Load().Error);
        Assert.Equal(PersistenceLimits.MaxFileBytes + 1, new FileInfo(owned.Main).Length);
    }

    [Fact]
    public void ActualWindowsLockedExternalResourcesAndIconsAreNotReadWrittenOrDeleted()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory();
        using var outside = new OwnedDirectory();
        string source = Path.Combine(outside.Root, "source.txt");
        System.IO.File.WriteAllText(source, "external bytes");
        var resource = new FileItem(Guid.NewGuid(), new("External", new ExternalResourceReference(new(source))), new(source));
        var snapshot = Samples.Full().AddItem(resource);
        using (var denied = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(owned.Store().Save(snapshot).Succeeded);
            Assert.True(owned.Store().Save(snapshot.RenameSpace(Samples.SpaceA, "Next")).Succeeded);
            Assert.NotNull(owned.Store().Load().Workspace);
            Assert.Equal(RecoveryState.BackupRecovered, owned.Store().Restore().State);
        }
        Assert.Equal("external bytes", System.IO.File.ReadAllText(source));
        Assert.Single(Directory.GetFiles(outside.Root));
    }

    [Fact]
    public void ActualWindowsDirectoryPinsPreventRenameDuringTransactionAndReleaseAfterward()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store(new RenameAttempt(owned.Root)).Save(Samples.Full()).Succeeded);
        Directory.Move(owned.Root, owned.Root + "-renamed");
        Directory.Move(owned.Root + "-renamed", owned.Root);
    }

    [Fact]
    public void ActualWindowsReplacementFailureRetainsMainAndDoesNotOverwriteLockedSource()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        byte[] expected = System.IO.File.ReadAllBytes(owned.Main);
        using (var held = new FileStream(owned.Main, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = owned.Store().Save(Samples.Full().RenameSpace(Samples.SpaceA, "Next"));
            Assert.Equal(StorageError.IoFailure, result.Error);
            Assert.Equal(expected, System.IO.File.ReadAllBytes(owned.Main));
        }
        Assert.True(owned.Store().Save(Samples.Full().RenameSpace(Samples.SpaceA, "Next")).Succeeded);
    }

    [Fact]
    public void ActualWindowsPostReplaceReadDenialReportsUnknownCommitThenAllowsSafeRetry()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        var next = Samples.Full().RenameSpace(Samples.SpaceA, "Next");
        using (var fault = new BlockReadAfterReplace(owned.Main))
        {
            var result = owned.Store(fault).Save(next);
            Assert.Equal(StorageError.IoFailure, result.Error);
            Assert.Equal(CommitState.Unknown, result.Commit);
        }
        Assert.Equal(next.Revision, owned.Store().Load().Workspace!.Revision);
        Assert.Equal(CommitState.Unchanged, owned.Store().Save(next).Commit);
    }

    private sealed class BlockReadAfterReplace(string main) : IStorageFaultInjector, IDisposable
    {
        private FileStream? _held;
        public void Check(StorageStage stage)
        {
            if (stage != StorageStage.AfterReplace) { return; }
            _held = new(main, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new IOException("owned post-submission failure");
        }
        public void Dispose() => _held?.Dispose();
    }

    [Fact]
    public void ActualWindowsWritePermissionDenialIsStructuredAndDoesNotMutateAclOrMain()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        PermissionDenial();
    }
    [SupportedOSPlatform("windows")]
    private static void PermissionDenial()
    {
        using var owned = new OwnedDirectory();
        Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        byte[] before = System.IO.File.ReadAllBytes(owned.Main);
        var directory = new DirectoryInfo(owned.Root);
        var original = directory.GetAccessControl(AccessControlSections.Access);
        string sddl = original.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var denied = directory.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(denied);
            var applied = directory.GetAccessControl(AccessControlSections.Access);
            var result = owned.Store().Save(Samples.Full().RenameSpace(Samples.SpaceA, "Next"));
            Assert.Equal(StorageError.AccessDenied, result.Error);
            Assert.Equal(before, System.IO.File.ReadAllBytes(owned.Main));
            var after = directory.GetAccessControl(AccessControlSections.Access);
            Assert.Equal(applied.GetSecurityDescriptorSddlForm(AccessControlSections.Access), after.GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            Assert.Equal(DaclBytes(applied), DaclBytes(after));
            Assert.Equal(applied.AreAccessRulesProtected, after.AreAccessRulesProtected);
        }
        finally
        {
            var restored = new DirectorySecurity();
            restored.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
            directory.SetAccessControl(restored);
        }
        var restoredSecurity = directory.GetAccessControl(AccessControlSections.Access);
        Assert.Equal(DaclBytes(original), DaclBytes(restoredSecurity));
        Assert.Equal(original.AreAccessRulesProtected, restoredSecurity.AreAccessRulesProtected);
        Assert.True(owned.Store().Save(Samples.Full().RenameSpace(Samples.SpaceA, "Next")).Succeeded);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] DaclBytes(DirectorySecurity security)
    {
        var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        var acl = descriptor.DiscretionaryAcl!;
        byte[] bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private sealed class RenameAttempt(string root) : IStorageFaultInjector
    {
        public void Check(StorageStage stage)
        {
            if (stage == StorageStage.BeforeReplace) { Assert.Throws<IOException>(() => Directory.Move(root, root + "-unexpected")); }
        }
    }
}
