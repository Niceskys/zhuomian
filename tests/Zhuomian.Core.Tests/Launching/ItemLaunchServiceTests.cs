using Zhuomian.Core.Launching;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.Launching;

public sealed class ItemLaunchServiceTests
{
    [Fact]
    public void SameRequestCanOnlySubmitOnceEvenAfterFailure()
    {
        var fixture = new LaunchFixture();
        var request = fixture.Prepare();
        fixture.Executor.Result = new(LaunchStatus.OperatingSystemFailure, LaunchReason.ExecutorFailure);
        Assert.Equal(LaunchStatus.OperatingSystemFailure, fixture.Service.Submit(request).Status);
        Assert.Equal(LaunchStatus.Duplicate, fixture.Service.Submit(request).Status);
        Assert.Equal(1, fixture.Executor.Calls);
    }

    [Fact]
    public async Task ConcurrentDuplicateHasExactlyOneExecutorCall()
    {
        var fixture = new LaunchFixture();
        var request = fixture.Prepare();
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => fixture.Service.Submit(request))));
        Assert.Single(results, result => result.Status == LaunchStatus.SubmittedToOperatingSystem);
        Assert.Equal(19, results.Count(result => result.Status == LaunchStatus.Duplicate));
        Assert.Equal(1, fixture.Executor.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1000)]
    [InlineData(1001)]
    public void ExpiredOrClockReversedRequestNeverResolves(int elapsed)
    {
        var fixture = new LaunchFixture();
        var request = fixture.Prepare();
        fixture.Clock.NowMilliseconds = elapsed;
        Assert.Equal(LaunchStatus.Expired, fixture.Service.Submit(request).Status);
        Assert.Equal(0, fixture.Resolver.Calls);
    }

    [Fact]
    public void CancellationBeforeResolutionConsumesWithoutLaunching()
    {
        var fixture = new LaunchFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = fixture.Prepare();
        Assert.Equal(LaunchStatus.Cancelled, fixture.Service.Submit(request, cancellation.Token).Status);
        Assert.Equal(LaunchStatus.Duplicate, fixture.Service.Submit(request).Status);
        Assert.Equal(0, fixture.Resolver.Calls);
    }

    [Fact]
    public void CancellationDuringResolutionDisposesLease()
    {
        var fixture = new LaunchFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Resolver.OnResolve = cancellation.Cancel;
        Assert.Equal(LaunchStatus.Cancelled, fixture.Service.Submit(fixture.Prepare(), cancellation.Token).Status);
        Assert.True(fixture.Resolver.Lease!.Disposed);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Theory]
    [InlineData("remove-reference")]
    [InlineData("remove-item")]
    [InlineData("delete-space")]
    [InlineData("unavailable")]
    [InlineData("new-revision")]
    [InlineData("same-revision-rehydrated")]
    public void AnyNewSnapshotInvalidatesPinnedPermission(string change)
    {
        var fixture = new LaunchFixture();
        var request = fixture.Prepare();
        var original = fixture.Workspace;
        fixture.Workspace = change switch
        {
            "remove-reference" => original.RemoveReference(LaunchFixture.Space, LaunchFixture.ItemId),
            "remove-item" => original.RemoveItemGlobally(LaunchFixture.ItemId, GlobalRemovalConfirmation.Confirmed),
            "delete-space" => original.DeleteSpace(LaunchFixture.Space),
            "unavailable" => original.SetItemAvailability(LaunchFixture.ItemId, ItemAvailability.Missing),
            "new-revision" => original.RenameSpace(LaunchFixture.Space, "changed"),
            _ => Workspace.FromSnapshot(original.Id, original.SchemaVersion, original.Revision, original.ItemStore, original.Spaces.Values),
        };
        fixture.Service.UpdateContext(fixture.Workspace, 0, true);
        Assert.Equal(LaunchStatus.Expired, fixture.Service.Submit(request).Status);
        Assert.Equal(0, fixture.Executor.Calls);
        Assert.Equal(0, fixture.Resolver.Calls);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void InputGenerationAndSafetyGateAreRechecked(bool allowed, long generation)
    {
        var fixture = new LaunchFixture();
        var request = fixture.Prepare();
        fixture.Service.UpdateContext(fixture.Workspace, generation, allowed);
        Assert.Equal(LaunchStatus.Expired, fixture.Service.Submit(request).Status);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Fact]
    public void SnapshotChangeDuringResolverCannotAuthorizeOldClick()
    {
        var fixture = new LaunchFixture();
        fixture.Resolver.OnResolve = () => fixture.Service.UpdateContext(fixture.Workspace.RemoveReference(LaunchFixture.Space, LaunchFixture.ItemId), 0, true);
        Assert.Equal(LaunchStatus.Expired, fixture.Service.Submit(fixture.Prepare()).Status);
        Assert.True(fixture.Resolver.Lease!.Disposed);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Theory]
    [InlineData(LaunchStatus.MissingOrInaccessible, LaunchReason.ResourceUnavailable)]
    [InlineData(LaunchStatus.NoTrustedApplication, LaunchReason.UntrustedProvider)]
    [InlineData(LaunchStatus.Unsupported, LaunchReason.UnreviewedAssociation)]
    [InlineData(LaunchStatus.Denied, LaunchReason.UnsafeTarget)]
    public void ResolverFailuresAreStructuredAndNeverExecute(LaunchStatus status, LaunchReason reason)
    {
        var fixture = new LaunchFixture();
        fixture.Resolver.Result = new(status, reason);
        Assert.Equal(new(status, reason), fixture.Service.Submit(fixture.Prepare()));
        Assert.Equal(0, fixture.Executor.Calls);
        Assert.True(fixture.Resolver.Lease!.Disposed);
    }

    [Theory]
    [InlineData(LaunchTargetKind.Application)]
    [InlineData(LaunchTargetKind.File)]
    [InlineData(LaunchTargetKind.Folder)]
    [InlineData(LaunchTargetKind.Url)]
    public void ReplacedTargetIsRejectedForEveryDomain(LaunchTargetKind kind)
    {
        var fixture = new LaunchFixture(LaunchFixture.CreateItem(kind));
        fixture.Resolver.Resolve(fixture.Workspace.ItemStore.Items[LaunchFixture.ItemId], default);
        fixture.Resolver.Lease!.Validation = new(LaunchStatus.Denied, LaunchReason.ResourceReplaced);
        Assert.Equal(LaunchReason.ResourceReplaced, fixture.Service.Submit(fixture.Prepare()).Reason);
        Assert.Equal(0, fixture.Executor.Calls);
        Assert.True(fixture.Resolver.Lease.Disposed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExceptionsNeverEscapeOrExposeSecretPayloadAndWorkspaceIsUnchanged(bool resolverError)
    {
        var fixture = new LaunchFixture();
        var original = fixture.Workspace;
        var error = new InvalidOperationException(@"C:\Users\private\secret-name.txt https://example.test/?token=secret");
        if (resolverError) { fixture.Resolver.Error = error; } else { fixture.Executor.Error = error; }
        var result = fixture.Service.Submit(fixture.Prepare());
        Assert.Equal(resolverError ? LaunchReason.ResolverFailure : LaunchReason.ExecutorFailure, result.Reason);
        Assert.DoesNotContain("secret", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.test", result.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Same(original, fixture.Workspace);
        Assert.Empty(original.ItemStore.RemovedItems);
        if (!resolverError) { Assert.True(fixture.Resolver.Lease!.Disposed); }
    }

    [Fact]
    public void ResolverCannotSubstituteIdentityOrProvenance()
    {
        var fixture = new LaunchFixture(LaunchFixture.CreateItem(LaunchTargetKind.Application));
        fixture.Resolver.Lease = new(new(LaunchTargetKind.Application, @"C:\danger.exe", "stable", new("untrusted", "command"), new(ApplicationOrigin.DiscoveryProvider, "untrusted")));
        Assert.Equal(LaunchStatus.Denied, fixture.Service.Submit(fixture.Prepare()).Status);
        Assert.Equal(0, fixture.Executor.Calls);
        Assert.True(fixture.Resolver.Lease.Disposed);
    }

    [Theory]
    [InlineData(LaunchTargetKind.File)]
    [InlineData(LaunchTargetKind.Folder)]
    [InlineData(LaunchTargetKind.Url)]
    public void ResolverCannotRetargetBoundResource(LaunchTargetKind kind)
    {
        var fixture = new LaunchFixture(LaunchFixture.CreateItem(kind));
        fixture.Resolver.Lease = new(new(kind, "another-target", "another-identity"));
        Assert.Equal(LaunchStatus.Denied, fixture.Service.Submit(fixture.Prepare()).Status);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Fact]
    public void ForeignSessionAndUnboundIdNeverAuthorize()
    {
        var first = new LaunchFixture();
        var other = new LaunchFixture();
        Assert.Equal(LaunchStatus.Denied, other.Service.Submit(first.Prepare()).Status);
        Assert.Null(first.Service.Prepare(LaunchFixture.Space, Guid.NewGuid(), 0).Request);
        Assert.Null(first.Service.Prepare(Guid.NewGuid(), LaunchFixture.ItemId, 0).Request);
        Assert.Null(first.Service.Prepare(LaunchFixture.Space, LaunchFixture.ItemId, 1).Request);
        Assert.Equal(0, other.Executor.Calls);
    }

    [Fact]
    public void SharedReferenceSurvivesUnbindingOtherSpaceButNeedsFreshRequest()
    {
        var fixture = new LaunchFixture();
        fixture.Workspace = fixture.Workspace.RemoveReference(LaunchFixture.OtherSpace, LaunchFixture.ItemId);
        fixture.Service.UpdateContext(fixture.Workspace, 0, true);
        Assert.Null(fixture.Service.Prepare(LaunchFixture.OtherSpace, LaunchFixture.ItemId, 0).Request);
        Assert.Equal(LaunchStatus.SubmittedToOperatingSystem, fixture.Service.Submit(fixture.Prepare()).Status);
    }

    [Fact]
    public void GateRestorationCannotReviveAnEarlierPermission()
    {
        var fixture = new LaunchFixture();
        var request = fixture.Prepare();
        fixture.Service.UpdateContext(fixture.Workspace, 0, false);
        fixture.Service.UpdateContext(fixture.Workspace, 0, true);
        Assert.Equal(LaunchStatus.Expired, fixture.Service.Submit(request).Status);
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Fact]
    public void ContextCannotRollBackRevisionGenerationOrWorkspaceSession()
    {
        var fixture = new LaunchFixture();
        var old = fixture.Workspace;
        var newer = old.RenameSpace(LaunchFixture.Space, "newer");
        fixture.Service.UpdateContext(newer, 2, true);
        Assert.Throws<ArgumentException>(() => fixture.Service.UpdateContext(old, 2, true));
        Assert.Throws<ArgumentException>(() => fixture.Service.UpdateContext(newer, 1, true));
        Assert.Throws<ArgumentException>(() => fixture.Service.UpdateContext(Workspace.Create(Guid.NewGuid()), 2, true));
        Assert.Equal(0, fixture.Executor.Calls);
    }

    [Fact]
    public void ExecutorAcceptanceIsNotOperatingSystemSubmission()
    {
        var fixture = new LaunchFixture();
        fixture.Executor.Result = new(LaunchStatus.Accepted);
        Assert.Equal(LaunchStatus.OperatingSystemFailure, fixture.Service.Submit(fixture.Prepare()).Status);
    }
}
