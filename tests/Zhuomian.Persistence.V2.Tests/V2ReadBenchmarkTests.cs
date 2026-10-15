using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

// Serialize this benchmark with the capacity and other test classes: timings are diagnostic,
// not a CI latency threshold. Every reader still verifies every native file and payload.
[CollectionDefinition("v2 read benchmark", DisableParallelization = true)]
public sealed class V2ReadBenchmarkGroup;

[Collection("v2 read benchmark")]
public sealed partial class V2ReadBenchmarkTests
{
    [Fact]
    public void BalancedRepeatedReadsMeasureFullHistoryWithoutProtocolOrEvidenceChanges()
    {
        using var root = new OwnedDirectory();
        using var writer = WorkspaceV2Store.ForTests(root.Root);
        var create = Stopwatch.StartNew(); Assert.True(writer.InitializeNew(V2TransactionMatrixTests.Snapshot(0)).Succeeded);
        double initializeMs = create.Elapsed.TotalMilliseconds;
        List<object> samples = [];
        List<object> growth = [];
        for (int revision = 0; revision <= 1024; revision++)
        {
            if (revision != 0) { Assert.True(writer.Save(V2TransactionMatrixTests.Snapshot(revision)).Succeeded); }
            if (revision is not (0 or 32 or 128 or 256 or 512 or 1024)) { continue; }
            var original = V2TransactionMatrixTests.Evidence(root);
            string[] files = Directory.GetFiles(root.Root);
            long? allocated = OperatingSystem.IsWindows() ? files.Sum(AllocatedBytes) : null;
            growth.Add(new
            {
                saves = revision,
                pages = Directory.GetFiles(root.Root, "segment-*.bin").Length,
                witnesses = Directory.GetFiles(root.Root, "root-*.bin").Length,
                files = files.Length,
                logicalBytes = files.Sum(p => new FileInfo(p).Length),
                ntfsAllocationBytes = allocated,
                allocationScope = "FILE_STANDARD_INFO.AllocationSize; excludes directory/MFT/journal/volume metadata",
            });
            foreach (bool reference in new[] { true, false })
            {
                using var warmup = WorkspaceV2Store.ForReadBenchmark(root.Root, reference);
                Assert.Equal(revision, warmup.Load().Workspace!.Revision);
            }
            for (int round = 1; round <= 3; round++)
            {
                foreach (bool reference in round % 2 == 1 ? new[] { true, false } : new[] { false, true })
                {
                    foreach (string operation in new[] { "Load", "Backup", "UnchangedSave", "RefusedSave", "Save", "Restore" })
                    {
                        using var clone = new OwnedDirectory();
                        string selectedRoot = root.Root;
                        if (operation is "Save" or "Restore")
                        {
                            foreach (string path in files)
                            { if (Path.GetFileName(path) != "workspace.lock") { File.Copy(path, Path.Combine(clone.Root, Path.GetFileName(path))); } }
                            selectedRoot = clone.Root;
                        }
                        using var store = WorkspaceV2Store.ForReadBenchmark(selectedRoot, reference);
                        var desired = V2TransactionMatrixTests.Snapshot(operation == "Save" ? revision + 1 : revision);
                        var rejected = V2TransactionMatrixTests.Snapshot(revision, different: true);
                        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
                        using var process = Process.GetCurrentProcess(); TimeSpan cpuStart = process.TotalProcessorTime;
                        var timer = Stopwatch.StartNew();
                        StorageError error; string outcome;
                        if (operation == "Load") { var result = store.Load(); error = result.Error; outcome = result.State.ToString(); Assert.Equal(revision, result.Workspace!.Revision); }
                        else if (operation == "Restore") { var result = store.RestoreRevision(0); error = result.Error; outcome = result.State.ToString(); Assert.Equal(revision + 1, result.Workspace!.Revision); }
                        else
                        {
                            var result = operation == "Backup" ? store.Backup() : store.Save(operation == "RefusedSave" ? rejected : desired);
                            error = result.Error; outcome = result.Commit.ToString();
                        }
                        timer.Stop(); double cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
                        long allocation = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
                        Assert.Equal(operation == "RefusedSave" ? StorageError.RevisionConflict : StorageError.None, error);
                        if (operation == "Save") { Assert.Equal("Committed", outcome); }
                        if (operation is "Backup" or "UnchangedSave") { Assert.Equal("Unchanged", outcome); }
                        var metrics = store.ReadMetrics;
                        Assert.Equal(3L * revision + 3 + (operation is "Save" or "Restore" ? 6 : 0), metrics.Files); // root + EVERY page/witness + exact write readbacks.
                        samples.Add(new
                        {
                            saves = revision,
                            round,
                            reader = reference ? "reference-dd6cf01" : "exact-owned-buffer",
                            operation,
                            outcome,
                            error = error.ToString(),
                            elapsedMs = timer.Elapsed.TotalMilliseconds,
                            cpuMs,
                            allocatedManagedBytes = allocation,
                            logicalReadFiles = metrics.Files,
                            logicalReadBytes = metrics.Bytes,
                            nativeReadElapsedMs = metrics.ReadMs,
                            payloadDecodeMs = metrics.DecodeMs,
                        });
                    }
                }
            }
            V2TransactionMatrixTests.AssertRetained(root, original);
        }
        Assert.Equal(1024, writer.Load().Workspace!.Revision);
        V2TransactionMatrixTests.WriteEvidence("v2-read-benchmark.json", new
        {
            platform = RuntimeInformation.OSDescription,
            sdkRuntime = Environment.Version.ToString(),
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            windowsNtfs = OperatingSystem.IsWindows(),
            initializeMs,
            realDistinctSaves = 1024,
            growth,
            samples,
            protocol = "one full Load warmup per reader; 3 paired rounds AB/BA/AB per checkpoint; fixtures/clones outside operation timer",
            deviation = "Persistence microbenchmark diagnostic; no UI frame metric; not product 60s/300s calibration",
            physicalIoMeasured = false,
            thresholdCalibration = false,
        });
    }

    [SupportedOSPlatform("windows")]
    private static long AllocatedBytes(string path)
    {
        using SafeFileHandle handle = NativeStorage.Open(path, false, 0x80000000, 1, 3);
        if (!GetFileInformationByHandleEx(handle, 1, out var info, Marshal.SizeOf<FileStandardInfo>()))
        { throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()); }
        return info.AllocationSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        internal long AllocationSize, EndOfFile;
        internal uint NumberOfLinks;
        internal byte DeletePending, Directory;
    }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass,
        out FileStandardInfo information, int size);
}
