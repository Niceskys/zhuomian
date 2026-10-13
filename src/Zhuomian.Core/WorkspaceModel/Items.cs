using System.Collections.Immutable;

namespace Zhuomian.Core.WorkspaceModel;

public enum ItemAvailability { Unverified, Available, Missing, Unsupported }

/// <summary>Closed resource family. Availability is an adapter observation, never launch permission.</summary>
public abstract class Item
{
    private protected Item(Guid id, ItemMetadata metadata, ItemAvailability availability)
    {
        Id = DomainValidation.Id(id);
        ArgumentNullException.ThrowIfNull(metadata);
        if (!Enum.IsDefined(availability)) { throw new ArgumentOutOfRangeException(nameof(availability)); }
        Metadata = metadata;
        Availability = availability;
    }
    public Guid Id { get; }
    public ItemMetadata Metadata { get; }
    public ItemAvailability Availability { get; }
    internal abstract object IdentityKey { get; }
    internal abstract Item WithAvailability(ItemAvailability availability);
}

public sealed class ApplicationItem : Item
{
    public ApplicationItem(Guid id, ItemMetadata metadata, ApplicationIdentity identity,
        ApplicationProvenance provenance, ItemAvailability availability = ItemAvailability.Unverified)
        : base(id, metadata, availability)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(provenance);
        Identity = identity;
        Provenance = provenance;
    }
    public ApplicationIdentity Identity { get; }
    public ApplicationProvenance Provenance { get; }
    internal override object IdentityKey => (typeof(ApplicationItem), Identity);
    internal override Item WithAvailability(ItemAvailability availability) => new ApplicationItem(Id, Metadata, Identity, Provenance, availability);
}

public sealed class FileItem : Item
{
    public FileItem(Guid id, ItemMetadata metadata, WindowsPath path, string? stableFileIdentity = null,
        ItemAvailability availability = ItemAvailability.Unverified) : base(id, metadata, availability)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        StableFileIdentity = stableFileIdentity is null ? null : DomainValidation.Text(stableFileIdentity);
    }
    public WindowsPath Path { get; }
    public string? StableFileIdentity { get; }
    internal override object IdentityKey => (typeof(FileItem), Path.CanonicalKey);
    internal override Item WithAvailability(ItemAvailability availability) => new FileItem(Id, Metadata, Path, StableFileIdentity, availability);
}

public sealed class FolderItem : Item
{
    public FolderItem(Guid id, ItemMetadata metadata, WindowsPath path, string? stableFileIdentity = null,
        ItemAvailability availability = ItemAvailability.Unverified) : base(id, metadata, availability)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
        StableFileIdentity = stableFileIdentity is null ? null : DomainValidation.Text(stableFileIdentity);
    }
    public WindowsPath Path { get; }
    public string? StableFileIdentity { get; }
    internal override object IdentityKey => (typeof(FolderItem), Path.CanonicalKey);
    internal override Item WithAvailability(ItemAvailability availability) => new FolderItem(Id, Metadata, Path, StableFileIdentity, availability);
}

public sealed class UrlItem : Item
{
    public UrlItem(Guid id, ItemMetadata metadata, HttpUrl destination,
        ItemAvailability availability = ItemAvailability.Unverified) : base(id, metadata, availability)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Destination = destination;
    }
    public HttpUrl Destination { get; }
    internal override object IdentityKey => (typeof(UrlItem), Destination.Value);
    internal override Item WithAvailability(ItemAvailability availability) => new UrlItem(Id, Metadata, Destination, availability);
}

/// <summary>Immutable root catalog. Removed records reserve their IDs and identities for recovery.</summary>
public sealed class ItemStore
{
    private ItemStore(ImmutableDictionary<Guid, Item> items, ImmutableDictionary<Guid, Item> removedItems)
    {
        Items = items;
        RemovedItems = removedItems;
    }
    public static ItemStore Empty { get; } = new(ImmutableDictionary<Guid, Item>.Empty, ImmutableDictionary<Guid, Item>.Empty);
    public ImmutableDictionary<Guid, Item> Items { get; }
    public ImmutableDictionary<Guid, Item> RemovedItems { get; }

    public static ItemStore FromSnapshot(IEnumerable<Item> items, IEnumerable<Item> removedItems)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(removedItems);
        var store = Empty;
        foreach (var item in items) { store = store.Add(item); }
        foreach (var item in removedItems) { store = store.Add(item).Remove(item.Id); }
        return store;
    }

    internal ItemStore Add(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Items.ContainsKey(item.Id) || RemovedItems.ContainsKey(item.Id)) { throw new WorkspaceOperationException(DomainError.IdentityAlreadyUsed); }
        if (Items.Values.Concat(RemovedItems.Values).Any(existing => existing.IdentityKey.Equals(item.IdentityKey)))
        {
            throw new WorkspaceOperationException(DomainError.ResourceAlreadyKnown);
        }
        return new(Items.Add(item.Id, item), RemovedItems);
    }

    internal Item Require(Guid id) => Items.TryGetValue(DomainValidation.Id(id), out var item)
        ? item : throw new WorkspaceOperationException(DomainError.ItemNotFound);

    internal ItemStore Remove(Guid id)
    {
        var item = Require(id);
        return new(Items.Remove(id), RemovedItems.Add(id, item));
    }

    internal ItemStore Restore(Guid id)
    {
        if (!RemovedItems.TryGetValue(DomainValidation.Id(id), out var item)) { throw new WorkspaceOperationException(DomainError.ItemNotFound); }
        return new(Items.Add(id, item), RemovedItems.Remove(id));
    }

    internal ItemStore SetAvailability(Guid id, ItemAvailability availability)
    {
        var item = Require(id);
        return new(Items.SetItem(id, item.WithAvailability(availability)), RemovedItems);
    }
}
