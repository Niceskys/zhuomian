using System.Collections.Immutable;
using Zhuomian.Core.Interaction;

namespace Zhuomian.Core.WorkspaceModel;

/// <summary>Immutable domain snapshots. No I/O, execution, serializer or interaction runtime ownership.</summary>
public sealed class Workspace
{
    public const int CurrentSchemaVersion = 1;

    private Workspace(Guid id, int schemaVersion, long revision, ItemStore itemStore,
        ImmutableDictionary<Guid, Space> spaces, ImmutableDictionary<Guid, Space> removedSpaces)
    {
        Id = id;
        SchemaVersion = schemaVersion;
        Revision = revision;
        ItemStore = itemStore;
        Spaces = spaces;
        RemovedSpaces = removedSpaces;
    }

    public Guid Id { get; }
    public int SchemaVersion { get; }
    public long Revision { get; }
    public ItemStore ItemStore { get; }
    public ImmutableDictionary<Guid, Space> Spaces { get; }
    public ImmutableDictionary<Guid, Space> RemovedSpaces { get; }

    public static Workspace Create(Guid id) => FromSnapshot(id, CurrentSchemaVersion, 0, ItemStore.Empty, []);

    /// <summary>Typed rehydration only. Unsupported root schema/duplicate IDs reject; no input is modified.</summary>
    public static Workspace FromSnapshot(Guid id, int schemaVersion, long revision, ItemStore itemStore,
        IEnumerable<Space> spaces, IEnumerable<Space>? removedSpaces = null)
    {
        DomainValidation.Id(id);
        if (schemaVersion != CurrentSchemaVersion) { throw new NotSupportedException("Unsupported workspace schema."); }
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ArgumentNullException.ThrowIfNull(itemStore);
        ArgumentNullException.ThrowIfNull(spaces);
        var active = IndexSpaces(spaces);
        var removed = IndexSpaces(removedSpaces ?? []);
        if (active.Keys.Any(removed.ContainsKey)) { throw new WorkspaceOperationException(DomainError.IdentityAlreadyUsed); }
        return new(id, schemaVersion, revision, itemStore, active, removed);
    }

    private static ImmutableDictionary<Guid, Space> IndexSpaces(IEnumerable<Space> spaces)
    {
        var builder = ImmutableDictionary.CreateBuilder<Guid, Space>();
        foreach (var space in spaces)
        {
            ArgumentNullException.ThrowIfNull(space);
            if (!builder.TryAdd(space.Id, space)) { throw new WorkspaceOperationException(DomainError.IdentityAlreadyUsed); }
        }
        return builder.ToImmutable();
    }

    private Workspace Next(ItemStore? store = null, ImmutableDictionary<Guid, Space>? spaces = null,
        ImmutableDictionary<Guid, Space>? removedSpaces = null) =>
        new(Id, SchemaVersion, checked(Revision + 1), store ?? ItemStore, spaces ?? Spaces, removedSpaces ?? RemovedSpaces);

    private Space RequireSpace(Guid id) => Spaces.TryGetValue(DomainValidation.Id(id), out var space)
        ? space : throw new WorkspaceOperationException(DomainError.SpaceNotFound);

    public Workspace CreateSpace(Guid id, string name, SpaceActivationPolicy activationPolicy,
        PreviewCapacityPolicy? previewPolicy = null)
    {
        var space = new Space(id, name, activationPolicy, previewPolicy);
        if (Spaces.ContainsKey(id) || RemovedSpaces.ContainsKey(id)) { throw new WorkspaceOperationException(DomainError.IdentityAlreadyUsed); }
        return Next(spaces: Spaces.Add(id, space));
    }

    public Workspace RenameSpace(Guid id, string name)
    {
        var space = RequireSpace(id);
        var replacement = space.WithName(name);
        return replacement.Name == space.Name ? this : Next(spaces: Spaces.SetItem(id, replacement));
    }

    public Workspace DeleteSpace(Guid id)
    {
        var space = RequireSpace(id);
        return Next(spaces: Spaces.Remove(id), removedSpaces: RemovedSpaces.Add(id, space));
    }

