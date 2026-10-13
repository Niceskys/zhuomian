using System.Text.Json;

namespace Zhuomian.Persistence;

/// <summary>Trusted pure code, never registered by configuration. Exactly one format version per step.</summary>
public interface IWorkspaceMigration
{
    int FromVersion { get; }
    JsonElement Transform(JsonElement payload);
}

/// <summary>There are no historical formats before v1. The default registry intentionally has no steps.</summary>
public sealed class MigrationRegistry
{
    private readonly Dictionary<int, IWorkspaceMigration> _steps;
    public MigrationRegistry(IEnumerable<IWorkspaceMigration>? steps = null)
    {
        _steps = [];
        foreach (var step in steps ?? [])
        {
            ArgumentNullException.ThrowIfNull(step);
            if (step.FromVersion < 1 || !_steps.TryAdd(step.FromVersion, step)) { throw new ArgumentException("Invalid migration registration."); }
        }
    }

    public MigrationResult TryMigrate(JsonElement payload, int fromVersion, int toVersion)
    {
        try { return new(StorageError.None, Migrate(payload, fromVersion, toVersion)); }
        catch (NotSupportedException) { return new(StorageError.UnsupportedVersion, null); }
        catch (Exception) { return new(StorageError.MigrationFailed, null); }
    }

    public JsonElement Migrate(JsonElement payload, int fromVersion, int toVersion)
    {
        if (fromVersion < 1 || toVersion < fromVersion) { throw new NotSupportedException("Unsupported format."); }
        WorkspaceCodec.ValidateMigrationPayload(payload);
        var current = payload.Clone();
        for (int version = fromVersion; version < toVersion; version++)
        {
            if (!_steps.TryGetValue(version, out var step)) { throw new NotSupportedException("Missing migration."); }
            var next = step.Transform(current.Clone()).Clone();
            // A migration must be deterministic and stable when reapplied. Work only on detached data.
            WorkspaceCodec.ValidateMigrationPayload(next);
            var repeated = step.Transform(next.Clone());
            if (next.GetRawText() != repeated.GetRawText()) { throw new InvalidOperationException("Migration is not idempotent."); }
            current = next;
        }
        return current;
    }
}

public readonly record struct MigrationResult(StorageError Error, JsonElement? Payload)
{
    public override string ToString() => $"{nameof(MigrationResult)}({Error})";
}
