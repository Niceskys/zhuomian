using Zhuomian.Core.Interaction;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.WorkspaceModel;

public sealed class WorkspaceTests
{
    private static readonly int[] DiagnosticIndices = [1, 2, 3, 4];
    private static readonly Guid WorkspaceId = Id(1);
    private static readonly Guid A = Id(2);
    private static readonly Guid B = Id(3);
    private static Guid Id(int value) => new(value, 0, 0, new byte[8]);
    private static FileItem File(int number = 10, ResourceReference? icon = null) => new(Id(number),
        new ItemMetadata("File " + number, icon), new WindowsPath(@"C:\Fixtures\file" + number + ".txt"), "volume:file-" + number);
    private static Workspace Empty() => Workspace.Create(WorkspaceId)
        .CreateSpace(A, "A", new(ActivationMode.Hover)).CreateSpace(B, "B", new(ActivationMode.Click));
    private static Workspace WithItems() => Empty().AddItemToSpace(A, File()).AddItemToSpace(A, File(11))
        .AddItemToSpace(A, File(12)).AddReference(B, Id(10));
    private static void Error(DomainError error, Action action) =>
        Assert.Equal(error, Assert.Throws<WorkspaceOperationException>(action).Error);

    [Fact]
    public void CreateRenameDeletePreserveStableIdsAndOldSnapshots()
    {
        var original = Empty();
        var renamed = original.RenameSpace(A, "  新名称  ");
        var deleted = renamed.DeleteSpace(A);
        Assert.Equal(WorkspaceId, deleted.Id);
        Assert.Equal(Workspace.CurrentSchemaVersion, deleted.SchemaVersion);
        Assert.Equal(4, deleted.Revision);
        Assert.Equal("A", original.Spaces[A].Name);
        Assert.Equal("新名称", renamed.Spaces[A].Name);
        Assert.False(deleted.Spaces.ContainsKey(A));
        Assert.Equal(A, deleted.RemovedSpaces[A].Id);
        Error(DomainError.IdentityAlreadyUsed, () => original.CreateSpace(A, "Other", new(ActivationMode.Click)));
        Error(DomainError.IdentityAlreadyUsed, () => deleted.CreateSpace(A, "Other", new(ActivationMode.Click)));
        Error(DomainError.SpaceNotFound, () => deleted.RenameSpace(A, "Other"));
    }

    [Fact]
    public void SharingUnbindingAndDeletingSpaceNeverCollectItems()
    {
        var initial = WithItems();
        var unbound = initial.RemoveReference(A, Id(10));
        Assert.Equal(2, initial.GetReferenceCount(Id(10)));
        Assert.Equal(1, unbound.GetReferenceCount(Id(10)));
        Assert.Equal(new[] { Id(11), Id(12) }, unbound.Spaces[A].ItemIds);
        Assert.Same(initial.Spaces[B], unbound.Spaces[B]);
        var deleted = unbound.DeleteSpace(B);
        Assert.Equal(0, deleted.GetReferenceCount(Id(10)));
        Assert.Same(initial.ItemStore.Items[Id(10)], deleted.ItemStore.Items[Id(10)]);
        Assert.Equal(new[] { Id(10) }, deleted.RemovedSpaces[B].ItemIds);
        Assert.Same(initial.ItemStore, deleted.ItemStore);
    }

    [Fact]
    public void DuplicateReferencesRejectWithoutChangingOrderOrRevision()
    {
        var workspace = WithItems();
        Error(DomainError.ReferenceAlreadyExists, () => workspace.AddReference(A, Id(10), 1));
        Assert.Equal(new[] { Id(10), Id(11), Id(12) }, workspace.Spaces[A].ItemIds);
        Assert.Equal(6, workspace.Revision);
        Assert.Equal(2, workspace.GetReferenceCount(Id(10)));
    }

    [Fact]
    public void InsertionMoveAndReorderPreserveExplicitUserOrder()
    {
        var initial = WithItems();
        var inserted = initial.AddItemToSpace(A, File(13), 1);
        Assert.Equal(new[] { Id(10), Id(13), Id(11), Id(12) }, inserted.Spaces[A].ItemIds);
        var moved = inserted.MoveReference(A, Id(10), 3);
        Assert.Equal(new[] { Id(13), Id(11), Id(12), Id(10) }, moved.Spaces[A].ItemIds);
        var ordered = moved.ReorderReferences(A, new[] { Id(12), Id(10), Id(13), Id(11) });
        Assert.Equal(new[] { Id(12), Id(10), Id(13), Id(11) }, ordered.Spaces[A].ItemIds);
        Assert.Same(initial.Spaces[B], ordered.Spaces[B]);
        Assert.Same(inserted.ItemStore, ordered.ItemStore);
    }

