using System.Collections.Immutable;
using Zhuomian.Core.Interaction;

namespace Zhuomian.Core.WorkspaceModel;

/// <summary>Null override means automatic/reset. No capacity algorithm or product maximum is frozen.</summary>
public sealed record PreviewCapacityPolicy
{
    public PreviewCapacityPolicy(int? capacityOverride = null)
    {
        if (capacityOverride is < 0) { throw new ArgumentOutOfRangeException(nameof(capacityOverride)); }
        CapacityOverride = capacityOverride;
    }
    public int? CapacityOverride { get; }
    public bool IsAutomatic => CapacityOverride is null;
    public static PreviewCapacityPolicy Automatic { get; } = new();
}

public sealed class Space
{
    public Space(Guid id, string name, SpaceActivationPolicy activationPolicy,
        PreviewCapacityPolicy? previewPolicy = null, IEnumerable<Guid>? itemIds = null)
    {
        Id = DomainValidation.Id(id);
        Name = DomainValidation.Text(name);
        ArgumentNullException.ThrowIfNull(activationPolicy);
        ActivationPolicy = activationPolicy;
        PreviewPolicy = previewPolicy ?? PreviewCapacityPolicy.Automatic;
        // Raw snapshot references are preserved, including invalid ones. Workspace resolves/diagnoses them.
        ItemIds = itemIds?.ToImmutableArray() ?? [];
    }
    public Guid Id { get; }
    public string Name { get; }
    public SpaceActivationPolicy ActivationPolicy { get; }
    public PreviewCapacityPolicy PreviewPolicy { get; }
    public ImmutableArray<Guid> ItemIds { get; }
    internal Space WithReferences(ImmutableArray<Guid> itemIds) => new(Id, Name, ActivationPolicy, PreviewPolicy, itemIds);
    internal Space WithName(string name) => new(Id, name, ActivationPolicy, PreviewPolicy, ItemIds);
    internal Space WithPolicies(SpaceActivationPolicy activationPolicy, PreviewCapacityPolicy previewPolicy) =>
        new(Id, Name, activationPolicy, previewPolicy, ItemIds);
}

public enum ReferenceProblem { EmptyIdentity, MissingItem, GloballyRemovedItem, DuplicateReference }

public readonly record struct ReferenceDiagnostic(Guid SpaceId, Guid ItemId, int Index, ReferenceProblem Problem);

/// <summary>Separate command semantics; the application layer must obtain user confirmation first.</summary>
public enum GlobalRemovalConfirmation { Unconfirmed, Confirmed }
