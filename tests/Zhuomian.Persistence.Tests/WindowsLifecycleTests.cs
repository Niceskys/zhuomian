using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Zhuomian.Persistence.Tests;

public sealed class WindowsLifecycleTests
{
    [Fact]
    public void ActualWindowsInventoryRejectsHardlinkedHistoryWithoutTouchingSource()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory(); using var outside = new OwnedDirectory();
        var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        string source = Path.Combine(outside.Root, "source"); byte[] bytes = WorkspaceCodec.Encode(Samples.Full()); File.WriteAllBytes(source, bytes);
        string link = Path.Combine(owned.Root, "prepared-" + Guid.NewGuid().ToString("N") + ".json");
        WindowsFixtureLinks.HardLink(link, source);
        Assert.Equal(StorageError.UnsafePath, store.Inventory().Error); Assert.False(store.DryRun().IsReviewable);
        Assert.Equal(StorageError.MaintenanceDisabled, store.Apply(store.DryRun(), true).Error);
        Assert.Equal(bytes, File.ReadAllBytes(source)); Assert.Single(Directory.GetFiles(outside.Root));
    }

    [Fact]
    public void ActualWindowsSameBytesReplacementInvalidatesNativeFileIdentity()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var plan = store.DryRun(); var export = store.PlanBackupExport();
        string backup = Directory.GetFiles(owned.Root, "backup-*.json").Single();
        string replacement = Path.Combine(owned.Root, "replacement"); File.WriteAllBytes(replacement, File.ReadAllBytes(backup));
        File.Replace(replacement, backup, null);
        Assert.Equal(StorageError.StalePlan, store.ValidatePlan(plan).Error);
        Assert.Equal(StorageError.StalePlan, store.ValidateExport(export).Error);
        Assert.True(store.Inventory().IsComplete);
    }

    [Fact]
    public void ActualWindowsSharedReadDenialFailsClosedAndPreservesAllAssets()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        var plan = store.DryRun(); var export = store.PlanBackupExport();
        string backup = Directory.GetFiles(owned.Root, "backup-*.json").Single(); byte[] before = File.ReadAllBytes(backup);
        using (var blocked = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.Inventory().IsComplete); Assert.False(store.DryRun().IsReviewable);
            Assert.NotEqual(StorageError.None, store.ValidatePlan(plan).Error); Assert.NotEqual(StorageError.None, store.ValidateExport(export).Error);
            Assert.Equal(StorageError.MaintenanceDisabled, store.Apply(plan, true).Error);
        }
        Assert.Equal(before, File.ReadAllBytes(backup)); Assert.Equal(StorageError.None, store.ValidatePlan(plan).Error);
    }

    [Fact]
    public void ActualWindowsJunctionAndDirectoryAliasesFailClosedDuringInventory()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory(); using var outside = new OwnedDirectory();
        string link = Path.Combine(owned.Root, "evidence-" + Guid.NewGuid().ToString("N") + ".json");
        WindowsFixtureLinks.Junction(link, outside.Root);
        try { Assert.Equal(StorageError.UnsafePath, owned.Store().Inventory().Error); Assert.Empty(Directory.GetFiles(outside.Root)); }
        finally { WindowsFixtureLinks.RemoveJunction(link); }
        // Native final-path comparison also refuses namespace aliases, independently of lexical validation.
        Assert.False(WorkspaceFileStore.ForTests(owned.Root + ".").Inventory().IsComplete);
    }

    [Fact]
    public void ActualWindowsInventoryAccessDenialIsRedactedAndRestoresFixtureAcl()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        AccessDenial();
    }

    [SupportedOSPlatform("windows")]
    private static void AccessDenial()
    {
        using var owned = new OwnedDirectory(); var store = owned.Store(); Assert.True(store.Save(Samples.Full()).Succeeded);
        string backup = Directory.GetFiles(owned.Root, "backup-*.json").Single(); byte[] before = File.ReadAllBytes(backup);
        var file = new FileInfo(backup); var original = file.GetAccessControl(AccessControlSections.Access);
        string sddl = original.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var denied = file.GetAccessControl(); using var identity = WindowsIdentity.GetCurrent();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            file.SetAccessControl(denied);
            var inventory = store.Inventory(); Assert.Equal(StorageError.AccessDenied, inventory.Error); Assert.False(inventory.IsComplete);
            Assert.DoesNotContain(owned.Root, inventory.ToString()); Assert.False(store.DryRun().IsReviewable);
        }
        finally
        {
            var restored = new FileSecurity(); restored.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access); file.SetAccessControl(restored);
        }
        Assert.Equal(before, File.ReadAllBytes(backup)); Assert.True(store.Inventory().IsComplete);
    }

    [Fact]
    public void ActualWindowsInventoryPinsFilesAgainstConcurrentReplacementAndDirectoriesAgainstRename()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var owned = new OwnedDirectory(); Assert.True(owned.Store().Save(Samples.Full()).Succeeded);
        var storage = new PhysicalStorage(owned.Root, null); var held = new List<FileStream>();
        string backup = Path.GetFileName(Directory.GetFiles(owned.Root, "backup-*.json").Single());
        using (var pins = storage.PinRoot(createMissing: false))
        {
            try
            {
                storage.Capture(backup, true, PersistenceLimits.MaxRecoveryScanBytes, held);
                string replacement = Path.Combine(owned.Root, "replacement"); File.WriteAllBytes(replacement, WorkspaceCodec.Encode(Samples.Full()));
                Assert.Throws<IOException>(() => File.Replace(replacement, Path.Combine(owned.Root, backup), null));
                Assert.Throws<IOException>(() => Directory.Move(owned.Root, owned.Root + "-moved"));
                File.Delete(replacement);
            }
            finally { foreach (var handle in held) { handle.Dispose(); } }
        }
        Directory.Move(owned.Root, owned.Root + "-moved"); Directory.Move(owned.Root + "-moved", owned.Root);
        Assert.True(owned.Store().Inventory().IsComplete);
    }
}
