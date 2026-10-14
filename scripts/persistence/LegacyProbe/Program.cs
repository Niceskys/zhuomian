using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence;

if (args.Length is not (2 or 3 or 4)) { throw new ArgumentException("Expected owned fixture and source SHA."); }
byte[] fixture = File.ReadAllBytes(args[0]);
var first = WorkspaceCodec.Decode(fixture).Workspace ?? throw new InvalidOperationException("Full v1 fixture required.");
if (args.Length == 3 && args[2] == "namespace-fence")
{
    NamespaceFenceProbe.Run(first, args[1]);
    return;
}
if (args.Length == 4 && args[2] == "matrix")
{
    PerformanceMatrix.Run(first, fixture, args[1], int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture));
    return;
}
if (args.Length == 3 && args[2] == "capacity")
{
    string root = Path.Combine(Path.GetTempPath(), "zhuomian-persistence-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var create = typeof(WorkspaceFileStore).GetMethod("ForTests", BindingFlags.NonPublic | BindingFlags.Static)!;
        var store = (WorkspaceFileStore)create.Invoke(null, [root, null, null])!;
        var initial = Workspace.Create(new("00000099-0000-0000-0000-000000000000"));
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        var milestones = new List<object>(); var timer = Stopwatch.StartNew();
        int committed = 0, refused = 0;
        for (int attempt = 1; attempt <= 1000; attempt++)
        {
            var snapshot = Workspace.FromSnapshot(initial.Id, 1, attempt - 1, initial.ItemStore, []);
            var saveTimer = Stopwatch.StartNew(); var saved = store.Save(snapshot); double saveMs = saveTimer.Elapsed.TotalMilliseconds;
            if (saved.Succeeded) { committed++; } else { refused++; }
            if (attempt <= 256 ? !saved.Succeeded : saved.Error != StorageError.TooLarge)
            { throw new InvalidOperationException("Capacity gate must report real quota refusal."); }
            if (attempt is 256 or 257 or 512 or 1000)
            {
                var loadTimer = Stopwatch.StartNew(); var loaded = store.Load(); double loadMs = loadTimer.Elapsed.TotalMilliseconds;
                if (loaded.Workspace?.Revision != 255) { throw new InvalidOperationException("Last good data changed."); }
                var files = Directory.GetFiles(root); long bytes = files.Sum(p => new FileInfo(p).Length);
                milestones.Add(new
                {
                    attempt,
                    committed,
                    refused,
                    elapsedMs = timer.Elapsed.TotalMilliseconds,
                    saveMs,
                    loadMs,
                    files = files.Length,
                    bytes
                });
            }
        }
        var restoreTimer = Stopwatch.StartNew(); var restored = store.Restore(); double restoreMs = restoreTimer.Elapsed.TotalMilliseconds;
        if (restored.Error != StorageError.TooLarge) { throw new InvalidOperationException("Quota recovery unexpectedly succeeded."); }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            sourceSha = args[1],
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            totalDiskBytes = drive.TotalSize,
            availableDiskBytes = drive.AvailableFreeSpace,
            filesystem = drive.DriveFormat,
            attempts = 1000,
            committed,
            refused,
            milestones,
            restoreMs,
            restoreError = restored.Error.ToString(),
            quotaRelief = false,
            desktopAcceptance = false,
            powerCutExperiment = false
        }));
    }
    finally { Directory.Delete(root, recursive: true); }
    return;
}
var outcomes = new List<object>();
foreach (string mainState in new[] { "complete", "corrupt", "missing", "v1" })
{
    foreach (string fenceState in new[] { "absent", "future", "corrupt" })
    {
        string root = Path.Combine(Path.GetTempPath(), "zhuomian-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Run the actual immutable pre-E3-4 implementation, not a reimplementation.
            var create = typeof(WorkspaceFileStore).GetMethod("ForTests", BindingFlags.NonPublic | BindingFlags.Static)!;
            var store = (WorkspaceFileStore)create.Invoke(null, [root, null, null])!;
            if (!store.Save(first).Succeeded) { throw new InvalidOperationException("Baseline initialization failed."); }
            // Counterexample to a proposed main-only format bump plus cold-directory layering.
            // This is synthetic future data, not a claimed released v2 or a migration.
            var hidden = Workspace.FromSnapshot(first.Id, 1, 1000, first.ItemStore, first.Spaces.Values, first.RemovedSpaces.Values);
            byte[] hiddenBytes = WorkspaceCodec.Encode(hidden);
            string cold = Path.Combine(root, "cold"); Directory.CreateDirectory(cold);
            string hiddenPath = Path.Combine(cold, "future-history.json"); File.WriteAllBytes(hiddenPath, hiddenBytes);
            var future = JsonNode.Parse(hiddenBytes)!.AsObject(); future["formatVersion"] = 2;
            byte[] futureBytes = Encoding.UTF8.GetBytes(future.ToJsonString());
            if (mainState == "complete") { File.WriteAllBytes(Path.Combine(root, "workspace.json"), futureBytes); }
            else if (mainState == "corrupt") { File.WriteAllText(Path.Combine(root, "workspace.json"), "interrupted future main"); }
            else if (mainState == "missing") { File.Delete(Path.Combine(root, "workspace.json")); }
            if (fenceState != "absent")
            {
                File.WriteAllBytes(Path.Combine(root, "prepared-" + Guid.NewGuid().ToString("N") + ".json"),
                    fenceState == "future" ? futureBytes : [0, 1, 2]);
            }
            string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var before = Directory.GetFiles(root, "backup-*.json").Concat(Directory.GetFiles(root, "prepared-*.json"))
                .ToDictionary(p => p, Digest);
            var loaded = store.Load();
            var backed = store.Backup();
            // Save and Restore each start from the same main bytes. Keep all other effects as evidence.
            string mainPath = Path.Combine(root, "workspace.json");
            byte[]? originalMain = File.Exists(mainPath) ? File.ReadAllBytes(mainPath) : null;
            var oldNext = Workspace.FromSnapshot(first.Id, 1, first.Revision + 1, first.ItemStore, first.Spaces.Values, first.RemovedSpaces.Values);
            var saved = store.Save(oldNext);
            if (originalMain is null) { File.Delete(mainPath); } else { File.WriteAllBytes(mainPath, originalMain); }
            var restored = store.Restore();
            bool rollback = saved.Succeeded && saved.Revision < 1000 || restored.Error == StorageError.None && restored.Workspace?.Revision < 1000;
            bool expectedRollback = mainState != "complete" && fenceState == "absent";
            if (rollback != expectedRollback || !hiddenBytes.AsSpan().SequenceEqual(File.ReadAllBytes(hiddenPath)) ||
                before.Any(p => Digest(p.Key) != p.Value)) { throw new InvalidOperationException("Compatibility counterexample or retention assertion changed."); }
            outcomes.Add(new
            {
                mainState,
                fenceState,
                loadState = loaded.State.ToString(),
                loadError = loaded.Error.ToString(),
                loadedRevision = loaded.Workspace?.Revision,
                backupError = backed.Error.ToString(),
                backupCommit = backed.Commit.ToString(),
                saveError = saved.Error.ToString(),
                saveCommit = saved.Commit.ToString(),
                savedRevision = saved.Revision,
                restoreError = restored.Error.ToString(),
                restoredRevision = restored.Workspace?.Revision,
                hiddenHistoryHighWater = 1000,
                rollback,
                sourceEvidenceUnchanged = true
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    sourceSha = args[1],
    platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    runtime = Environment.Version.ToString(),
    cases = outcomes,
    productionMigrationEnabled = false,
    candidate = "synthetic main-only format bump with unrecognized cold history; not shipped v2"
}));
