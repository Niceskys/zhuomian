using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence;

internal static class PerformanceMatrix
{
    private static readonly string[] MetricProperties = ["SuccessfulReadFiles", "SuccessfulReadBytes", "HistoryEnumerationEntries"];
    internal static void Run(Workspace fixture, byte[] fixtureBytes, string sourceSha, int repetition)
    {
        // Same complete warmup matrix in each process. Setup, JIT warmup and cleanup
        // are excluded from measured intervals; every mutation gets a fresh root.
        foreach (int totalHistory in new[] { 0, 32, 128, 256, 512 })
        {
            foreach (string operation in new[] { "Load", "Save", "Backup", "Restore" })
            { Measure(fixture, totalHistory, operation, repetition, warmup: true); }
        }
        var samples = new List<object>();
        foreach (int totalHistory in new[] { 0, 32, 128, 256, 512 })
        {
            foreach (string operation in new[] { "Load", "Save", "Backup", "Restore" })
            { samples.Add(Measure(fixture, totalHistory, operation, repetition, warmup: false)); }
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            sourceSha,
            repetition,
            fixtureSha256 = Convert.ToHexString(SHA256.HashData(fixtureBytes)),
            persistenceAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(WorkspaceFileStore).Assembly.Location))),
            platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            runtime = Environment.Version.ToString(),
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            protocol = "full v1 fixture; half backups/half prepared; one complete warmup matrix per process; fresh root per sample; setup/cleanup excluded",
            logicalReadsOnly = true,
            quotaRelief = false,
            samples
        }));
    }

    private static object Measure(Workspace fixture, int totalHistory, string operation, int repetition, bool warmup)
    {
        string root = Path.Combine(Path.GetTempPath(), "zhuomian-persistence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var create = typeof(WorkspaceFileStore).GetMethod("ForTests", BindingFlags.NonPublic | BindingFlags.Static)!;
            var store = (WorkspaceFileStore)create.Invoke(null, [root, null, null])!;
            int perFamily = totalHistory / 2;
            Workspace At(long revision) => Workspace.FromSnapshot(fixture.Id, 1, revision, fixture.ItemStore, fixture.Spaces.Values, fixture.RemovedSpaces.Values);
            using var seed = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            // Deterministic exact-byte duplicate pairs, complete distinct revision coverage.
            for (int revision = 1; revision <= perFamily; revision++)
            {
                byte[] bytes = WorkspaceCodec.Encode(At(revision));
                foreach (string family in new[] { "backup", "prepared" })
                { File.WriteAllBytes(Path.Combine(root, family + "-" + revision.ToString("x32", System.Globalization.CultureInfo.InvariantCulture) + ".json"), bytes); seed.AppendData(bytes); }
            }
            string main = Path.Combine(root, "workspace.json"); byte[] mainBytes = WorkspaceCodec.Encode(At(perFamily + 1));
            File.WriteAllBytes(main, mainBytes); seed.AppendData(mainBytes);
            string seedDigest = Convert.ToHexString(seed.GetHashAndReset());
            // Lock-file creation excluded from timing, but each measured operation still acquires it.
            using (File.Create(Path.Combine(root, "workspace.lock"))) { }
            var beforeFiles = Directory.GetFiles(root);
            long beforeBytes = beforeFiles.Sum(p => new FileInfo(p).Length);
            var next = At(perFamily + 2); // Domain fixture construction excluded.
            long[]? Metrics()
            {
                var property = typeof(WorkspaceFileStore).GetProperty("ReadMetrics", BindingFlags.NonPublic | BindingFlags.Instance);
                if (property is null) { return null; } // Immutable E3-3 has no read instrumentation.
                var value = property.GetValue(store)!;
                return MetricProperties
                    .Select(p => (long)value.GetType().GetProperty(p)!.GetValue(value)!).ToArray();
            }
            var beforeReads = Metrics();
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            StorageError error; string outcome; long? observedRevision;
            if (operation is "Load" or "Restore")
            {
                var result = operation == "Load" ? store.Load() : store.Restore();
                error = result.Error; observedRevision = result.Workspace?.Revision;
                outcome = error != StorageError.None ? "Refused" : operation == "Restore" && result.State == RecoveryState.BackupRecovered ? "Committed" : "ReadOnlyOrUnchanged";
            }
            else
            {
                var result = operation == "Save" ? store.Save(next) : store.Backup();
                error = result.Error; observedRevision = result.Revision;
                outcome = result.Succeeded ? result.Commit.ToString() : "Refused";
            }
            double elapsedMs = timer.Elapsed.TotalMilliseconds;
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var afterReads = Metrics();
            bool quotaRefusal = totalHistory == 512 && operation != "Load";
            if (error != (quotaRefusal ? StorageError.TooLarge : StorageError.None))
            { throw new InvalidOperationException("Measured operation produced an unexpected storage result."); }
            if (quotaRefusal && (!mainBytes.AsSpan().SequenceEqual(File.ReadAllBytes(main)) || Directory.GetFiles(root).Length != beforeFiles.Length))
            { throw new InvalidOperationException("Quota refusal mutated the fixture."); }
            if (!quotaRefusal && (operation is "Save" or "Restore") && (operation != "Restore" || totalHistory != 0) && observedRevision != perFamily + 2)
            { throw new InvalidOperationException("Measured successful mutation did not preserve the revision floor."); }
            var drive = new DriveInfo(Path.GetPathRoot(root)!);
            return new
            {
                seedDigest,
                repetition,
                warmup,
                totalHistory,
                backups = perFamily,
                prepared = perFamily,
                operation,
                outcome,
                error = error.ToString(),
                observedRevision,
                elapsedMs,
                allocatedBytes,
                readMetricsAvailable = afterReads is not null,
                successfulReadFiles = afterReads is null ? (long?)null : afterReads[0] - beforeReads![0],
                successfulReadBytes = afterReads is null ? (long?)null : afterReads[1] - beforeReads![1],
                enumerationEntries = afterReads is null ? (long?)null : afterReads[2] - beforeReads![2],
                beforeFiles = beforeFiles.Length,
                beforeBytes,
                afterFiles = Directory.GetFiles(root).Length,
                filesystem = drive.DriveFormat,
                totalDiskBytes = drive.TotalSize,
                availableDiskBytes = drive.AvailableFreeSpace
            };
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
