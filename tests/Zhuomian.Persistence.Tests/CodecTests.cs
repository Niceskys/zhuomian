using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Persistence.Tests;

public sealed class CodecTests
{
    [Fact]
    public void CompleteRoundTripPreservesFourKindsSharedRootOrderRemovedAndPolicies()
    {
        var original = Samples.Full();
        byte[] bytes = WorkspaceCodec.Encode(original);
        var result = WorkspaceCodec.Decode(bytes);
        Assert.Equal(RecoveryState.Normal, result.State);
        Assert.False(result.IsReadOnly);
        var restored = result.Workspace!;
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Revision, restored.Revision);
        Assert.Equal(original.SchemaVersion, restored.SchemaVersion);
        Assert.Equal(bytes, WorkspaceCodec.Encode(restored));
        Assert.Equal(original.Spaces[Samples.SpaceA].ItemIds.ToArray(), restored.Spaces[Samples.SpaceA].ItemIds.ToArray());
        Assert.Equal(Samples.FileId, Assert.Single(restored.Spaces[Samples.SpaceB].ItemIds));
        Assert.Equal(4, restored.ItemStore.Items.Count);
        Assert.Single(restored.ItemStore.RemovedItems);
        Assert.Single(restored.RemovedSpaces);
        Assert.Equal(0, restored.Spaces[Samples.SpaceA].PreviewPolicy.CapacityOverride);
        Assert.True(restored.Spaces[Samples.SpaceB].PreviewPolicy.IsAutomatic);
        Assert.Equal(120, restored.Spaces[Samples.SpaceA].ActivationPolicy.HoverDelayMilliseconds);
        Assert.Equal(ItemAvailability.Missing, restored.ItemStore.Items[Samples.FileId].Availability);
        Assert.IsType<ApplicationItem>(restored.ItemStore.Items.Values.Single(i => i is ApplicationItem));
        Assert.Single(restored.GetRetainedManagedAssetIds());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"formatVersion\":1}")]
    public void InvalidRootNeverCreatesAnEmptyWorkspace(string text)
    {
        var result = WorkspaceCodec.Decode(Encoding.UTF8.GetBytes(text));
        Assert.Null(result.Workspace);
        Assert.NotEqual(StorageError.None, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(999)]
    public void UnsupportedDiskVersionIsExplicit(int version)
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r => r["formatVersion"] = version));
        Assert.Equal(RecoveryState.UnsupportedVersion, result.State);
        Assert.Equal(StorageError.UnsupportedVersion, result.Error);
        Assert.Null(result.Workspace);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsupportedDomainSchemaIsDistinctFromDiskV1(int version) =>
        Assert.Equal(StorageError.UnsupportedVersion, WorkspaceCodec.Decode(Samples.Mutate(r => r["payload"]!["schemaVersion"] = version)).Error);

    [Fact]
    public void TamperingWithoutMatchingChecksumIsWholeDocumentFailure() =>
        Assert.Equal(StorageError.IntegrityFailure, WorkspaceCodec.Decode(Samples.Mutate(r => r["payload"]!["revision"] = 999, rehash: false)).Error);

    [Theory]
    [InlineData("kind")]
    [InlineData("path")]
    [InlineData("availability")]
    [InlineData("metadata")]
    public void IndividualInvalidItemIsQuarantinedWithRawEvidenceAndReferences(string field)
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r => r["payload"]!["items"]![0]![field] = "invalid"));
        Assert.Equal(RecoveryState.PartiallyQuarantined, result.State);
        Assert.Equal(3, result.Workspace!.ItemStore.Items.Count);
        Assert.Equal(Samples.FileId, result.Workspace.Spaces[Samples.SpaceB].ItemIds[0]);
        Assert.Contains(result.Quarantine, q => q.RawJson.Contains("invalid", StringComparison.Ordinal));
        Assert.True(result.IsReadOnly);
        Assert.All(result.Quarantine, q => Assert.Equal(nameof(QuarantinedObject), q.ToString()));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("workspace")]
    [InlineData("item")]
    [InlineData("space")]
    [InlineData("metadata")]
    [InlineData("identity")]
    public void UnknownFieldsAreRetainedInSourceAndForceReadOnly(string level)
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r =>
        {
            JsonNode node = level switch
            {
                "root" => r,
                "workspace" => r["payload"]!,
                "item" => r["payload"]!["items"]![0]!,
                "space" => r["payload"]!["spaces"]![0]!,
                "metadata" => r["payload"]!["items"]![0]!["metadata"]!,
                _ => r["payload"]!["items"]![1]!["identity"]!,
            };
            node["futureField"] = "private payload";
        }));
        Assert.True(result.IsReadOnly);
        Assert.NotNull(result.Workspace);
        Assert.Contains(result.Quarantine, q => q.Reason == QuarantineReason.UnknownField);
    }

    [Theory]
    [InlineData("items")]
    [InlineData("spaces")]
    [InlineData("removedItems")]
    [InlineData("removedSpaces")]
    public void DuplicateRootIdentifiersRejectWithoutChoosingOneCopy(string collection)
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r =>
        {
            var array = r["payload"]![collection]!.AsArray();
            array.Add(array[0]!.DeepClone());
        }));
        Assert.Equal(StorageError.InvalidStructure, result.Error);
        Assert.Null(result.Workspace);
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("provenance")]
    [InlineData("destination")]
    public void KnownKindConflictingFieldsAreQuarantined(string field)
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r => r["payload"]!["items"]![0]![field] = "conflicting type"));
        Assert.Equal(RecoveryState.PartiallyQuarantined, result.State);
        Assert.DoesNotContain(Samples.FileId, result.Workspace!.ItemStore.Items.Keys);
        Assert.Contains(result.Quarantine, q => q.Reason == QuarantineReason.InvalidObject);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("activationPolicy")]
    [InlineData("capacityOverride")]
    public void SingleBadSpaceKeepsOtherSpacesAndRemovedRecords(string field)
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r => r["payload"]!["spaces"]![0]![field] = "invalid"));
        Assert.Equal(RecoveryState.PartiallyQuarantined, result.State);
        Assert.Single(result.Workspace!.Spaces);
        Assert.Single(result.Workspace.RemovedSpaces);
        Assert.Equal(4, result.Workspace.ItemStore.Items.Count);
        Assert.Contains(result.Quarantine, q => q.Collection == "spaces" && q.RawJson.Contains("invalid", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("items", "removedItems", 10001)]
    [InlineData("spaces", "removedSpaces", 1025)]
    public void TotalActivePlusRemovedObjectCountIsBounded(string active, string removed, int count)
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r =>
        {
            var array = r["payload"]![removed]!.AsArray();
            array.Clear();
            int existing = r["payload"]![active]!.AsArray().Count;
            for (int i = 0; i < count - existing; i++) { array.Add(new JsonObject()); }
        }));
        Assert.Equal(StorageError.TooLarge, result.Error);
        Assert.Null(result.Workspace);
    }

    [Fact]
    public void DuplicateResourceIdentityIsIsolatedInsteadOfCloned()
    {
        var result = WorkspaceCodec.Decode(Samples.Mutate(r =>
        {
            var items = r["payload"]!["items"]!.AsArray();
            var copy = items[0]!.DeepClone();
            copy["id"] = Guid.NewGuid(); items.Add(copy);
        }));
        Assert.Equal(4, result.Workspace!.ItemStore.Items.Count);
        Assert.Contains(result.Quarantine, q => q.Reason == QuarantineReason.DuplicateIdentity);
    }

    [Theory]
    [InlineData("$type")]
    [InlineData("commandLine")]
    [InlineData("launchPermission")]
    [InlineData("armed")]
    [InlineData("keyboardMode")]
    [InlineData("storageRoot")]
    public void MaliciousAndRuntimeFieldsCannotGrantCapabilities(string field) =>
        Assert.Equal(StorageError.InvalidStructure, WorkspaceCodec.Decode(Samples.Mutate(r => r["payload"]![field] = "private malicious payload")).Error);

    [Fact]
    public void DuplicateJsonPropertyIsRejectedEvenIfParserWouldChooseLastValue()
    {
        string json = Encoding.UTF8.GetString(WorkspaceCodec.Encode(Samples.Full())).Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1", StringComparison.Ordinal);
        Assert.Equal(StorageError.InvalidStructure, WorkspaceCodec.Decode(Encoding.UTF8.GetBytes(json)).Error);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("string")]
    [InlineData("array")]
    [InlineData("properties")]
    [InlineData("object")]
    [InlineData("depth")]
    public void ParsingBudgetsAreEnforced(string budget)
    {
        byte[] bytes = budget switch
        {
            "file" => new byte[PersistenceLimits.MaxFileBytes + 1],
            "string" => Samples.Mutate(r => r["payload"]!["items"]![0]!["metadata"]!["displayName"] = new string('x', PersistenceLimits.MaxStringLength + 1)),
            "array" => Encoding.UTF8.GetBytes("[" + string.Join(',', Enumerable.Repeat("0", PersistenceLimits.MaxArrayElements + 1)) + "]"),
            "properties" => Encoding.UTF8.GetBytes("{" + string.Join(',', Enumerable.Range(0, 65).Select(i => "\"p" + i + "\":0")) + "}"),
            "object" => Samples.Mutate(r => { for (int i = 0; i < 8; i++) { r["payload"]!["items"]![0]!["extra" + i] = new string('x', 20000); } }),
            _ => Encoding.UTF8.GetBytes(new string('[', 40) + "0" + new string(']', 40)),
        };
        var result = WorkspaceCodec.Decode(bytes);
        Assert.Null(result.Workspace);
        Assert.Contains(result.Error, new[] { StorageError.TooLarge, StorageError.InvalidJson });
    }

    [Fact]
    public void V1FixtureIsRealAndCanonicalNoHistoricalFormatIsInvented()
    {
        byte[] fixture = System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "workspace-v1.json"));
        var result = WorkspaceCodec.Decode(fixture);
        Assert.Equal(RecoveryState.Normal, result.State);
        Assert.Equal(42, result.Workspace!.Revision);
        Assert.Equal(WorkspaceCodec.Encode(Samples.Full()), WorkspaceCodec.Encode(result.Workspace));
    }

    [Fact]
    public void DefaultMigrationV1IsIdentityAndMissingOldVersionIsRejected()
    {
        using var data = JsonDocument.Parse("{\"value\":1}");
        var registry = new MigrationRegistry();
        Assert.Equal(data.RootElement.GetRawText(), registry.Migrate(data.RootElement, 1, 1).GetRawText());
        Assert.Throws<NotSupportedException>(() => registry.Migrate(data.RootElement, 0, 1));
        Assert.Throws<NotSupportedException>(() => registry.Migrate(data.RootElement, 1, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntheticFutureMigrationHarnessRejectsNonIdempotentOrThrowingStep(bool throwing)
    {
        using var input = JsonDocument.Parse("{\"value\":1}");
        var registry = new MigrationRegistry([new BadMigration(throwing)]);
        Assert.Throws<InvalidOperationException>(() => registry.Migrate(input.RootElement, 1, 2));
        Assert.Equal("{\"value\":1}", input.RootElement.GetRawText());
    }
    [Fact]
    public void SyntheticSequentialMigrationChecksRegistrationBoundariesAndIdempotence()
    {
        using var input = JsonDocument.Parse("{}");
        var registry = new MigrationRegistry([new StableMigration(1), new StableMigration(2)]);
        var first = registry.TryMigrate(input.RootElement, 1, 3);
        Assert.Equal(StorageError.None, first.Error);
        Assert.True(first.Payload!.Value.GetProperty("step1").GetBoolean());
        Assert.True(first.Payload.Value.GetProperty("step2").GetBoolean());
        Assert.Equal(first.Payload.Value.GetRawText(), registry.Migrate(first.Payload.Value, 1, 3).GetRawText());
        Assert.Equal("{}", input.RootElement.GetRawText());
        Assert.Throws<ArgumentException>(() => new MigrationRegistry([new StableMigration(1), new StableMigration(1)]));
        Assert.Equal(StorageError.MigrationFailed, new MigrationRegistry([new BadMigration(true)]).TryMigrate(input.RootElement, 1, 2).Error);
    }
    private sealed class StableMigration(int from) : IWorkspaceMigration
    {
        public int FromVersion => from;
        public JsonElement Transform(JsonElement payload)
        {
            var node = JsonNode.Parse(payload.GetRawText())!;
            node["step" + from] = true;
            return JsonSerializer.SerializeToElement(node);
        }
    }

    private sealed class BadMigration(bool throwing) : IWorkspaceMigration
    {
        public int FromVersion => 1;
        public JsonElement Transform(JsonElement payload)
        {
            if (throwing) { throw new InvalidOperationException("synthetic failure"); }
            return JsonSerializer.SerializeToElement(new { value = payload.GetProperty("value").GetInt32() + 1 });
        }
    }
}
