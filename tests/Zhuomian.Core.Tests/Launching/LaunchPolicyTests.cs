using Zhuomian.Core.Launching;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.Launching;

public sealed class LaunchPolicyTests
{
    [Theory]
    [InlineData(LaunchTargetKind.Application)]
    [InlineData(LaunchTargetKind.File)]
    [InlineData(LaunchTargetKind.Folder)]
    [InlineData(LaunchTargetKind.Url)]
    public void FourDomainsRequireResolverAfterLexicalAdmission(LaunchTargetKind kind)
    {
        var fixture = new LaunchFixture(LaunchFixture.CreateItem(kind));
        var admission = fixture.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, 0);
        Assert.Equal(LaunchStatus.Accepted, admission.Result.Status);
        Assert.Equal(0, fixture.Executor.Calls);
        Assert.Equal(LaunchStatus.SubmittedToOperatingSystem, fixture.Service.Submit(admission.Request!).Status);
        Assert.Equal(1, fixture.Resolver.Calls);
        Assert.Equal(1, fixture.Executor.Calls);
        Assert.True(fixture.Resolver.Lease!.Disposed);
    }

    [Theory]
    [InlineData("cmd.exe")]
    [InlineData("file.EXE")]
    [InlineData("file.com")]
    [InlineData("file.bat")]
    [InlineData("file.cmd")]
    [InlineData("file.ps1")]
    [InlineData("file.psm1")]
    [InlineData("file.vbs")]
    [InlineData("file.js")]
    [InlineData("file.jse")]
    [InlineData("file.wsf")]
    [InlineData("file.hta")]
    [InlineData("file.msi")]
    [InlineData("file.msp")]
    [InlineData("file.lnk")]
    [InlineData("file.url")]
    [InlineData("file.scf")]
    [InlineData("file.scr")]
    [InlineData("file.cpl")]
    [InlineData("file.dll")]
    [InlineData("file.reg")]
    [InlineData("file.application")]
    [InlineData("file.appref-ms")]
    [InlineData("file.html")]
    [InlineData("file.svg")]
    [InlineData("file.docm")]
    [InlineData("file.txt.exe")]
    [InlineData("file")]
    public void FileCannotBecomeExecutionShortcut(string leaf)
    {
        var fixture = new LaunchFixture(new FileItem(LaunchFixture.ItemId, new("fixture"), new(@"C:\data\" + leaf)));
        Assert.Equal(LaunchStatus.Denied, fixture.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, 0).Result.Status);
        Assert.Equal(0, fixture.Resolver.Calls);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Theory]
    [InlineData(@"\\server\share\data.txt")]
    [InlineData("C:\\data\\hidden\u202efile.txt")]
    public void LexicallyValidButSuspiciousPathDoesNotReachResolver(string path)
    {
        Assert.Equal(LaunchStatus.Denied, LaunchPolicy.CheckPath(new(path), file: true).Status);
        Assert.Equal(LaunchStatus.Denied, LaunchPolicy.CheckPath(new(path), file: false).Status);
    }

    [Theory]
    [InlineData(@"C:\CON.txt")]
    [InlineData(@"C:\aux\data.txt")]
    [InlineData(@"C:\COM1.txt")]
    [InlineData(@"C:\LPT9.txt")]
    [InlineData("relative.txt")]
    [InlineData(@"C:relative.txt")]
    [InlineData(@"\relative.txt")]
    [InlineData(@"\\?\C:\data.txt")]
    [InlineData(@"\\.\GLOBALROOT\data")]
    [InlineData(@"C:\data.txt:stream")]
    [InlineData(@"C:\data.txt.")]
    [InlineData("C:\\data\\file\n.txt")]
    public void InvalidPathsAreAlreadyRejectedByExistingIdentity(string path) => Assert.Throws<ArgumentException>(() => new WindowsPath(path));

    [Theory]
    [InlineData("https://example.test/")]
    [InlineData("http://example.test/path?q=a%20b")]
    [InlineData("https://例子.测试/path")]
    [InlineData("https://example.test/日本語")]
    public void CanonicalHttpDestinationsRemainEligibleForSeparateAssociationReview(string url) =>
        Assert.True(LaunchPolicy.IsSafeUrl(new HttpUrl(url).Value));

    [Theory]
    [InlineData("file:///C:/data.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-settings:display")]
    [InlineData("https://user:password@example.test/")]
    [InlineData(" https://example.test/")]
    [InlineData("https:\\example.test/")]
    [InlineData("https://example.test/%0a")]
    [InlineData("https://example.test/%0d")]
    [InlineData("https://example.test/%00")]
    [InlineData("https://example.test/%250a")]
    [InlineData("https://example.test/%2f%2fevil.test")]
    [InlineData("https://example.test/%40evil.test")]
    [InlineData("https://example.test/\u202eprivate")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("http://[::1]/")]
    [InlineData("https://localhost/")]
    [InlineData("https://host.local/")]
    [InlineData("https://example.test./")]
    [InlineData("https://example.test/\n")]
    [InlineData("https://example.test/%E2%80%AEname")]
    public void UnsafeOrAmbiguousDestinationsAreDenied(string url) => Assert.False(LaunchPolicy.IsSafeUrl(url));

    [Theory]
    [InlineData(ItemAvailability.Missing, LaunchStatus.MissingOrInaccessible)]
    [InlineData(ItemAvailability.Unsupported, LaunchStatus.Unsupported)]
    public void InvalidAvailabilityIsNotPermission(ItemAvailability availability, LaunchStatus status)
    {
        var fixture = new LaunchFixture(new UrlItem(LaunchFixture.ItemId, new("fixture"), new("https://example.test/"), availability));
        Assert.Equal(status, fixture.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, 0).Result.Status);
        Assert.Equal(0, fixture.Executor.Calls);
    }
}
