using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Launching;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Launching.Windows;

namespace Zhuomian.Launching.Windows.Tests;

public sealed class WindowsLaunchTests
{
    private static readonly ApplicationIdentity App = new("controlled-test-provider", "fixture");
    private static readonly ApplicationProvenance Source = new(ApplicationOrigin.ExplicitSelection, "repository-build-fixture");
    private static ApplicationItem Item => new(Guid.NewGuid(), new("fixture"), App, Source);
    private static string FixtureExecutable => Path.Combine(AppContext.BaseDirectory, "fixture", "Zhuomian.LaunchFixture.exe");
    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static WindowsLaunchResolver Approved(string path, string? digest = null) => new([new(App, Source, new(path), digest ?? Digest(path))]);

    [Fact]
    public void DefaultCatalogDoesNotTrustOpaqueApplicationStrings()
    {
        var resolver = new WindowsLaunchResolver();
        Assert.Equal(LaunchStatus.NoTrustedApplication, resolver.Resolve(Item, default).Result.Status);
        var command = new ApplicationItem(Guid.NewGuid(), new("fixture"), new("cmd.exe /c", @"C:\danger.exe"), Source);
        Assert.Equal(LaunchStatus.NoTrustedApplication, resolver.Resolve(command, default).Result.Status);
    }

    [Fact]
    public void ExactProvenanceIsRequiredEvenForKnownIdentity()
    {
        var resolver = new WindowsLaunchResolver([new(App, Source, new(@"C:\fixture\safe.exe"), new string('A', 64))]);
        var forged = new ApplicationItem(Guid.NewGuid(), new("fixture"), App, new(ApplicationOrigin.DiscoveryProvider, "unknown"));
        Assert.Equal(LaunchStatus.NoTrustedApplication, resolver.Resolve(forged, default).Result.Status);
    }

    [Fact]
    public void ActualWindowsCreateProcessSubmissionOfOwnedFixtureOrExplicitUnsupportedPlatform()
    {
        var executor = new WindowsLaunchExecutor();
        if (!OperatingSystem.IsWindows())
        {
            var resolver = new WindowsLaunchResolver([new(App, Source, new(@"C:\fixture\safe.exe"), new string('A', 64))]);
            Assert.Equal(LaunchReason.PlatformUnavailable, resolver.Resolve(Item, default).Result.Reason);
            Assert.Equal(LaunchReason.PlatformUnavailable, executor.Submit(new DescriptionLease(), default).Reason);
            return;
        }
        Assert.True(File.Exists(FixtureExecutable));
        var resolution = Approved(FixtureExecutable).Resolve(Item, default);
        using var lease = resolution.Lease;
        Assert.Equal(LaunchStatus.Accepted, resolution.Result.Status);
        Assert.NotNull(lease);
        Assert.Equal(LaunchStatus.Accepted, lease.Revalidate().Status);
        Assert.Equal(LaunchStatus.SubmittedToOperatingSystem, executor.Submit(lease, default).Status);
    }

