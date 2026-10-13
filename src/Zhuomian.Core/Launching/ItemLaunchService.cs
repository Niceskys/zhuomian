using Zhuomian.Core.Interaction;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Launching;

/// <summary>
/// Synchronous bounded adapter. Serializes snapshot/gate updates and submission; no async permission survives a click.
/// A resolver/executor is trusted code, never loaded from Item provider strings. No Workspace/resource mutation.
/// </summary>
public sealed class ItemLaunchService
{
    private readonly object _sync = new();
    private readonly IInteractionClock _clock;
    private readonly ILaunchIdentityResolver _resolver;
    private readonly ILaunchExecutor _executor;
    private readonly int _requestLifetimeMilliseconds;
    private Workspace _workspace;
    private long _generation;
    private long _contextVersion;
    private bool _inputAllowed;

    public ItemLaunchService(Workspace workspace, IInteractionClock clock, ILaunchIdentityResolver resolver,
        ILaunchExecutor executor, int requestLifetimeMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestLifetimeMilliseconds);
        _workspace = workspace;
        _clock = clock;
        _resolver = resolver;
        _executor = executor;
        _requestLifetimeMilliseconds = requestLifetimeMilliseconds;
    }

    /// <summary>Called on the input dispatcher with the authoritative latest snapshot and controller gate.</summary>
    public void UpdateContext(Workspace workspace, long inputGeneration, bool inputAllowed)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentOutOfRangeException.ThrowIfNegative(inputGeneration);
        lock (_sync)
        {
            if (workspace.Id != _workspace.Id || workspace.Revision < _workspace.Revision || inputGeneration < _generation)
            { throw new ArgumentException("Launch context cannot regress or change session."); }
            if (!ReferenceEquals(workspace, _workspace) || inputGeneration != _generation || inputAllowed != _inputAllowed)
            { _contextVersion = checked(_contextVersion + 1); }
            _workspace = workspace;
            _generation = inputGeneration;
            _inputAllowed = inputAllowed;
        }
    }

    public LaunchAdmission Prepare(Guid spaceId, Guid itemId, long inputGeneration)
    {
        lock (_sync)
        {
            if (!_inputAllowed || inputGeneration != _generation) { return new(new(LaunchStatus.Denied, LaunchReason.StaleContext)); }
            if (!_workspace.Spaces.ContainsKey(spaceId) || !_workspace.GetUsableItemIds(spaceId).Contains(itemId) ||
                !_workspace.ItemStore.Items.TryGetValue(itemId, out var item))
            { return new(new(LaunchStatus.Denied, LaunchReason.InvalidReference)); }
            var result = LaunchPolicy.Check(item);
            return result.Status == LaunchStatus.Accepted
                ? new(result, new(this, _workspace, spaceId, item, inputGeneration, _clock.NowMilliseconds, _contextVersion)) : new(result);
        }
    }

    public LaunchResult Submit(LaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(request.Owner, this)) { return new(LaunchStatus.Denied, LaunchReason.StaleContext); }
        if (Interlocked.Exchange(ref request.Consumed, 1) != 0) { return new(LaunchStatus.Duplicate, LaunchReason.RequestConsumed); }
        lock (_sync)
        {
            var gate = CheckCurrent(request, cancellationToken);
            if (gate.Status != LaunchStatus.Accepted) { return gate; }
            LaunchResolution resolution;
            try { resolution = _resolver.Resolve(request.Item, cancellationToken); }
            catch (OperationCanceledException) { return new(LaunchStatus.Cancelled, LaunchReason.RequestCancelled); }
            catch (Exception) { return new(LaunchStatus.NoTrustedApplication, LaunchReason.ResolverFailure); }
            using var lease = resolution.Lease;
            if (resolution.Result.Status != LaunchStatus.Accepted) { return resolution.Result; }
            if (lease is null || !Matches(request.Item, lease.Target)) { return new(LaunchStatus.Denied, LaunchReason.UnsafeTarget); }
            try
            {
                var verified = lease.Revalidate();
                if (verified.Status != LaunchStatus.Accepted) { return verified; }
                gate = CheckCurrent(request, cancellationToken);
                if (gate.Status != LaunchStatus.Accepted) { return gate; }
                var result = _executor.Submit(lease, cancellationToken);
                return result.Status == LaunchStatus.Accepted ? new(LaunchStatus.OperatingSystemFailure, LaunchReason.ExecutorFailure) : result;
            }
            catch (OperationCanceledException) { return new(LaunchStatus.Cancelled, LaunchReason.RequestCancelled); }
            catch (Exception) { return new(LaunchStatus.OperatingSystemFailure, LaunchReason.ExecutorFailure); }
        }
    }

    private LaunchResult CheckCurrent(LaunchRequest request, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) { return new(LaunchStatus.Cancelled, LaunchReason.RequestCancelled); }
        long age = _clock.NowMilliseconds - request.Timestamp;
        if (age < 0 || age >= _requestLifetimeMilliseconds) { return new(LaunchStatus.Expired, LaunchReason.RequestExpired); }
        if (!_inputAllowed || request.ContextVersion != _contextVersion || request.Generation != _generation || !ReferenceEquals(_workspace, request.Workspace) ||
            !_workspace.Spaces.ContainsKey(request.SpaceId) || !_workspace.GetUsableItemIds(request.SpaceId).Contains(request.Item.Id) ||
            !_workspace.ItemStore.Items.TryGetValue(request.Item.Id, out var item) || !ReferenceEquals(item, request.Item))
        { return new(LaunchStatus.Expired, LaunchReason.StaleContext); }
        return LaunchPolicy.Check(request.Item);
    }

    private static bool Matches(Item item, ResolvedLaunchTarget target) => item switch
    {
        ApplicationItem app => target.Kind == LaunchTargetKind.Application && app.Identity == target.ApplicationIdentity && app.Provenance == target.Provenance,
        FileItem file => target.Kind == LaunchTargetKind.File && target.Target == file.Path.Value &&
            (file.StableFileIdentity is null || file.StableFileIdentity == target.ResourceIdentity),
        FolderItem folder => target.Kind == LaunchTargetKind.Folder && target.Target == folder.Path.Value &&
            (folder.StableFileIdentity is null || folder.StableFileIdentity == target.ResourceIdentity),
        UrlItem url => target.Kind == LaunchTargetKind.Url && target.Target == url.Destination.Value && LaunchPolicy.IsSafeUrl(target.Target),
        _ => false,
    };
}