    public Workspace ConfigureSpace(Guid id, SpaceActivationPolicy activationPolicy, PreviewCapacityPolicy previewPolicy)
    {
        var space = RequireSpace(id);
        ArgumentNullException.ThrowIfNull(activationPolicy);
        ArgumentNullException.ThrowIfNull(previewPolicy);
        return activationPolicy == space.ActivationPolicy && previewPolicy == space.PreviewPolicy ? this :
            Next(spaces: Spaces.SetItem(id, space.WithPolicies(activationPolicy, previewPolicy)));
    }

    public Workspace AddItem(Item item) => Next(store: ItemStore.Add(item));

    /// <summary>Atomic catalog addition and binding, suitable for a future validated drop transaction.</summary>
    public Workspace AddItemToSpace(Guid spaceId, Item item, int? index = null)
    {
        var space = RequireSpace(spaceId);
        var store = ItemStore.Add(item);
        var updated = InsertReference(space, item.Id, index);
        return Next(store, Spaces.SetItem(spaceId, updated));
    }

    public Workspace AddReference(Guid spaceId, Guid itemId, int? index = null)
    {
        var space = RequireSpace(spaceId);
        ItemStore.Require(itemId);
        return Next(spaces: Spaces.SetItem(spaceId, InsertReference(space, itemId, index)));
    }

