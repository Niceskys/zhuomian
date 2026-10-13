using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

internal sealed class OwnedDirectory : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "zhuomian-persistence-" + Guid.NewGuid().ToString("N"));
    internal string Main => Path.Combine(Root, "workspace.json");
    internal OwnedDirectory() => Directory.CreateDirectory(Root);
    internal WorkspaceFileStore Store(IStorageFaultInjector? faults = null, IStorageDiagnostics? diagnostics = null) => WorkspaceFileStore.ForTests(Root, faults, diagnostics);
    public void Dispose() => Directory.Delete(Root, recursive: true);
}

internal static class Samples
{
    internal static readonly Guid SpaceA = new("00000002-0000-0000-0000-000000000000");
    internal static readonly Guid SpaceB = new("00000003-0000-0000-0000-000000000000");
    internal static readonly Guid FileId = new("00000010-0000-0000-0000-000000000000");
    internal static Workspace Full()
    {
        var file = new FileItem(FileId, new("Private name", new ExternalResourceReference(new(@"C:\fixtures\icon.png"))), new(@"C:\fixtures\data.txt"), "volume:file", ItemAvailability.Missing);
        var app = new ApplicationItem(new("00000011-0000-0000-0000-000000000000"), new("Application"), new("provider", "app"), new(ApplicationOrigin.ExplicitSelection, "source"), ItemAvailability.Available);
        var folder = new FolderItem(new("00000012-0000-0000-0000-000000000000"), new("Folder", new ManagedAssetReference(new("00000050-0000-0000-0000-000000000000"))), new(@"C:\fixtures\folder"), "folder-id", ItemAvailability.Unsupported);
        var url = new UrlItem(new("00000013-0000-0000-0000-000000000000"), new("URL"), new("https://example.test/path?token=private#secret"));
        var removed = new UrlItem(new("00000014-0000-0000-0000-000000000000"), new("Removed"), new("https://example.test/removed"));
        return Workspace.FromSnapshot(new("00000001-0000-0000-0000-000000000000"), 1, 42,
            ItemStore.FromSnapshot([file, app, folder, url], [removed]),
            [new(SpaceA, "A", new(ActivationMode.Hover, 120), new(0), [url.Id, file.Id, app.Id, folder.Id]),
             new(SpaceB, "B", new(ActivationMode.Click), itemIds: [file.Id])],
            [new(new("00000004-0000-0000-0000-000000000000"), "Removed space", new(ActivationMode.Hover, 0), itemIds: [removed.Id, file.Id])]);
    }
    internal static byte[] Mutate(Action<JsonObject> action, bool rehash = true)
    {
        var root = JsonNode.Parse(WorkspaceCodec.Encode(Full()))!.AsObject();
        action(root);
        if (rehash) { root["sha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root["payload"]!.ToJsonString()))); }
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
}

internal sealed class Fault(StorageStage target, bool diskFull = false) : IStorageFaultInjector
{
    public void Check(StorageStage stage)
    {
        if (stage == target) { throw new IOException("private exception must never escape", diskFull ? unchecked((int)0x80070070) : unchecked((int)0x8007001D)); }
    }
}
internal sealed class Diagnostics : IStorageDiagnostics
{
    internal List<StorageDiagnostic> Events { get; } = [];
    public void Report(StorageDiagnostic diagnostic) => Events.Add(diagnostic);
}
