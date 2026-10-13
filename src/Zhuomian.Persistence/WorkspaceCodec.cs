using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence;

/// <summary>First disk format, independent of the domain schema. Explicit fields; no polymorphic deserialization.</summary>
public static class WorkspaceCodec
{
    public const int CurrentFormatVersion = 1;
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = PersistenceLimits.MaxDepth };
    private static readonly HashSet<string> Forbidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "$type", "$id", "$ref", "commandLine", "arguments", "launchRequest", "launchPermission", "armed",
        "focus", "hover", "animationProgress", "searchText", "keyboardMode", "pointerCapture", "storageRoot",
    };

    public static byte[] Encode(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ValidateSnapshot(workspace);
        var payload = new WorkspaceDto(workspace.Id, workspace.SchemaVersion, workspace.Revision,
            workspace.Spaces.Values.OrderBy(s => s.Id).Select(SpaceData).ToArray(),
            workspace.RemovedSpaces.Values.OrderBy(s => s.Id).Select(SpaceData).ToArray(),
            workspace.ItemStore.Items.Values.OrderBy(i => i.Id).Select(ItemData).ToArray(),
            workspace.ItemStore.RemovedItems.Values.OrderBy(i => i.Id).Select(ItemData).ToArray());
        byte[] payloadBytes = SerializeBounded(payload);
        using var document = JsonDocument.Parse(payloadBytes);
        byte[] bytes = SerializeBounded(new EnvelopeDto(CurrentFormatVersion,
            Convert.ToHexString(SHA256.HashData(payloadBytes)), document.RootElement));
        if (bytes.Length > PersistenceLimits.MaxFileBytes) { throw new StorageException(StorageError.TooLarge); }
        var check = Decode(bytes);
        if (check.Error != StorageError.None || check.IsReadOnly) { throw new StorageException(check.Error == StorageError.None ? StorageError.ReadOnly : check.Error); }
        return bytes;
    }

    private static byte[] SerializeBounded<T>(T value)
    {
        using var stream = new LimitedStream();
        JsonSerializer.Serialize(stream, value, Options);
        return stream.ToArray();
    }
    private sealed class LimitedStream : MemoryStream
    {
        private void Ensure(int count)
        {
            if (Length + count > PersistenceLimits.MaxFileBytes) { throw new StorageException(StorageError.TooLarge); }
        }
        public override void Write(byte[] buffer, int offset, int count) { Ensure(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Ensure(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Ensure(1); base.WriteByte(value); }
    }
    private static void ValidateSnapshot(Workspace workspace)
    {
        if (workspace.ItemStore.Items.Count + workspace.ItemStore.RemovedItems.Count > PersistenceLimits.MaxItems ||
            workspace.Spaces.Count + workspace.RemovedSpaces.Count > PersistenceLimits.MaxSpaces)
        { throw new StorageException(StorageError.TooLarge); }
        long references = 0;
        foreach (var space in workspace.Spaces.Values.Concat(workspace.RemovedSpaces.Values))
        {
            CheckText(space.Name);
            references += space.ItemIds.Length;
            if (space.ItemIds.Length > PersistenceLimits.MaxArrayElements || references > PersistenceLimits.MaxTotalReferences)
            { throw new StorageException(StorageError.TooLarge); }
        }
        foreach (var item in workspace.ItemStore.Items.Values.Concat(workspace.ItemStore.RemovedItems.Values))
        {
            CheckText(item.Metadata.DisplayName);
            if (item.Metadata.Icon is ExternalResourceReference external) { CheckText(external.Path.Value); }
            switch (item)
            {
                case ApplicationItem app:
                    CheckText(app.Identity.ProviderId); CheckText(app.Identity.ApplicationId); CheckText(app.Provenance.SourceId); break;
                case FileItem file: CheckText(file.Path.Value); CheckText(file.StableFileIdentity); break;
                case FolderItem folder: CheckText(folder.Path.Value); CheckText(folder.StableFileIdentity); break;
                case UrlItem url: CheckText(url.Destination.Value); break;
            }
        }
    }
    private static void CheckText(string? text)
    {
        if (text?.Length > PersistenceLimits.MaxStringLength) { throw new StorageException(StorageError.TooLarge); }
        if (text is null) { return; }
        for (int index = 0; index < text.Length; index++)
        {
            if (!char.IsSurrogate(text[index])) { continue; }
            if (!char.IsHighSurrogate(text[index]) || index + 1 >= text.Length || !char.IsLowSurrogate(text[index + 1]))
            { throw new StorageException(StorageError.InvalidStructure); }
            index++;
        }
    }

    public static RecoveryResult Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > PersistenceLimits.MaxFileBytes) { return Failure(StorageError.TooLarge); }
        try
        {
            using var doc = JsonDocument.Parse(bytes, new() { MaxDepth = PersistenceLimits.MaxDepth });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            { ValidateTree(root); throw new StorageException(StorageError.InvalidStructure); }
            ValidateHeader(root);
            int format = root.GetProperty("formatVersion").GetInt32();
            if (format != CurrentFormatVersion) { return Failure(StorageError.UnsupportedVersion); }
            var payload = root.GetProperty("payload");
            ValidateHeader(payload);
            int schema = payload.GetProperty("schemaVersion").GetInt32();
            if (schema != Workspace.CurrentSchemaVersion) { return Failure(StorageError.UnsupportedVersion); }
            // Unknown version headers are protected before interpreting their future fields as v1 corruption.
            ValidateTree(root);
            bool unknown = HasUnknown(root, "formatVersion", "sha256", "payload");
            string digest = Text(root, "sha256");
            string actual = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.GetRawText())));
            if (!StringComparer.Ordinal.Equals(actual, digest)) { return Failure(StorageError.IntegrityFailure); }
            unknown |= HasUnknown(payload, "id", "schemaVersion", "revision", "spaces", "removedSpaces", "items", "removedItems");
            var quarantine = ImmutableArray.CreateBuilder<QuarantinedObject>();
            long referenceCount = 0;
            foreach (string collection in new[] { "spaces", "removedSpaces" })
            {
                foreach (var space in Array(payload, collection, PersistenceLimits.MaxSpaces).EnumerateArray())
                {
                    if (space.ValueKind == JsonValueKind.Object && space.TryGetProperty("itemIds", out var references) && references.ValueKind == JsonValueKind.Array)
                    { referenceCount += references.GetArrayLength(); }
                }
            }
            if (referenceCount > PersistenceLimits.MaxTotalReferences) { throw new StorageException(StorageError.TooLarge); }
            if (Array(payload, "items", PersistenceLimits.MaxItems).GetArrayLength() + Array(payload, "removedItems", PersistenceLimits.MaxItems).GetArrayLength() > PersistenceLimits.MaxItems ||
                Array(payload, "spaces", PersistenceLimits.MaxSpaces).GetArrayLength() + Array(payload, "removedSpaces", PersistenceLimits.MaxSpaces).GetArrayLength() > PersistenceLimits.MaxSpaces)
            { throw new StorageException(StorageError.TooLarge); }
            var ids = new HashSet<Guid>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var items = ReadItems(payload, "items", ids, identities, quarantine, ref unknown);
            var removedItems = ReadItems(payload, "removedItems", ids, identities, quarantine, ref unknown);
            if (ids.Count > PersistenceLimits.MaxItems) { throw new StorageException(StorageError.TooLarge); }
            ids.Clear();
            var spaces = ReadSpaces(payload, "spaces", ids, quarantine, ref unknown);
            var removedSpaces = ReadSpaces(payload, "removedSpaces", ids, quarantine, ref unknown);
            if (ids.Count > PersistenceLimits.MaxSpaces) { throw new StorageException(StorageError.TooLarge); }
            var workspace = Workspace.FromSnapshot(Id(payload), schema, payload.GetProperty("revision").GetInt64(),
                ItemStore.FromSnapshot(items, removedItems), spaces, removedSpaces);
            foreach (var space in workspace.Spaces.Values)
            {
                foreach (var diagnostic in workspace.DiagnoseReferences(space.Id))
                { quarantine.Add(new("references", diagnostic.Index, QuarantineReason.DanglingReference, "")); }
            }
            foreach (var space in workspace.RemovedSpaces.Values)
            {
                var seen = new HashSet<Guid>();
                for (int index = 0; index < space.ItemIds.Length; index++)
                {
                    Guid id = space.ItemIds[index];
                    if (!seen.Add(id) || id == Guid.Empty ||
                        (!workspace.ItemStore.Items.ContainsKey(id) && !workspace.ItemStore.RemovedItems.ContainsKey(id)))
                    { quarantine.Add(new("removedReferences", index, QuarantineReason.DanglingReference, "")); }
                }
            }
            if (unknown) { quarantine.Add(new("extensions", 0, QuarantineReason.UnknownField, "")); }
            bool partial = quarantine.Count > 0;
            return new(partial ? RecoveryState.PartiallyQuarantined : RecoveryState.Normal, StorageError.None,
                workspace, quarantine.ToImmutable(), readOnly: partial);
        }
        catch (JsonException) { return Failure(StorageError.InvalidJson); }
        catch (StorageException e) { return Failure(e.Error); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or WorkspaceOperationException)
        { return Failure(StorageError.InvalidStructure); }
    }

    private static RecoveryResult Failure(StorageError error) => new(error == StorageError.UnsupportedVersion ?
        RecoveryState.UnsupportedVersion : RecoveryState.Unrecoverable, error, readOnly: true);

    private static List<Item> ReadItems(JsonElement payload, string collection, HashSet<Guid> ids,
        HashSet<string> identities, ImmutableArray<QuarantinedObject>.Builder quarantine, ref bool unknown)
    {
        var array = Array(payload, collection, PersistenceLimits.MaxItems);
        var items = new List<Item>();
        int index = 0;
        foreach (var data in array.EnumerateArray())
        {
            CheckObjectSize(data);
            // IDs are root structure: ambiguity across active/removed records rejects the entire input.
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var idValue) &&
                idValue.ValueKind == JsonValueKind.String && idValue.TryGetGuid(out var rawId) && rawId != Guid.Empty && !ids.Add(rawId))
            { throw new StorageException(StorageError.InvalidStructure); }
            try
            {
                Guid id = Id(data);
                string kind = Text(data, "kind");
                if (kind is not ("application" or "file" or "folder" or "url"))
                { quarantine.Add(new(collection, index++, QuarantineReason.UnsupportedKind, data.GetRawText())); continue; }
                // Fields belonging to a different known kind cannot be silently ignored.
                foreach (string field in kind switch
                {
                    "application" => new[] { "path", "stableFileIdentity", "destination" },
                    "file" or "folder" => new[] { "identity", "provenance", "destination" },
                    _ => new[] { "identity", "provenance", "path", "stableFileIdentity" },
                })
                {
                    if (data.TryGetProperty(field, out var incompatible) && incompatible.ValueKind != JsonValueKind.Null)
                    { throw new ArgumentException("Conflicting kind fields."); }
                }
                unknown |= HasUnknown(data, "id", "kind", "availability", "metadata", "identity", "provenance", "path", "stableFileIdentity", "destination");
                var metadata = data.GetProperty("metadata");
                unknown |= HasUnknown(metadata, "displayName", "icon");
                ResourceReference? icon = null;
                var iconData = metadata.GetProperty("icon");
                if (iconData.ValueKind != JsonValueKind.Null)
                {
                    unknown |= HasUnknown(iconData, "kind", "path", "assetId");
                    string iconKind = Text(iconData, "kind");
                    string incompatibleField = iconKind == "managed" ? "path" : "assetId";
                    if (iconData.TryGetProperty(incompatibleField, out var incompatibleIcon) && incompatibleIcon.ValueKind != JsonValueKind.Null)
                    { throw new ArgumentException("Conflicting icon fields."); }
                    icon = iconKind switch
                    {
                        "external" => new ExternalResourceReference(new WindowsPath(Text(iconData, "path"))),
                        "managed" => new ManagedAssetReference(iconData.GetProperty("assetId").GetGuid()),
                        _ => throw new ArgumentException("Invalid icon kind."),
                    };
                }
                var meta = new ItemMetadata(Text(metadata, "displayName"), icon);
                var availability = EnumValue<ItemAvailability>(data, "availability");
                Item item;
                string key;
                switch (kind)
                {
                    case "application":
                        var identity = data.GetProperty("identity");
                        var provenance = data.GetProperty("provenance");
                        unknown |= HasUnknown(identity, "providerId", "applicationId") | HasUnknown(provenance, "origin", "sourceId");
                        var application = new ApplicationItem(id, meta, new(Text(identity, "providerId"), Text(identity, "applicationId")),
                            new(EnumValue<ApplicationOrigin>(provenance, "origin"), Text(provenance, "sourceId")), availability);
                        item = application;
                        // Length-prefix avoids delimiter collisions in opaque identity tokens.
                        key = "application:" + application.Identity.ProviderId.Length + ":" + application.Identity.ProviderId + application.Identity.ApplicationId;
                        break;
                    case "file":
                        var file = new FileItem(id, meta, new(Text(data, "path")), OptionalText(data, "stableFileIdentity"), availability);
                        item = file; key = "file:" + file.Path.CanonicalKey; break;
                    case "folder":
                        var folder = new FolderItem(id, meta, new(Text(data, "path")), OptionalText(data, "stableFileIdentity"), availability);
                        item = folder; key = "folder:" + folder.Path.CanonicalKey; break;
                    default:
                        var url = new UrlItem(id, meta, new(Text(data, "destination")), availability);
                        item = url; key = "url:" + url.Destination.Value; break;
                }
                if (!identities.Add(key)) { quarantine.Add(new(collection, index, QuarantineReason.DuplicateIdentity, data.GetRawText())); }
                else { items.Add(item); }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            { quarantine.Add(new(collection, index, QuarantineReason.InvalidObject, data.GetRawText())); }
            index++;
        }
        return items;
    }

    private static List<Space> ReadSpaces(JsonElement payload, string collection, HashSet<Guid> ids,
        ImmutableArray<QuarantinedObject>.Builder quarantine, ref bool unknown)
    {
        var array = Array(payload, collection, PersistenceLimits.MaxSpaces);
        var spaces = new List<Space>();
        int index = 0;
        foreach (var data in array.EnumerateArray())
        {
            CheckObjectSize(data);
            if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("id", out var idValue) &&
                idValue.ValueKind == JsonValueKind.String && idValue.TryGetGuid(out var rawId) && rawId != Guid.Empty && !ids.Add(rawId))
            { throw new StorageException(StorageError.InvalidStructure); }
            try
            {
                unknown |= HasUnknown(data, "id", "name", "itemIds", "activationPolicy", "capacityOverride");
                var policy = data.GetProperty("activationPolicy");
                unknown |= HasUnknown(policy, "mode", "hoverDelayMilliseconds");
                var capacity = data.GetProperty("capacityOverride");
                spaces.Add(new(Id(data), Text(data, "name"), new(EnumValue<ActivationMode>(policy, "mode"),
                    policy.GetProperty("hoverDelayMilliseconds").GetInt32()),
                    new(capacity.ValueKind == JsonValueKind.Null ? null : capacity.GetInt32()),
                    Array(data, "itemIds", PersistenceLimits.MaxArrayElements).EnumerateArray().Select(v => v.GetGuid())));
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
            { quarantine.Add(new(collection, index, QuarantineReason.InvalidObject, data.GetRawText())); }
            catch (StorageException e) when (e.Error == StorageError.InvalidStructure)
            { quarantine.Add(new(collection, index, QuarantineReason.InvalidObject, data.GetRawText())); }
            index++;
        }
        return spaces;
    }

    internal static void ValidateMigrationPayload(JsonElement payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload.GetRawText());
        if (bytes.Length > PersistenceLimits.MaxFileBytes) { throw new StorageException(StorageError.TooLarge); }
        using var document = JsonDocument.Parse(bytes, new() { MaxDepth = PersistenceLimits.MaxDepth });
        ValidateTree(document.RootElement);
    }

    private static void CheckObjectSize(JsonElement data)
    {
        if (Encoding.UTF8.GetByteCount(data.GetRawText()) > PersistenceLimits.MaxObjectBytes) { throw new StorageException(StorageError.TooLarge); }
    }
    private static JsonElement Array(JsonElement data, string field, int maximum)
    {
        var array = data.GetProperty(field);
        if (array.ValueKind != JsonValueKind.Array) { throw new StorageException(StorageError.InvalidStructure); }
        if (array.GetArrayLength() > maximum) { throw new StorageException(StorageError.TooLarge); }
        return array;
    }
    private static Guid Id(JsonElement data)
    {
        Guid id = data.GetProperty("id").GetGuid();
        return id != Guid.Empty ? id : throw new ArgumentException("Empty identity.");
    }
    private static string Text(JsonElement data, string field) => data.GetProperty(field).GetString() ?? throw new ArgumentException("Null text.");
    private static string? OptionalText(JsonElement data, string field) => data.GetProperty(field).GetString();
    private static T EnumValue<T>(JsonElement data, string field) where T : struct, Enum
    {
        string text = Text(data, field);
        return Enum.TryParse<T>(text, out var result) && Enum.IsDefined(result) && result.ToString() == text ? result : throw new ArgumentException("Invalid enum.");
    }
    private static bool HasUnknown(JsonElement data, params string[] fields)
    {
        if (data.ValueKind != JsonValueKind.Object) { throw new ArgumentException("Object required."); }
        return data.EnumerateObject().Any(p => !fields.Contains(p.Name, StringComparer.Ordinal));
    }
    private static void ValidateHeader(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object) { throw new StorageException(StorageError.InvalidStructure); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in data.EnumerateObject())
        {
            if (!names.Add(property.Name)) { throw new StorageException(StorageError.InvalidStructure); }
            if (names.Count > PersistenceLimits.MaxProperties || property.Name.Length > PersistenceLimits.MaxStringLength)
            { throw new StorageException(StorageError.TooLarge); }
        }
    }

    private static void ValidateTree(JsonElement data)
    {
        switch (data.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var p in data.EnumerateObject())
                {
                    if (!names.Add(p.Name) || Forbidden.Contains(p.Name)) { throw new StorageException(StorageError.InvalidStructure); }
                    if (names.Count > PersistenceLimits.MaxProperties || p.Name.Length > PersistenceLimits.MaxStringLength) { throw new StorageException(StorageError.TooLarge); }
                    ValidateTree(p.Value);
                }
                break;
            case JsonValueKind.Array:
                if (data.GetArrayLength() > PersistenceLimits.MaxArrayElements) { throw new StorageException(StorageError.TooLarge); }
                foreach (var value in data.EnumerateArray()) { ValidateTree(value); }
                break;
            case JsonValueKind.String:
                if (data.GetString()!.Length > PersistenceLimits.MaxStringLength) { throw new StorageException(StorageError.TooLarge); }
                break;
        }
    }

    private static SpaceDto SpaceData(Space space) => new(space.Id, space.Name, space.ItemIds.ToArray(),
        new(space.ActivationPolicy.Mode.ToString(), space.ActivationPolicy.HoverDelayMilliseconds), space.PreviewPolicy.CapacityOverride);
    private static ItemDto ItemData(Item item)
    {
        IconDto? icon = item.Metadata.Icon switch
        {
            ExternalResourceReference external => new("external", external.Path.Value, null),
            ManagedAssetReference managed => new("managed", null, managed.AssetId),
            _ => null,
        };
        var metadata = new MetadataDto(item.Metadata.DisplayName, icon);
        return item switch
        {
            ApplicationItem app => new(item.Id, "application", item.Availability.ToString(), metadata,
                new(app.Identity.ProviderId, app.Identity.ApplicationId), new(app.Provenance.Origin.ToString(), app.Provenance.SourceId), null, null, null),
            FileItem file => new(item.Id, "file", item.Availability.ToString(), metadata, null, null, file.Path.Value, file.StableFileIdentity, null),
            FolderItem folder => new(item.Id, "folder", item.Availability.ToString(), metadata, null, null, folder.Path.Value, folder.StableFileIdentity, null),
            UrlItem url => new(item.Id, "url", item.Availability.ToString(), metadata, null, null, null, null, url.Destination.Value),
            _ => throw new ArgumentException("Unsupported Item."),
        };
    }

    private sealed record EnvelopeDto(int FormatVersion, string Sha256, JsonElement Payload);
    private sealed record WorkspaceDto(Guid Id, int SchemaVersion, long Revision, SpaceDto[] Spaces, SpaceDto[] RemovedSpaces, ItemDto[] Items, ItemDto[] RemovedItems);
    private sealed record SpaceDto(Guid Id, string Name, Guid[] ItemIds, PolicyDto ActivationPolicy, int? CapacityOverride);
    private sealed record PolicyDto(string Mode, int HoverDelayMilliseconds);
    private sealed record ItemDto(Guid Id, string Kind, string Availability, MetadataDto Metadata, IdentityDto? Identity, ProvenanceDto? Provenance, string? Path, string? StableFileIdentity, string? Destination);
    private sealed record MetadataDto(string DisplayName, IconDto? Icon);
    private sealed record IconDto(string Kind, string? Path, Guid? AssetId);
    private sealed record IdentityDto(string ProviderId, string ApplicationId);
    private sealed record ProvenanceDto(string Origin, string SourceId);
}
