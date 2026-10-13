using Zhuomian.Core.Interaction;
using Zhuomian.Core.Launching;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.Launching;

internal sealed class LaunchClock : IInteractionClock { public long NowMilliseconds { get; set; } }

internal sealed class FakeLease(ResolvedLaunchTarget target) : ILaunchLease
{
    public ResolvedLaunchTarget Target { get; } = target;
    internal LaunchResult Validation { get; set; } = new(LaunchStatus.Accepted);
    internal bool Disposed { get; private set; }
    public LaunchResult Revalidate() => Validation;
    public void Dispose() => Disposed = true;
}

internal sealed class FakeResolver : ILaunchIdentityResolver
{
    internal int Calls { get; private set; }
    internal Action? OnResolve { get; set; }
    internal Exception? Error { get; set; }
    internal LaunchResult Result { get; set; } = new(LaunchStatus.Accepted);
    internal FakeLease? Lease { get; set; }
    public LaunchResolution Resolve(Item item, CancellationToken cancellationToken)
    {
        Calls++;
        OnResolve?.Invoke();
        if (Error is not null) { throw Error; }
        Lease ??= new(item switch
        {
            ApplicationItem app => new(LaunchTargetKind.Application, @"C:\fixture\approved.exe", "stable", app.Identity, app.Provenance),
            FileItem file => new(LaunchTargetKind.File, file.Path.Value, "stable"),
            FolderItem folder => new(LaunchTargetKind.Folder, folder.Path.Value, "stable"),
            UrlItem url => new(LaunchTargetKind.Url, url.Destination.Value, "stable"),
            _ => throw new InvalidOperationException(),
        });
        return new(Result, Lease);
    }
}

internal sealed class FakeExecutor : ILaunchExecutor
{
    internal int Calls { get; private set; }
    internal Exception? Error { get; set; }
    internal LaunchResult Result { get; set; } = new(LaunchStatus.SubmittedToOperatingSystem);
    public LaunchResult Submit(ILaunchLease lease, CancellationToken cancellationToken)
    {
        Calls++;
        if (Error is not null) { throw Error; }
        return Result;
    }
}

internal sealed class LaunchFixture
{
    internal static readonly Guid Space = Guid.Parse("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid OtherSpace = Guid.Parse("10000000-0000-0000-0000-000000000002");
    internal static readonly Guid ItemId = Guid.Parse("10000000-0000-0000-0000-000000000003");
    internal Workspace Workspace { get; set; }
    internal LaunchClock Clock { get; } = new();
    internal FakeResolver Resolver { get; } = new();
    internal FakeExecutor Executor { get; } = new();
    internal ItemLaunchService Service { get; }
    internal LaunchFixture(Item? item = null)
    {
        Workspace = Workspace.Create(Guid.NewGuid()).CreateSpace(Space, "fixture", new(ActivationMode.Click))
            .CreateSpace(OtherSpace, "shared", new(ActivationMode.Hover))
            .AddItemToSpace(Space, item ?? CreateItem(LaunchTargetKind.Url)).AddReference(OtherSpace, ItemId);
        Service = new(Workspace, Clock, Resolver, Executor, 1000);
        Service.UpdateContext(Workspace, 0, true);
    }
    internal LaunchRequest Prepare() => Service.Prepare(Space, ItemId, 0).Request!;
    internal static Item CreateItem(LaunchTargetKind kind) => kind switch
    {
        LaunchTargetKind.Application => new ApplicationItem(ItemId, new("private-name"), new("approved", "opaque"), new(ApplicationOrigin.ExplicitSelection, "reviewed")),
        LaunchTargetKind.File => new FileItem(ItemId, new("private-name"), new(@"C:\fixture\data.txt"), "stable"),
        LaunchTargetKind.Folder => new FolderItem(ItemId, new("private-name"), new(@"C:\fixture\folder"), "stable"),
        LaunchTargetKind.Url => new UrlItem(ItemId, new("private-name"), new("https://example.test/path?secret=hidden#private")),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