    private static Space InsertReference(Space space, Guid itemId, int? index)
    {
        if (space.ItemIds.Contains(itemId)) { throw new WorkspaceOperationException(DomainError.ReferenceAlreadyExists); }
        int position = index ?? space.ItemIds.Length;
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, space.ItemIds.Length);
        return space.WithReferences(space.ItemIds.Insert(position, itemId));
    }

    /// <summary>Only this Space is unbound. Also explicitly repairs all raw occurrences of a quarantined ID.</summary>
    public Workspace RemoveReference(Guid spaceId, Guid itemId)
    {
        var space = RequireSpace(spaceId);
        if (!space.ItemIds.Contains(itemId)) { throw new WorkspaceOperationException(DomainError.ReferenceNotFound); }
        return Next(spaces: Spaces.SetItem(spaceId, space.WithReferences(space.ItemIds.RemoveAll(id => id == itemId))));
    }

    /// <summary>Index denotes final position, after removal. No change to catalog or other Spaces.</summary>
    public Workspace MoveReference(Guid spaceId, Guid itemId, int index)
    {
        var space = RequireSpace(spaceId);
        ItemStore.Require(itemId);
        int oldIndex = space.ItemIds.IndexOf(itemId);
        if (oldIndex < 0) { throw new WorkspaceOperationException(DomainError.ReferenceNotFound); }
        if (space.ItemIds.Count(id => id == itemId) != 1) { throw new WorkspaceOperationException(DomainError.InvalidOrder); }
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, space.ItemIds.Length);
        return index == oldIndex ? this : Next(spaces: Spaces.SetItem(spaceId,
            space.WithReferences(space.ItemIds.RemoveAt(oldIndex).Insert(index, itemId))));
    }

    /// <summary>Atomic cross-Space transfer; destination duplicate rejects without unbinding the source.</summary>
    public Workspace MoveReference(Guid sourceSpaceId, Guid targetSpaceId, Guid itemId, int? index = null)
    {
        if (sourceSpaceId == targetSpaceId)
        {
            var space = RequireSpace(sourceSpaceId);
            return MoveReference(sourceSpaceId, itemId, index ?? space.ItemIds.Length - 1);
        }
        var source = RequireSpace(sourceSpaceId);
        var target = RequireSpace(targetSpaceId);
        ItemStore.Require(itemId);
        if (!source.ItemIds.Contains(itemId)) { throw new WorkspaceOperationException(DomainError.ReferenceNotFound); }
        if (source.ItemIds.Count(id => id == itemId) != 1) { throw new WorkspaceOperationException(DomainError.InvalidOrder); }
        var destination = InsertReference(target, itemId, index);
        var origin = source.WithReferences(source.ItemIds.RemoveAll(id => id == itemId));
        return Next(spaces: Spaces.SetItem(sourceSpaceId, origin).SetItem(targetSpaceId, destination));
    }

    public Workspace ReorderReferences(Guid spaceId, IEnumerable<Guid> orderedIds)
    {
        var space = RequireSpace(spaceId);
        ArgumentNullException.ThrowIfNull(orderedIds);
        var order = orderedIds.ToImmutableArray();
        if (space.ItemIds.Distinct().Count() != space.ItemIds.Length || order.Length != space.ItemIds.Length ||
            order.Distinct().Count() != order.Length || !space.ItemIds.ToHashSet().SetEquals(order))
        {
            throw new WorkspaceOperationException(DomainError.InvalidOrder);
        }
        return order.SequenceEqual(space.ItemIds) ? this : Next(spaces: Spaces.SetItem(spaceId, space.WithReferences(order)));
    }

    public Workspace RemoveItemGlobally(Guid itemId, GlobalRemovalConfirmation confirmation)
    {
        if (confirmation != GlobalRemovalConfirmation.Confirmed) { throw new WorkspaceOperationException(DomainError.ConfirmationRequired); }
        var store = ItemStore.Remove(itemId);
        var spaces = Spaces;
        foreach (var (id, space) in Spaces)
        {
            if (space.ItemIds.Contains(itemId)) { spaces = spaces.SetItem(id, space.WithReferences(space.ItemIds.RemoveAll(reference => reference == itemId))); }
        }
        // Removed Spaces and old snapshots intentionally retain recovery references.
        return Next(store, spaces);
    }

    public Workspace RestoreItem(Guid itemId) => Next(store: ItemStore.Restore(itemId));

    public Workspace SetItemAvailability(Guid itemId, ItemAvailability availability)
    {
        var item = ItemStore.Require(itemId);
        if (!Enum.IsDefined(availability)) { throw new ArgumentOutOfRangeException(nameof(availability)); }
        return item.Availability == availability ? this : Next(store: ItemStore.SetAvailability(itemId, availability));
    }

    public ImmutableArray<ReferenceDiagnostic> DiagnoseReferences(Guid spaceId)
    {
        var space = RequireSpace(spaceId);
        var diagnostics = ImmutableArray.CreateBuilder<ReferenceDiagnostic>();
        var seen = new HashSet<Guid>();
        for (int i = 0; i < space.ItemIds.Length; i++)
        {
            Guid id = space.ItemIds[i];
            ReferenceProblem? problem = id == Guid.Empty ? ReferenceProblem.EmptyIdentity :
                !seen.Add(id) ? ReferenceProblem.DuplicateReference :
                ItemStore.RemovedItems.ContainsKey(id) ? ReferenceProblem.GloballyRemovedItem :
                !ItemStore.Items.ContainsKey(id) ? ReferenceProblem.MissingItem : null;
            if (problem is { } value) { diagnostics.Add(new(spaceId, id, i, value)); }
        }
        return diagnostics.ToImmutable();
    }

    /// <summary>Ordered usable view; raw quarantined references remain in Space.ItemIds for recovery.</summary>
    public ImmutableArray<Guid> GetUsableItemIds(Guid spaceId)
    {
        var space = RequireSpace(spaceId);
        var excluded = DiagnoseReferences(spaceId).Select(d => d.Index).ToHashSet();
        return space.ItemIds.Where((_, index) => !excluded.Contains(index)).ToImmutableArray();
    }

    public int GetReferenceCount(Guid itemId)
    {
        ItemStore.Require(itemId);
        return Spaces.Values.Sum(space => space.ItemIds.Count(id => id == itemId));
    }

    /// <summary>Retention inventory includes active, unreferenced and globally removed items. Never a GC permit.</summary>
    public ImmutableHashSet<Guid> GetRetainedManagedAssetIds() => ItemStore.Items.Values.Concat(ItemStore.RemovedItems.Values)
        .Select(item => item.Metadata.Icon).OfType<ManagedAssetReference>().Select(asset => asset.AssetId).ToImmutableHashSet();

    /// <summary>One-way projection to the existing controller; canonical opaque IDs only.</summary>
    public ImmutableDictionary<string, SpaceActivationPolicy> GetInteractionPolicies() =>
        Spaces.Values.ToImmutableDictionary(space => space.Id.ToString("D"), space => space.ActivationPolicy, StringComparer.Ordinal);
}
