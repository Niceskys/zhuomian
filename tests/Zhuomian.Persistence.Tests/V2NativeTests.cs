using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

public sealed class V2NativeTests
{
    private static Workspace Snapshot(long revision)
    {
        var full = Samples.Full();
        return Workspace.FromSnapshot(full.Id, full.SchemaVersion, revision, full.ItemStore,
            full.Spaces.Values, full.RemovedSpaces.Values);
    }
    private static WorkspaceV2Store Store(OwnedDirectory root, IV2FaultInjector? fault = null) => WorkspaceV2Store.ForTests(root.Root, fault);

    [Theory]
    [InlineData(V2Stage.BeforeReservation)]
    [InlineData(V2Stage.AfterReservation)]
    [InlineData(V2Stage.BeforeMain)]
    [InlineData(V2Stage.AfterMain)]
    public async Task KilledChildReleasesMutexAndNewProcessResolvesDurableState(V2Stage stage)
    {
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        using var child = Start(root, stage, "save");
        try
        {
            Assert.Equal("cut", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(StorageError.Busy, Store(root).Save(Snapshot(20)).Error);
            Assert.Equal(StorageError.Busy, Store(root).Restore().Error);
            child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
        using var restarted = Start(root, stage, "load");
        string expected = stage == V2Stage.AfterMain ? "None:10" : "None:1";
        Assert.Equal(expected, await restarted.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
        await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); Assert.Equal(0, restarted.ExitCode);
        if (stage != V2Stage.BeforeReservation)
        { Assert.Equal(StorageError.StaleRevision, Store(root).Save(Snapshot(2)).Error); }
        Assert.True(Store(root).Save(Snapshot(10)).Succeeded);
    }

    private static Process Start(OwnedDirectory root, V2Stage stage, string mode)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "v2-fixture", "Zhuomian.Persistence.V2Fixture.dll"));
        start.ArgumentList.Add(root.Root); start.ArgumentList.Add(stage.ToString()); start.ArgumentList.Add(mode);
        return Process.Start(start)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymbolicRootOrStateCannotTouchExternalBytes(bool rootLink)
    {
        using var owned = new OwnedDirectory(); using var outside = new OwnedDirectory();
        var state = Path.Combine(owned.Root, "state-v2.bin"); var external = Path.Combine(outside.Root, "generated.bin");
        File.WriteAllBytes(external, [1, 2, 3]);
        if (rootLink) { Directory.Delete(owned.Root); Directory.CreateSymbolicLink(owned.Root, outside.Root); }
        else { File.CreateSymbolicLink(state, external); }
        try
        {
            Assert.False(Store(owned).InitializeNew(Snapshot(1)).Succeeded);
            Assert.Equal(StorageError.UnsafePath, Store(owned).Load().Error);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(external));
        }
        finally
        {
            if (rootLink) { Directory.Delete(owned.Root); Directory.CreateDirectory(owned.Root); }
            else { File.Delete(state); }
        }
    }

    [Theory]
    [InlineData("state-v2.bin")]
    [InlineData("workspace.lock")]
    [InlineData("segment")]
    public void WindowsHardLinksAreRejected(string entry)
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var root = new OwnedDirectory(); using var outside = new OwnedDirectory();
        Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        string path = entry == "segment" ? Directory.GetFiles(root.Root, "segment-*.bin")[0] : Path.Combine(root.Root, entry);
        byte[] expected = File.ReadAllBytes(path);
        string source = Path.Combine(outside.Root, "generated.bin"); File.WriteAllBytes(source, expected);
        File.Delete(path); WindowsFixtureLinks.HardLink(path, source);
        Assert.Equal(StorageError.UnsafePath, Store(root).Load().Error);
        Assert.False(Store(root).Save(Snapshot(2)).Succeeded);
        Assert.Equal(expected, File.ReadAllBytes(source));
    }

    [Fact]
    public void WindowsJunctionAliasesRenameAndSharingRemainGuarded()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var root = new OwnedDirectory(); using var outside = new OwnedDirectory();
        Directory.Delete(root.Root); WindowsFixtureLinks.Junction(root.Root, outside.Root);
        try { Assert.Equal(StorageError.UnsafePath, Store(root).InitializeNew(Snapshot(1)).Error); Assert.Empty(Directory.GetFiles(outside.Root)); }
        finally { WindowsFixtureLinks.RemoveJunction(root.Root); Directory.CreateDirectory(root.Root); }
        Assert.Throws<ArgumentException>(() => WorkspaceV2Store.ForTests(root.Root + "."));
        Assert.Throws<ArgumentException>(() => WorkspaceV2Store.ForTests(root.Root + " "));
        Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        Assert.True(Store(root, new Rename(root.Root)).Save(Snapshot(2)).Succeeded);
        using (var held = new FileStream(Path.Combine(root.Root, "state-v2.bin"), FileMode.Open, FileAccess.Read, FileShare.Read))
        { Assert.False(Store(root).Save(Snapshot(3)).Succeeded); }
        Assert.True(Store(root).Save(Snapshot(3)).Succeeded);
        Directory.Move(root.Root, root.Root + "-moved"); Directory.Move(root.Root + "-moved", root.Root);
    }

    [Fact]
    public void WindowsPermissionDenialPreservesMainAndAcl()
    {
        if (OperatingSystem.IsWindows()) { PermissionDenial(); }
    }
    [SupportedOSPlatform("windows")]
    private static void PermissionDenial()
    {
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        var directory = new DirectoryInfo(root.Root); var original = directory.GetAccessControl(AccessControlSections.Access);
        string sddl = original.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        byte[] state = File.ReadAllBytes(Path.Combine(root.Root, "state-v2.bin"));
        using var identity = WindowsIdentity.GetCurrent(); var denied = directory.GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(denied);
            string applied = directory.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            Assert.Equal(StorageError.AccessDenied, Store(root).Save(Snapshot(2)).Error);
            Assert.Equal(state, File.ReadAllBytes(Path.Combine(root.Root, "state-v2.bin")));
            Assert.Equal(applied, directory.GetAccessControl(AccessControlSections.Access).GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        finally { var restored = new DirectorySecurity(); restored.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access); directory.SetAccessControl(restored); }
        Assert.True(Store(root).Save(Snapshot(2)).Succeeded);
    }

    [Fact]
    public void WindowsActualPostPublicationSharingDenialReportsUnknown()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        using (var fault = new BlockRead(Path.Combine(root.Root, "state-v2.bin")))
        { Assert.Equal(CommitState.Unknown, Store(root, fault).Save(Snapshot(2)).Commit); }
        Assert.Equal(2, Store(root).Load().Workspace!.Revision);
        Assert.Equal(CommitState.Unchanged, Store(root).Save(Snapshot(2)).Commit);
    }
    private sealed class BlockRead(string state) : IV2FaultInjector, IDisposable
    {
        private FileStream? _held;
        public void Check(V2Stage stage)
        {
            if (stage != V2Stage.AfterMain) { return; }
            _held = new(state, FileMode.Open, FileAccess.Read, FileShare.None);
            throw new IOException();
        }
        public void Dispose() => _held?.Dispose();
    }

    [Theory]
    [InlineData(V2Stage.DuringWrite, 112, StorageError.DiskFull)]
    [InlineData(V2Stage.BeforeFlush, 29, StorageError.IoFailure)]
    public void InjectedIoErrorsPreserveSelectedState(V2Stage stage, int code, StorageError expected)
    {
        using var root = new OwnedDirectory(); Assert.True(Store(root).InitializeNew(Snapshot(1)).Succeeded);
        Assert.Equal(expected, Store(root, new Io(stage, code)).Save(Snapshot(2)).Error);
        Assert.Equal(1, Store(root).Load().Workspace!.Revision);
    }
    private sealed class Io(V2Stage target, int code) : IV2FaultInjector
    { public void Check(V2Stage stage) { if (stage == target) { throw new IOException("generated", unchecked((int)0x80070000) | code); } } }
    private sealed class Rename(string root) : IV2FaultInjector
    {
        public void Check(V2Stage stage)
        { if (stage == V2Stage.BeforeMain) { Assert.Throws<IOException>(() => Directory.Move(root, root + "-unexpected")); } }
    }
}
