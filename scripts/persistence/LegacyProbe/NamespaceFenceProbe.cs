using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence;

internal static class NamespaceFenceProbe
{
    internal static void Run(Workspace fixture, string sourceSha)
    {
        var cases = new List<object>();
        foreach (string newRoot in new[] { "complete", "corrupt", "missing" })
        {
            string root = Path.Combine(Path.GetTempPath(), "zhuomian-persistence-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var create = typeof(WorkspaceFileStore).GetMethod("ForTests", BindingFlags.NonPublic | BindingFlags.Static)!;
                var store = (WorkspaceFileStore)create.Invoke(null, [root, null, null])!;
                if (!store.Save(fixture).Succeeded) { throw new InvalidOperationException("Owned legacy fixture failed."); }
                var originals = Directory.GetFiles(root, "backup-*.json").Concat(Directory.GetFiles(root, "prepared-*.json"))
                    .ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
                // Synthetic namespace experiment only: preserve old main exactly, then fence its legacy name.
                File.Move(Path.Combine(root, "workspace.json"), Path.Combine(root, "original-main.raw"));
                Directory.CreateDirectory(Path.Combine(root, "workspace.json"));
                string candidate = Path.Combine(root, "state-v2.json");
                if (newRoot == "complete") { File.WriteAllText(candidate, "{\"formatVersion\":2,\"syntheticCandidate\":true}"); }
                if (newRoot == "corrupt") { File.WriteAllText(candidate, "broken candidate root"); }
                var load = store.Load(); var save = store.Save(fixture); var backup = store.Backup(); var restore = store.Restore();
                foreach (var error in new[] { load.Error, save.Error, backup.Error, restore.Error })
                { if (error != StorageError.UnsafePath) { throw new InvalidOperationException("Old executable bypassed directory barrier."); } }
                if (originals.Any(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p.Key))) != p.Value))
                { throw new InvalidOperationException("Old recovery evidence changed."); }
                cases.Add(new
                {
                    newRoot,
                    load = load.Error.ToString(),
                    save = save.Error.ToString(),
                    backup = backup.Error.ToString(),
                    restore = restore.Error.ToString(),
                    evidenceUnchanged = true
                });
            }
            finally { Directory.Delete(root, recursive: true); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sourceSha,
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            candidate = "synthetic directory barrier; no migration or v2 correctness claim",
            barrierPresent = true,
            productionMigrationEnabled = false,
            cases
        }));
    }
}