    [Fact]
    public void FullServiceCanSubmitOnlyThePinnedOwnedFixtureOnWindows()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        var item = Item;
        Guid space = Guid.NewGuid();
        var workspace = Workspace.Create(Guid.NewGuid()).CreateSpace(space, "fixture", new(ActivationMode.Click)).AddItemToSpace(space, item);
        var service = new ItemLaunchService(workspace, new Clock(), Approved(FixtureExecutable), new WindowsLaunchExecutor(), 1000);
        service.UpdateContext(workspace, 0, true);
        var admission = service.Prepare(space, item.Id, 0);
        Assert.Equal(LaunchStatus.Accepted, admission.Result.Status);
        Assert.Equal(LaunchStatus.SubmittedToOperatingSystem, service.Submit(admission.Request!).Status);
        Assert.Equal(LaunchStatus.Duplicate, service.Submit(admission.Request!).Status);
        Assert.Same(item, workspace.ItemStore.Items[item.Id]);
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("powershell.exe")]
    [InlineData("pwsh.exe")]
    [InlineData("wscript.exe")]
    [InlineData("cscript.exe")]
    [InlineData("mshta.exe")]
    [InlineData("rundll32.exe")]
    [InlineData("msiexec.exe")]
    [InlineData("dotnet.exe")]
    [InlineData("explorer.exe")]
    [InlineData("script.ps1")]
    [InlineData("shortcut.lnk")]
    [InlineData("installer.msi")]
    public void CompositionApprovalCannotEnableCommandHostsOrNonExecutables(string name)
    {
        var resolver = new WindowsLaunchResolver([new(App, Source, new(@"C:\fixture\" + name), new string('A', 64))]);
        Assert.Equal(OperatingSystem.IsWindows() ? LaunchStatus.Denied : LaunchStatus.Unsupported, resolver.Resolve(Item, default).Result.Status);
    }

    [Theory]
    [InlineData("relative.exe")]
    [InlineData(@"\\?\C:\safe.exe")]
    [InlineData(@"C:\safe.exe:stream")]
    public void ApprovalStillRequiresExistingCanonicalPathRules(string path) => Assert.Throws<ArgumentException>(() =>
        new ApprovedApplication(App, Source, new(path), new string('A', 64)));

    [Theory]
    [InlineData("")]
    [InlineData("sha256")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void DeploymentDigestCannotBeImplicitOrMalformed(string digest) => Assert.Throws<ArgumentException>(() =>
        new ApprovedApplication(App, Source, new(@"C:\safe.exe"), digest));

    [Fact]
    public void WindowsExecutorRejectsForeignLeaseAndNeverExecutesItsDescription()
    {
        using var lease = new DescriptionLease();
        var result = new WindowsLaunchExecutor().Submit(lease, default);
        Assert.Equal(LaunchStatus.Unsupported, result.Status);
        Assert.Equal(OperatingSystem.IsWindows() ? LaunchReason.UnreviewedAssociation : LaunchReason.PlatformUnavailable, result.Reason);
    }

    [Fact]
    public void CancellationIsExplicitBeforeAnyNativeOperation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new WindowsLaunchResolver().Resolve(Item, cancellation.Token));
        Assert.Equal(LaunchStatus.Cancelled, new WindowsLaunchExecutor().Submit(new DescriptionLease(), cancellation.Token).Status);
    }

    [Fact]
    public void ActualFileSystemMissingDigestReplacementAndSharingViolationAreSafe()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        var missing = new FileItem(Guid.NewGuid(), new("fixture"), new(Path.Combine(fixture.Root, "missing.txt")));
        Assert.Equal(LaunchStatus.MissingOrInaccessible, new WindowsLaunchResolver().Resolve(missing, default).Result.Status);
        var approval = Approved(fixture.Executable);
        File.WriteAllText(fixture.Executable, "replaced");
        Assert.Equal(LaunchReason.ResourceReplaced, approval.Resolve(Item, default).Result.Reason);
        using var exclusive = new FileStream(fixture.File, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var file = new FileItem(Guid.NewGuid(), new("fixture"), new(fixture.File));
        Assert.Equal(LaunchStatus.MissingOrInaccessible, new WindowsLaunchResolver().Resolve(file, default).Result.Status);
    }

    [Fact]
    public void LiveHandlesBlockLeafReplacementWriteAndParentRenameThenReleaseOwnership()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        var resolution = Approved(fixture.Executable).Resolve(Item, default);
        Assert.Equal(LaunchStatus.Accepted, resolution.Result.Status);
        using (var lease = resolution.Lease!)
        {
            Assert.Throws<IOException>(() => File.Delete(fixture.Executable));
            Assert.Throws<IOException>(() => File.WriteAllText(fixture.Executable, "replacement"));
            Assert.Throws<IOException>(() => Directory.Move(fixture.Root, fixture.Root + "-moved"));
            Assert.Equal(LaunchStatus.Accepted, lease.Revalidate().Status);
        }
        File.WriteAllText(fixture.Executable, "after-dispose");
        Assert.Equal("after-dispose", File.ReadAllText(fixture.Executable));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualLeafAndAncestorSymbolicLinksAreRejected(bool ancestor)
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        string path;
        if (ancestor)
        {
            string link = Path.Combine(fixture.Root, "linked-directory");
            Directory.CreateSymbolicLink(link, fixture.RealFolder);
            path = Path.Combine(link, "data.txt");
        }
        else
        {
            path = Path.Combine(fixture.Root, "linked.txt");
            File.CreateSymbolicLink(path, fixture.File);
        }
        var file = new FileItem(Guid.NewGuid(), new("fixture"), new(path));
        Assert.Equal(LaunchStatus.Denied, new WindowsLaunchResolver().Resolve(file, default).Result.Status);
        Assert.Equal("source", File.ReadAllText(fixture.File));
    }

    [Fact]
    public void SafeFileFolderAndUrlRemainDisabledWithoutReviewedWindowsAssociation()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        Item[] items = [new FileItem(Guid.NewGuid(), new("fixture"), new(fixture.File)),
            new FolderItem(Guid.NewGuid(), new("fixture"), new(fixture.RealFolder)),
            new UrlItem(Guid.NewGuid(), new("fixture"), new("https://example.test/?private=secret"))];
        foreach (var item in items)
        {
            var resolution = new WindowsLaunchResolver().Resolve(item, default);
            Assert.Equal(new(LaunchStatus.Unsupported, LaunchReason.UnreviewedAssociation), resolution.Result);
            Assert.Null(resolution.Lease);
        }
        Assert.Equal("source", File.ReadAllText(fixture.File));
        Assert.Single(Directory.GetFiles(fixture.RealFolder));
    }

    [Fact]
    public void StableIdentityMismatchAndInvalidExecutableAreDifferentFailures()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        var file = new FileItem(Guid.NewGuid(), new("fixture"), new(fixture.File), "wrong-volume-file-id");
        Assert.Equal(LaunchReason.ResourceReplaced, new WindowsLaunchResolver().Resolve(file, default).Result.Reason);
        var resolution = Approved(fixture.Executable).Resolve(Item, default);
        using var lease = resolution.Lease!;
        // An explicitly reviewed fixture containing plain text cannot be submitted as a PE executable.
        Assert.Equal(LaunchStatus.OperatingSystemFailure, new WindowsLaunchExecutor().Submit(lease, default).Status);
        Assert.Equal("fixture-not-PE", File.ReadAllText(fixture.Executable));
    }

    [Fact]
    public void ActualJunctionIsRejectedWithoutFollowingItsTarget()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        string link = Path.Combine(fixture.Root, "junction");
        WindowsFixtureLinks.Junction(link, fixture.RealFolder);
        try
        {
            var file = new FileItem(Guid.NewGuid(), new("fixture"), new(Path.Combine(link, "data.txt")));
            var folder = new FolderItem(Guid.NewGuid(), new("fixture"), new(link));
            Assert.Equal(LaunchStatus.Denied, new WindowsLaunchResolver().Resolve(file, default).Result.Status);
            Assert.Equal(LaunchStatus.Denied, new WindowsLaunchResolver().Resolve(folder, default).Result.Status);
        }
        finally { WindowsFixtureLinks.RemoveJunction(link); }
        Assert.False(Directory.Exists(link));
        Assert.Equal("source", File.ReadAllText(fixture.File));
    }

    [Fact]
    public void ActualHardLinkedLeafCannotGainLaunchPermission()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        string link = Path.Combine(fixture.Root, "hardlink.txt");
        WindowsFixtureLinks.HardLink(link, fixture.File);
        var file = new FileItem(Guid.NewGuid(), new("fixture"), new(link));
        Assert.Equal(LaunchStatus.Denied, new WindowsLaunchResolver().Resolve(file, default).Result.Status);
        Assert.Equal("source", File.ReadAllText(fixture.File));
    }

    [Fact]
    public void ActualReadPermissionDenialIsReportedWithoutChangingSource()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        var info = new FileInfo(fixture.File);
        var originalSecurity = info.GetAccessControl(AccessControlSections.Access);
        string original = originalSecurity.GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        var restricted = info.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        try
        {
            info.SetAccessControl(restricted);
            var file = new FileItem(Guid.NewGuid(), new("fixture"), new(fixture.File));
            var result = new WindowsLaunchResolver().Resolve(file, default).Result;
            Assert.Equal(new(LaunchStatus.MissingOrInaccessible, LaunchReason.ResourceUnavailable), result);
            Assert.DoesNotContain(fixture.Root, result.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            // An unchanged FileSecurity has no modified sections: SetAccessControl(originalObject) is a no-op.
            var restored = new FileSecurity();
            restored.SetSecurityDescriptorSddlForm(original, AccessControlSections.Access);
            info.SetAccessControl(restored);
        }
        var currentSecurity = info.GetAccessControl(AccessControlSections.Access);
        // SetNamedSecurityInfo may add the AI control flag; compare exact ACL bytes and protection instead of SDDL formatting.
        Assert.Equal(DaclBytes(originalSecurity), DaclBytes(currentSecurity));
        Assert.Equal(originalSecurity.AreAccessRulesProtected, currentSecurity.AreAccessRulesProtected);
        Assert.Equal("source", File.ReadAllText(fixture.File));
    }

    [Fact]
    public void NetworkApprovalIsDeniedBeforeAnyNetworkAccess()
    {
        var resolver = new WindowsLaunchResolver([new(App, Source, new(@"\\unreachable\share\safe.exe"), new string('A', 64))]);
        Assert.Equal(OperatingSystem.IsWindows() ? LaunchStatus.Denied : LaunchStatus.Unsupported, resolver.Resolve(Item, default).Result.Status);
        var file = new FileItem(Guid.NewGuid(), new("fixture"), new(@"\\unreachable\share\data.txt"));
        Assert.Equal(LaunchStatus.Denied, resolver.Resolve(file, default).Result.Status);
    }

    [Fact]
    public void MarkedDownloadIsDeniedEvenWhenItsDeploymentDigestMatches()
    {
        if (!OperatingSystem.IsWindows()) { Assert.False(OperatingSystem.IsWindows()); return; }
        using var fixture = new TemporaryResources();
        string marker = fixture.Executable + ":Zone.Identifier";
        File.WriteAllText(marker, "[ZoneTransfer]\nZoneId=3\n");
        var result = Approved(fixture.Executable).Resolve(Item, default).Result;
        Assert.Equal(new(LaunchStatus.Denied, LaunchReason.UnsafeTarget), result);
        Assert.Equal("[ZoneTransfer]\nZoneId=3\n", File.ReadAllText(marker));
    }

    [Fact]
    public void PublicDescriptionToStringDoesNotExposeDestinationsOrDigests()
    {
        var target = new ResolvedLaunchTarget(LaunchTargetKind.Url, "https://example.test/?secret=private", "private-id");
        Assert.Equal(nameof(ResolvedLaunchTarget), target.ToString());
        var approved = new ApprovedApplication(App, Source, new(@"C:\private\name.exe"), new string('A', 64));
        Assert.Equal(nameof(ApprovedApplication), approved.ToString());
    }

    [SupportedOSPlatform("windows")]
    private static byte[] DaclBytes(FileSecurity security)
    {
        var descriptor = new RawSecurityDescriptor(security.GetSecurityDescriptorBinaryForm(), 0);
        var acl = descriptor.DiscretionaryAcl!;
        byte[] bytes = new byte[acl.BinaryLength];
        acl.GetBinaryForm(bytes, 0);
        return bytes;
    }

    private sealed class Clock : IInteractionClock { public long NowMilliseconds => 0; }
    private sealed class DescriptionLease : ILaunchLease
    {
        public ResolvedLaunchTarget Target { get; } = new(LaunchTargetKind.Application, @"C:\Windows\System32\cmd.exe", "untrusted", App, Source);
        public LaunchResult Revalidate() => new(LaunchStatus.Accepted);
        public void Dispose() { }
    }
    private sealed class TemporaryResources : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "zhuomian-launch-" + Guid.NewGuid().ToString("N"));
        internal string RealFolder => Path.Combine(Root, "real");
        internal string File => Path.Combine(RealFolder, "data.txt");
        internal string Executable => Path.Combine(Root, "approved.exe");
        internal TemporaryResources()
        {
            Directory.CreateDirectory(RealFolder);
            System.IO.File.WriteAllText(File, "source");
            System.IO.File.WriteAllText(Executable, "fixture-not-PE");
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
