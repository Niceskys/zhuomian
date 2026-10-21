using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.WorkspaceModel;

public sealed class ResourceIdentityTests
{
    private static readonly Guid ItemId = new("10000000-0000-0000-0000-000000000001");

    [Theory]
    [InlineData(@"c:/Fixtures/sub/../file.txt", @"C:\Fixtures\file.txt")]
    [InlineData(@"C:\Fixtures\.\file.txt", @"C:\Fixtures\file.txt")]
    [InlineData(@"\\server\share\a\..\file.txt", @"\\server\share\file.txt")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"\\server\share", @"\\server\share\")]
    public void WindowsPathsNormalizeLexicallyOnEveryHost(string input, string expected)
    {
        var path = new WindowsPath(input);
        Assert.Equal(expected, path.Value);
        Assert.Equal(expected.ToUpperInvariant(), path.CanonicalKey);
    }

    [Theory]
    [InlineData("relative.txt")]
    [InlineData("C:relative.txt")]
    [InlineData(@"C:\..\escape.txt")]
    [InlineData(@"\\server\share\..\escape.txt")]
    [InlineData(@"\\server")]
    [InlineData(@"\\.\pipe\payload")]
    [InlineData(@"\\?\C:\payload")]
    [InlineData(@"C:\file.txt:stream")]
    [InlineData(@"C:\file.")]
    [InlineData(@"C:\file ")]
    [InlineData(@"C:\*.txt")]
    [InlineData("C:\\bad\nname")]
    public void AmbiguousOrNonAbsolutePathsRejectWithoutIO(string value) =>
        Assert.Throws<ArgumentException>(() => new WindowsPath(value));

    [Theory]
    [InlineData(@"C:\NUL")]
    [InlineData(@"C:\tools\con.TXT")]
    [InlineData(@"D:\PRN.log")]
    [InlineData(@"C:\AUX\file.txt")]
    [InlineData(@"C:\tools\COM1")]
    [InlineData(@"C:\tools\lpt9.tar.gz")]
    [InlineData(@"C:\tools\COM¹.txt")]
    [InlineData(@"C:\tools\LPT²")]
    [InlineData(@"C:\tools\COM³")]
    [InlineData(@"\\server\share\Nul.txt")]
    [InlineData(@"C:\NUL\..\ordinary.txt")]
    public void ReservedDosDeviceNamesCannotEnterFileOrDirectoryPaths(string value) =>
        Assert.Throws<ArgumentException>(() => new WindowsPath(value));

    [Theory]
    [InlineData(@"C:\tools\COM10.txt")]
    [InlineData(@"C:\tools\LPT0.txt")]
    [InlineData(@"C:\tools\AUXILIARY.txt")]
    [InlineData(@"C:\tools\file.CON")]
    [InlineData(@"\\con\nul\ordinary.txt")]
    public void DeviceNameLookalikesAndUncRootNamesAreNotRejected(string value) =>
        Assert.Equal(value, new WindowsPath(value).Value);

    [Theory]
    [InlineData("HTTPS://EXAMPLE.TEST:443/a", "https://example.test/a")]
    [InlineData("http://example.test:80", "http://example.test/")]
    [InlineData("https://example.test/a?x=1#part", "https://example.test/a?x=1#part")]
    public void UrlNormalizationPreservesDestinationData(string input, string expected) =>
        Assert.Equal(expected, new HttpUrl(input).Value);

    [Theory]
    [InlineData("file:///C:/file.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:test@example.test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("https://example.test/\nsecret")]
    [InlineData("relative")]
    public void UnsupportedOrCredentialUrlsAreRejected(string value) =>
        Assert.Throws<ArgumentException>(() => new HttpUrl(value));

    [Fact]
    public void ApplicationIdentityIsProviderScopedAndProvenanceDoesNotGrantExecution()
    {
        var identity = new ApplicationIdentity(" provider ", " app-e\u0301 ");
        Assert.Equal(new ApplicationIdentity("provider", "app-é"), identity);
        Assert.NotEqual(new ApplicationIdentity("other-provider", "app-é"), identity);
        Assert.NotEqual(new ApplicationIdentity("provider", "APP-é"), identity);
        var item = new ApplicationItem(ItemId, new("Application"), identity, new(ApplicationOrigin.ExplicitSelection, "selector"));
        Assert.Equal(ApplicationOrigin.ExplicitSelection, item.Provenance.Origin);
        Assert.Equal(ItemAvailability.Unverified, item.Availability);
        Assert.DoesNotContain(typeof(ApplicationItem).GetProperties(), p => p.Name.Contains("Command", StringComparison.Ordinal) || p.Name.Contains("Arguments", StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationProvenance((ApplicationOrigin)99, "source"));
        Assert.Throws<ArgumentException>(() => new ApplicationIdentity("provider", " "));
    }

    [Fact]
    public void CatalogDedupUsesTypeAndCanonicalIdentityAndReservesRemovedIdentities()
    {
        var first = new UrlItem(ItemId, new("URL"), new("https://EXAMPLE.test:443/"));
        var duplicate = new UrlItem(new("10000000-0000-0000-0000-000000000002"), new("Other"), new("https://example.test/"));
        Assert.Equal(DomainError.ResourceAlreadyKnown,
            Assert.Throws<WorkspaceOperationException>(() => ItemStore.FromSnapshot(new[] { first, duplicate }, [])).Error);
        var app1 = new ApplicationItem(ItemId, new("App"), new("provider", "app"), new(ApplicationOrigin.DiscoveryProvider, "provider"));
        var app2 = new ApplicationItem(duplicate.Id, new("Alias"), new("provider", "app"), new(ApplicationOrigin.ExplicitSelection, "selector"));
        Assert.Equal(DomainError.ResourceAlreadyKnown,
            Assert.Throws<WorkspaceOperationException>(() => ItemStore.FromSnapshot(new[] { app1 }, new[] { app2 })).Error);
        var file = new FileItem(ItemId, new("File"), new(@"C:\Fixtures\target"), "volume:file-id");
        var folder = new FolderItem(duplicate.Id, new("Folder"), file.Path, "volume:folder-id");
        Assert.Equal(2, ItemStore.FromSnapshot(new Item[] { file, folder }, []).Items.Count);
        Assert.Equal("volume:folder-id", folder.StableFileIdentity);
    }

    [Fact]
    public void ResourceStringsAndErrorsDoNotEchoPrivateDestinations()
    {
        var path = new WindowsPath(@"C:\Fixtures\private.txt");
        var url = new HttpUrl("https://example.test/?secret=value#private");
        Assert.Equal(nameof(WindowsPath), path.ToString());
        Assert.Equal(nameof(HttpUrl), url.ToString());
        Assert.Equal(nameof(ExternalResourceReference), new ExternalResourceReference(path).ToString());
        Assert.Equal(nameof(ItemMetadata), new ItemMetadata("private").ToString());
        var error = Assert.Throws<ArgumentException>(() => new HttpUrl("javascript:private-value"));
        Assert.DoesNotContain("private-value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundaryNullsAndInvalidResourceStatesReject()
    {
        Assert.Throws<ArgumentNullException>(() => new WindowsPath(null!));
        Assert.Throws<ArgumentNullException>(() => new HttpUrl(null!));
        Assert.Throws<ArgumentNullException>(() => new FileItem(ItemId, new("File"), null!));
        Assert.Throws<ArgumentNullException>(() => new FolderItem(ItemId, null!, new(@"C:\Fixtures")));
        Assert.Throws<ArgumentNullException>(() => new ApplicationItem(ItemId, new("App"), null!, new(ApplicationOrigin.DiscoveryProvider, "source")));
        Assert.Throws<ArgumentNullException>(() => new ApplicationItem(ItemId, new("App"), new("provider", "app"), null!));
        Assert.Throws<ArgumentNullException>(() => new ExternalResourceReference(null!));
        Assert.Throws<ArgumentException>(() => new ManagedAssetReference(Guid.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UrlItem(ItemId, new("URL"), new("https://example.test"), (ItemAvailability)99));
    }
}
