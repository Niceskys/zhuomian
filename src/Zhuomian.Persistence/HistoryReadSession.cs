using System.Security.Cryptography;

namespace Zhuomian.Persistence;

/// <summary>
/// Bounded reuse within one locked operation only. Every source is still independently
/// opened and fully read through PhysicalStorage. A hit requires exact byte equality,
/// not filename, revision, metadata or digest equality alone. Never retained by a store.
/// </summary>
internal sealed class HistoryReadSession
{
    private readonly Dictionary<string, (byte[] Bytes, RecoveryResult Result)> _decoded = [];
    private readonly HashSet<RecoveryResult> _retained = [];
    private readonly Dictionary<RecoveryResult, byte[]> _commitments = [];
    private int _retainedBytes;

    internal RecoveryResult Decode(byte[] bytes, out string rawDigest)
    {
        rawDigest = Convert.ToHexString(SHA256.HashData(bytes));
        if (_decoded.TryGetValue(rawDigest, out var previous) && bytes.AsSpan().SequenceEqual(previous.Bytes))
        { return previous.Result; }
        var result = WorkspaceCodec.Decode(bytes);
        // A full cache changes performance only. Neither parsing nor acceptance depends on it.
        if (!_decoded.ContainsKey(rawDigest) && _decoded.Count < 2 * PersistenceLimits.MaxRecoveryFiles + 1 &&
            bytes.Length <= PersistenceLimits.MaxFileBytes - _retainedBytes)
        {
            _decoded.Add(rawDigest, (bytes, result));
            _retained.Add(result);
            _retainedBytes += bytes.Length;
        }
        return result;
    }

    internal byte[] ContentDigest(RecoveryResult result)
    {
        if (result.Workspace is null || result.IsReadOnly) { throw new StorageException(StorageError.ReadOnly); }
        if (_commitments.TryGetValue(result, out var digest)) { return digest; }
        // Retain the complete existing Encode validation/roundtrip; no cheaper codec path.
        digest = SHA256.HashData(WorkspaceCodec.Encode(result.Workspace));
        if (_retained.Contains(result)) { _commitments.Add(result, digest); }
        return digest;
    }
}
