using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Launching;

public enum LaunchStatus
{
    Accepted, SubmittedToOperatingSystem, MissingOrInaccessible, NoTrustedApplication,
    Unsupported, Denied, OperatingSystemFailure, Cancelled, Expired, Duplicate,
}

public enum LaunchReason
{
    None, InvalidReference, StaleContext, UnsafeTarget, UntrustedProvider, ResourceUnavailable,
    ResourceReplaced, UnreviewedAssociation, PlatformUnavailable, ResolverFailure, ExecutorFailure,
    RequestCancelled, RequestExpired, RequestConsumed,
}

/// <summary>Safe feedback only. Never carries paths, names, URLs, exception messages or OS payloads.</summary>
public readonly record struct LaunchResult(LaunchStatus Status, LaunchReason Reason = LaunchReason.None);

public enum LaunchTargetKind { Application, File, Folder, Url }

/// <summary>Trusted in-process adapter output, not a configuration/import format or a bearer permission.</summary>
public sealed class ResolvedLaunchTarget(
    LaunchTargetKind kind, string target, string resourceIdentity,
    ApplicationIdentity? applicationIdentity = null, ApplicationProvenance? provenance = null)
{
    public LaunchTargetKind Kind { get; } = kind;
    public string Target { get; } = target;
    public string ResourceIdentity { get; } = resourceIdentity;
    public ApplicationIdentity? ApplicationIdentity { get; } = applicationIdentity;
    public ApplicationProvenance? Provenance { get; } = provenance;
    public override string ToString() => nameof(ResolvedLaunchTarget);
}

/// <summary>Owns resource verification/pinning until execution completes. No user arguments exist.</summary>
public interface ILaunchLease : IDisposable
{
    ResolvedLaunchTarget Target { get; }
    LaunchResult Revalidate();
}

public readonly record struct LaunchResolution(LaunchResult Result, ILaunchLease? Lease = null);

/// <summary>Inject only reviewed adapters at composition. Item provenance alone never selects code.</summary>
public interface ILaunchIdentityResolver
{
    LaunchResolution Resolve(Item item, CancellationToken cancellationToken);
}

public interface ILaunchExecutor
{
    LaunchResult Submit(ILaunchLease lease, CancellationToken cancellationToken);
}

public readonly record struct LaunchAdmission(LaunchResult Result, LaunchRequest? Request = null);

/// <summary>One-shot, session-owned request bound to the exact snapshot and input generation at down.</summary>
public sealed class LaunchRequest
{
    internal LaunchRequest(object owner, Workspace workspace, Guid spaceId, Item item, long generation, long timestamp, long contextVersion)
    {
        Owner = owner;
        Workspace = workspace;
        SpaceId = spaceId;
        Item = item;
        Generation = generation;
        Timestamp = timestamp;
        ContextVersion = contextVersion;
    }
    internal object Owner { get; }
    internal Workspace Workspace { get; }
    internal Guid SpaceId { get; }
    internal Item Item { get; }
    internal long Generation { get; }
    internal long Timestamp { get; }
    internal long ContextVersion { get; }
    internal int Consumed;
    public override string ToString() => nameof(LaunchRequest);
}