    [Fact]
    public void CrossSpaceMoveIsAtomicAndPreservesBothRelativeOrders()
    {
        var initial = WithItems().AddReference(B, Id(12));
        var moved = initial.MoveReference(A, B, Id(11), 1);
        Assert.Equal(new[] { Id(10), Id(12) }, moved.Spaces[A].ItemIds);
        Assert.Equal(new[] { Id(10), Id(11), Id(12) }, moved.Spaces[B].ItemIds);
        Assert.Equal(initial.Revision + 1, moved.Revision);
        Error(DomainError.ReferenceAlreadyExists, () => initial.MoveReference(A, B, Id(10)));
        Assert.Equal(new[] { Id(10), Id(11), Id(12) }, initial.Spaces[A].ItemIds);
        Assert.Equal(new[] { Id(10), Id(12) }, initial.Spaces[B].ItemIds);
    }

    [Fact]
    public void SameSpaceTransferUsesFinalIndexAndNoopDoesNotAdvanceRevision()
    {
        var workspace = WithItems();
        Assert.Same(workspace, workspace.MoveReference(A, Id(10), 0));
        Assert.Same(workspace, workspace.RenameSpace(A, " A "));
        Assert.Same(workspace, workspace.ReorderReferences(A, workspace.Spaces[A].ItemIds));
        Assert.Same(workspace, workspace.ConfigureSpace(A, new(ActivationMode.Hover), new()));
        Assert.Same(workspace, workspace.SetItemAvailability(Id(10), ItemAvailability.Unverified));
        Assert.Equal(new[] { Id(11), Id(12), Id(10) }, workspace.MoveReference(A, A, Id(10)).Spaces[A].ItemIds);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void InvalidInsertionCannotLeaveHalfAddedCatalogItem(int index)
    {
        var initial = WithItems();
        Assert.Throws<ArgumentOutOfRangeException>(() => initial.AddItemToSpace(A, File(13), index));
        Assert.False(initial.ItemStore.Items.ContainsKey(Id(13)));
        Assert.Equal(3, initial.ItemStore.Items.Count);
        Assert.Equal(6, initial.Revision);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void InvalidMoveCannotUnbindEitherSpace(int index)
    {
        var workspace = WithItems();
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.MoveReference(A, Id(10), index));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.MoveReference(A, B, Id(11), index));
        Assert.Equal(3, workspace.Spaces[A].ItemIds.Length);
        Assert.Single(workspace.Spaces[B].ItemIds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void InvalidPermutationIsRejectedAtomically(int variant)
    {
        var workspace = WithItems();
        Guid[] ids = variant switch
        {
            0 => [Id(10), Id(11)],
            1 => [Id(10), Id(11), Id(11)],
            _ => [Id(10), Id(11), Id(99)],
        };
        Error(DomainError.InvalidOrder, () => workspace.ReorderReferences(A, ids));
        Assert.Equal(new[] { Id(10), Id(11), Id(12) }, workspace.Spaces[A].ItemIds);
    }


    [Fact]
    public void ConfirmedGlobalRemovalUnbindsEverySharingSpaceWithoutReorderingOthers()
    {
        var initial = WithItems().AddReference(B, Id(12));
        var removed = initial.RemoveItemGlobally(Id(10), GlobalRemovalConfirmation.Confirmed);
        Assert.Equal(new[] { Id(11), Id(12) }, removed.Spaces[A].ItemIds);
        Assert.Equal(new[] { Id(12) }, removed.Spaces[B].ItemIds);
        Assert.Same(initial.Spaces[A].ActivationPolicy, removed.Spaces[A].ActivationPolicy);
        Assert.Same(initial.Spaces[B].PreviewPolicy, removed.Spaces[B].PreviewPolicy);
        Assert.Same(initial.ItemStore.Items[Id(10)], removed.ItemStore.RemovedItems[Id(10)]);
        Assert.Empty(removed.DiagnoseReferences(A));
        Assert.Empty(removed.DiagnoseReferences(B));
    }

    [Fact]
    public void AddingMissingItemRepairsQuarantineWithoutChangingUserOrder()
    {
        var initial = Workspace.FromSnapshot(WorkspaceId, 1, 7, ItemStore.FromSnapshot(new[] { File(11) }, []),
            new[] { new Space(A, "A", new(ActivationMode.Click), itemIds: new[] { Id(10), Id(11) }) });
        Assert.Single(initial.DiagnoseReferences(A));
        var repaired = initial.AddItem(File());
        Assert.Empty(repaired.DiagnoseReferences(A));
        Assert.Equal(new[] { Id(10), Id(11) }, repaired.GetUsableItemIds(A));
        Assert.Same(initial.Spaces[A], repaired.Spaces[A]);
        Assert.Equal(8, repaired.Revision);
    }

    [Fact]
    public void ExplicitGlobalRemovalRetainsRecoveryRecordAndUnbindsAllLiveSpaces()
    {
        var initial = WithItems().DeleteSpace(B);
        var removed = initial.RemoveItemGlobally(Id(10), GlobalRemovalConfirmation.Confirmed);
        Assert.False(removed.ItemStore.Items.ContainsKey(Id(10)));
        Assert.Same(initial.ItemStore.Items[Id(10)], removed.ItemStore.RemovedItems[Id(10)]);
        Assert.DoesNotContain(Id(10), removed.Spaces[A].ItemIds);
        Assert.Equal(new[] { Id(10) }, removed.RemovedSpaces[B].ItemIds);
        Assert.Contains(Id(10), initial.ItemStore.Items.Keys);
        Error(DomainError.ItemNotFound, () => removed.AddReference(A, Id(10)));
        Error(DomainError.IdentityAlreadyUsed, () => removed.AddItem(File()));
        Error(DomainError.ResourceAlreadyKnown, () => removed.AddItem(new FileItem(Id(99), new("Same"), File().Path)));
        var restored = removed.RestoreItem(Id(10));
        Assert.Same(initial.ItemStore.Items[Id(10)], restored.ItemStore.Items[Id(10)]);
        Assert.Empty(restored.ItemStore.RemovedItems);
        Assert.DoesNotContain(Id(10), restored.Spaces[A].ItemIds);
        Assert.Contains(Id(10), restored.AddReference(A, Id(10)).Spaces[A].ItemIds);
    }

    [Theory]
    [InlineData(GlobalRemovalConfirmation.Unconfirmed)]
    [InlineData((GlobalRemovalConfirmation)99)]
    public void GlobalRemovalRequiresIndependentConfirmation(GlobalRemovalConfirmation confirmation)
    {
        var workspace = WithItems();
        Error(DomainError.ConfirmationRequired, () => workspace.RemoveItemGlobally(Id(10), confirmation));
        Assert.Equal(2, workspace.GetReferenceCount(Id(10)));
        Assert.Empty(workspace.ItemStore.RemovedItems);
    }

    [Fact]
    public void DanglingEmptyDuplicateAndRemovedReferencesArePreservedAndDiagnosed()
    {
        var store = ItemStore.FromSnapshot(new[] { File() }, new[] { File(11) });
        Guid[] raw = [Id(10), Id(99), Guid.Empty, Id(10), Id(11)];
        var workspace = Workspace.FromSnapshot(WorkspaceId, 1, 42, store,
            new[] { new Space(A, "Snapshot", new(ActivationMode.Click), itemIds: raw) });
        raw[0] = Id(98);
        Assert.Equal(new[] { Id(10), Id(99), Guid.Empty, Id(10), Id(11) }, workspace.Spaces[A].ItemIds);
        Assert.Equal(new[] { Id(10) }, workspace.GetUsableItemIds(A));
        Assert.Equal(new[] { ReferenceProblem.MissingItem, ReferenceProblem.EmptyIdentity,
            ReferenceProblem.DuplicateReference, ReferenceProblem.GloballyRemovedItem }, workspace.DiagnoseReferences(A).Select(d => d.Problem));
        Assert.Equal(DiagnosticIndices, workspace.DiagnoseReferences(A).Select(d => d.Index));
        Assert.All(workspace.DiagnoseReferences(A), d => Assert.Equal(A, d.SpaceId));
        Assert.Equal(42, workspace.Revision);
        var repaired = workspace.RemoveReference(A, Guid.Empty).RemoveReference(A, Id(99)).RemoveReference(A, Id(10));
        Assert.Equal(new[] { Id(11) }, repaired.Spaces[A].ItemIds);
        Assert.Same(store, repaired.ItemStore);
        Assert.Single(repaired.DiagnoseReferences(A));
        Assert.Equal(new[] { Id(10), Id(11) }, workspace.RestoreItem(Id(11)).GetUsableItemIds(A));
    }

    [Fact]
    public void MissingResourcesAreRecoverableAndNotAutomaticallyDeleted()
    {
        var initial = WithItems();
        var missing = initial.SetItemAvailability(Id(10), ItemAvailability.Missing);
        Assert.Equal(ItemAvailability.Unverified, initial.ItemStore.Items[Id(10)].Availability);
        Assert.Equal(ItemAvailability.Missing, missing.ItemStore.Items[Id(10)].Availability);
        Assert.Equal(initial.GetUsableItemIds(A).ToArray(), missing.GetUsableItemIds(A).ToArray());
        Assert.Same(initial.Spaces[A], missing.Spaces[A]);
        Assert.Equal(ItemAvailability.Available, missing.SetItemAvailability(Id(10), ItemAvailability.Available).ItemStore.Items[Id(10)].Availability);
    }

    [Fact]
    public void IndependentPoliciesRetainZeroCapacityAndCanResetToAutomatic()
    {
        var initial = WithItems();
        var configured = initial.ConfigureSpace(A, new(ActivationMode.Hover, 120), new(0));
        Assert.Equal(0, configured.Spaces[A].PreviewPolicy.CapacityOverride);
        Assert.Equal(120, configured.Spaces[A].ActivationPolicy.HoverDelayMilliseconds);
        Assert.Equal(20, initial.Spaces[A].ActivationPolicy.HoverDelayMilliseconds);
        Assert.Same(initial.Spaces[B], configured.Spaces[B]);
        Assert.Equal(initial.Spaces[A].ItemIds, configured.Spaces[A].ItemIds);
        Assert.True(configured.ConfigureSpace(A, new(ActivationMode.Click), PreviewCapacityPolicy.Automatic).Spaces[A].PreviewPolicy.IsAutomatic);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PreviewCapacityPolicy(-1));
        Assert.Equal(int.MaxValue, new PreviewCapacityPolicy(int.MaxValue).CapacityOverride);
    }

    [Fact]
    public void DeletionRetainsExternalSourcesAndManagedAssetsEvenAfterGlobalRemoval()
    {
        var external = new ExternalResourceReference(new WindowsPath(@"C:\Fixtures\external.png"));
        var managed = new ManagedAssetReference(Id(50));
        var initial = Empty().AddItemToSpace(A, File(10, external)).AddItemToSpace(A, File(11, managed));
        var deleted = initial.DeleteSpace(A).RemoveItemGlobally(Id(11), GlobalRemovalConfirmation.Confirmed);
        Assert.Equal(external, deleted.ItemStore.Items[Id(10)].Metadata.Icon);
        Assert.Equal(managed, deleted.ItemStore.RemovedItems[Id(11)].Metadata.Icon);
        Assert.Contains(Id(50), deleted.GetRetainedManagedAssetIds());
        Assert.Equal(0, deleted.GetReferenceCount(Id(10)));
        Assert.Equal(@"C:\Fixtures\file10.txt", ((FileItem)deleted.ItemStore.Items[Id(10)]).Path.Value);
        // No filesystem/launcher/asset adapter can be injected into any of these operations.
        Assert.All(typeof(Workspace).GetMethods().SelectMany(m => m.GetParameters()),
            parameter => Assert.False(typeof(IInteractionLauncher).IsAssignableFrom(parameter.ParameterType)));
    }

    [Fact]
    public void DuplicateIdsAndNormalizedResourceIdentityAreRejected()
    {
        var workspace = WithItems();
        Error(DomainError.IdentityAlreadyUsed, () => workspace.AddItem(new UrlItem(Id(10), new("URL"), new("https://example.test"))));
        Error(DomainError.ResourceAlreadyKnown, () => workspace.AddItem(new FileItem(Id(99), new("Same"), new(@"c:/fixtures/sub/../FILE10.txt"))));
        Assert.Equal(3, workspace.ItemStore.Items.Count);
        Error(DomainError.IdentityAlreadyUsed, () => ItemStore.FromSnapshot(new[] { File(), File() }, []));
        Error(DomainError.IdentityAlreadyUsed, () => Workspace.FromSnapshot(WorkspaceId, 1, 0, ItemStore.Empty,
            new[] { workspace.Spaces[A], workspace.Spaces[A] }));
        Error(DomainError.IdentityAlreadyUsed, () => Workspace.FromSnapshot(WorkspaceId, 1, 0, ItemStore.Empty,
            new[] { workspace.Spaces[A] }, new[] { workspace.Spaces[A] }));
    }

    [Fact]
    public void MissingTargetsRejectAndFailedAtomicAddDoesNotChangeCatalog()
    {
        var workspace = WithItems();
        Error(DomainError.SpaceNotFound, () => workspace.AddItemToSpace(Id(99), File(13)));
        Error(DomainError.SpaceNotFound, () => workspace.DeleteSpace(Id(99)));
        Error(DomainError.ItemNotFound, () => workspace.AddReference(A, Id(99)));
        Error(DomainError.ReferenceNotFound, () => workspace.RemoveReference(B, Id(11)));
        Error(DomainError.ReferenceNotFound, () => workspace.MoveReference(B, Id(11), 0));
        Error(DomainError.ReferenceNotFound, () => workspace.MoveReference(B, A, Id(11)));
        Error(DomainError.ItemNotFound, () => workspace.RestoreItem(Id(99)));
        Error(DomainError.ItemNotFound, () => workspace.RemoveItemGlobally(Id(99), GlobalRemovalConfirmation.Confirmed));
        Assert.Equal(6, workspace.Revision);
        Assert.Equal(3, workspace.ItemStore.Items.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nname")]
    public void InvalidNamesAndRenameFailWithoutMutation(string name)
    {
        var workspace = Empty();
        Assert.Throws<ArgumentException>(() => workspace.CreateSpace(Id(99), name, new(ActivationMode.Click)));
        Assert.Throws<ArgumentException>(() => workspace.RenameSpace(A, name));
        Assert.Equal("A", workspace.Spaces[A].Name);
        Assert.Equal(2, workspace.Revision);
    }

    [Fact]
    public void InvalidIdsNullsAndSchemaDoNotCreatePartialState()
    {
        var workspace = WithItems();
        Assert.Throws<ArgumentException>(() => Workspace.Create(Guid.Empty));
        Assert.Throws<ArgumentException>(() => workspace.CreateSpace(Guid.Empty, "Space", new(ActivationMode.Click)));
        Assert.Throws<ArgumentException>(() => new FileItem(Guid.Empty, new("File"), new(@"C:\file.txt")));
        Assert.Throws<ArgumentNullException>(() => workspace.CreateSpace(Id(99), "Space", null!));
        Assert.Throws<ArgumentNullException>(() => workspace.ConfigureSpace(A, new(ActivationMode.Click), null!));
        Assert.Throws<ArgumentNullException>(() => workspace.AddItem(null!));
        Assert.Throws<ArgumentNullException>(() => workspace.ReorderReferences(A, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.SetItemAvailability(Id(10), (ItemAvailability)99));
        Assert.Throws<NotSupportedException>(() => Workspace.FromSnapshot(WorkspaceId, 2, 0, ItemStore.Empty, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => Workspace.FromSnapshot(WorkspaceId, 1, -1, ItemStore.Empty, []));
    }


    [Fact]
    public void CorruptDuplicateSourceCannotBeSilentlyCollapsedByMove()
    {
        var workspace = Workspace.FromSnapshot(WorkspaceId, 1, 0, ItemStore.FromSnapshot(new[] { File() }, []),
            new[] { new Space(A, "A", new(ActivationMode.Click), itemIds: new[] { Id(10), Id(10) }),
                new Space(B, "B", new(ActivationMode.Click)) });
        Error(DomainError.InvalidOrder, () => workspace.MoveReference(A, Id(10), 1));
        Error(DomainError.InvalidOrder, () => workspace.MoveReference(A, B, Id(10)));
        Error(DomainError.InvalidOrder, () => workspace.ReorderReferences(A, new[] { Id(10), Id(10) }));
        Assert.Equal(2, workspace.Spaces[A].ItemIds.Length);
        Assert.Empty(workspace.Spaces[B].ItemIds);
        Assert.Single(workspace.DiagnoseReferences(A));
    }

    [Fact]
    public void RevisionOverflowCannotLeaveHalfCompletedChanges()
    {
        var initial = WithItems();
        var max = Workspace.FromSnapshot(WorkspaceId, 1, long.MaxValue, initial.ItemStore, initial.Spaces.Values);
        Assert.Throws<OverflowException>(() => max.RemoveItemGlobally(Id(10), GlobalRemovalConfirmation.Confirmed));
        Assert.Throws<OverflowException>(() => max.AddItemToSpace(A, File(13)));
        Assert.Equal(2, max.GetReferenceCount(Id(10)));
        Assert.False(max.ItemStore.Items.ContainsKey(Id(13)));
        Assert.Empty(max.ItemStore.RemovedItems);
    }

    [Fact]
    public void ThrowingReorderEnumerationLeavesOriginalSnapshotUntouched()
    {
        var workspace = WithItems();
        Assert.Throws<InvalidOperationException>(() => workspace.ReorderReferences(A, ThrowAfterOne()));
        Assert.Equal(new[] { Id(10), Id(11), Id(12) }, workspace.Spaces[A].ItemIds);
        static IEnumerable<Guid> ThrowAfterOne() { yield return Id(10); throw new InvalidOperationException("fixture"); }
    }
}
