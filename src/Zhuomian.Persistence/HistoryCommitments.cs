using System.Security.Cryptography;

namespace Zhuomian.Persistence;

/// <summary>
/// Content-bound evidence for one completely scanned v1 history. This is not a persisted
/// index, coverage proof, or permission to retire a source. Unknown coverage fails closed
/// at the caller; gaps between observed revisions are allowed.
/// </summary>
internal sealed class HistoryCommitments
{
    private readonly Dictionary<long, byte[]> _contents = [];
    private Guid? _workspaceId;
    internal long MaximumRevision { get; private set; }
    internal StorageError Error { get; private set; }

    internal StorageError Observe(RecoveryResult candidate, HistoryReadSession session)
    {
        if (Error != StorageError.None) { return Error; }
        if (candidate.Workspace is null || candidate.IsReadOnly) { throw new StorageException(StorageError.ReadOnly); }
        var workspace = candidate.Workspace;
        if (_workspaceId is not null && _workspaceId != workspace.Id)
        { return Error = StorageError.WorkspaceConflict; }
        _workspaceId ??= workspace.Id;
        byte[] digest = session.ContentDigest(candidate);
        if (_contents.TryGetValue(workspace.Revision, out byte[]? previous) &&
            !CryptographicOperations.FixedTimeEquals(previous, digest))
        { return Error = StorageError.RevisionConflict; }
        _contents[workspace.Revision] = digest;
        MaximumRevision = Math.Max(MaximumRevision, workspace.Revision);
        return StorageError.None;
    }

    internal (StorageError Error, long Revision, bool Match) Check(Guid workspaceId, long? revision, byte[]? next)
    {
        if (Error != StorageError.None) { return (Error, MaximumRevision, false); }
        if (_workspaceId is not null && _workspaceId != workspaceId)
        { return (StorageError.WorkspaceConflict, MaximumRevision, false); }
        bool match = false;
        if (revision is not null && _contents.TryGetValue(revision.Value, out byte[]? previous))
        {
            if (next is null || !CryptographicOperations.FixedTimeEquals(previous, SHA256.HashData(next)))
            { return (StorageError.RevisionConflict, MaximumRevision, false); }
            match = true;
        }
        return (revision < MaximumRevision ? StorageError.StaleRevision : StorageError.None, MaximumRevision, match);
    }
}
