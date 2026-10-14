using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

[Collection("v2 read benchmark")]
public sealed partial class V3PerformanceTests
{
    [Fact]
    public void ImmutableBaselineAndBothProtocolsUseRealSavesAndBalancedRepeatedOperations()
    {
        string? baseline = Environment.GetEnvironmentVariable("E3_V2_BASELINE_DLL");
        if (OperatingSystem.IsWindows()) { Assert.False(string.IsNullOrEmpty(baseline)); }
        using var oldRoot = new OwnedDirectory(); using var currentRoot = new OwnedDirectory(); using var newRoot = new OwnedDirectory();
        using var before = new Probe(oldRoot.Root, baseline, false);
        using var current = new Probe(currentRoot.Root, null, false);
        using var recoverable = new Probe(newRoot.Root, null, true);
        Probe[] probes = [before, current, recoverable];
        string[] labels = [baseline is null ? "local-current-control" : "514a5594", "current-format2", "current-format3"];
        List<object> samples = [], profiles = [], checkpoints = [];
        foreach (var probe in probes) { probe.Initialize(WorkspaceCodec.Encode(V3AuthorityTests.Snapshot(0))); }
        for (int n = 1; n <= 1024; n++)
        {
            byte[] payload = WorkspaceCodec.Encode(V3AuthorityTests.Snapshot(n));
            double saveMs = 0; long saveAllocation = 0;
            foreach (var probe in probes)
            {
                object desired = probe.Decode(payload);
                long allocation = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
                probe.Operation("Save", desired); timer.Stop();
                if (probe == recoverable) { saveMs = timer.Elapsed.TotalMilliseconds; saveAllocation = GC.GetAllocatedBytesForCurrentThread() - allocation; }
            }
            if (n is not (32 or 128 or 256 or 512 or 1000 or 1024)) { continue; }
            long loadAllocation = GC.GetAllocatedBytesForCurrentThread(); var loadTimer = Stopwatch.StartNew();
            Assert.Equal(n, recoverable.Load()); loadTimer.Stop(); loadAllocation = GC.GetAllocatedBytesForCurrentThread() - loadAllocation;
            var files = Directory.GetFiles(newRoot.Root);
            checkpoints.Add(new
            {
                saves = n,
                successes = n,
                refusals = 0,
                revision = n,
                files = files.Length,
                pages = Directory.GetFiles(newRoot.Root, "segment-*.bin").Length,
                witnesses = Directory.GetFiles(newRoot.Root, "root-*.bin").Length,
                logicalBytes = files.Sum(p => new FileInfo(p).Length),
                ntfsAllocationBytes = OperatingSystem.IsWindows() ? files.Sum(AllocatedBytes) : (long?)null,
                saveMs,
                loadMs = loadTimer.Elapsed.TotalMilliseconds,
                saveAllocation,
                loadAllocation,
                freeBytes = new DriveInfo(Path.GetPathRoot(newRoot.Root)!).AvailableFreeSpace,
                filesystem = new DriveInfo(Path.GetPathRoot(newRoot.Root)!).DriveFormat,
                completeHistory = true,
            });
            Assert.Equal(3 * n + 4, files.Length);
            if (n == 1000) { continue; }
            foreach (var probe in probes) { Assert.Equal(n, probe.Load()); }
            for (int round = 1; round <= 3; round++)
            {
                foreach (int index in round % 2 == 1 ? new[] { 0, 1, 2 } : new[] { 2, 1, 0 })
                {
                    foreach (string operation in new[] { "Load", "Save", "Restore" })
                    {
                        using var clone = new OwnedDirectory();
                        foreach (string path in Directory.GetFiles(probes[index].Root))
                        { if (Path.GetFileName(path) != "workspace.lock") { File.Copy(path, Path.Combine(clone.Root, Path.GetFileName(path))); } }
                        using var selected = new Probe(clone.Root, null, index == 2, index == 0 ? before.BaselineAssembly : null);
                        object desired = selected.Decode(WorkspaceCodec.Encode(V3AuthorityTests.Snapshot(n + 1)));
                        long allocation = GC.GetAllocatedBytesForCurrentThread();
                        using var process = Process.GetCurrentProcess(); TimeSpan cpu = process.TotalProcessorTime;
                        var timer = Stopwatch.StartNew();
                        selected.Operation(operation, desired);
                        timer.Stop();
                        samples.Add(new
                        {
                            saves = n,
                            round,
                            implementation = labels[index],
                            operation,
                            elapsedMs = timer.Elapsed.TotalMilliseconds,
                            managedBytes = GC.GetAllocatedBytesForCurrentThread() - allocation,
                            cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                            expectedRevision = operation == "Load" ? n : n + 1,
                        });
                        Assert.Equal(operation == "Load" ? n : n + 1, selected.Load());
                    }
                }
            }
            // Separate instrumented operations: the latency pairs above have profiling disabled in ALL binaries.
            foreach (bool v3 in new[] { false, true })
            {
                foreach (string operation in new[] { "Load", "Save", "Restore" })
                {
                    using var clone = new OwnedDirectory(); string source = v3 ? newRoot.Root : currentRoot.Root;
                    foreach (string path in Directory.GetFiles(source))
                    { if (Path.GetFileName(path) != "workspace.lock") { File.Copy(path, Path.Combine(clone.Root, Path.GetFileName(path))); } }
                    using var store = WorkspaceV2Store.ForProfile(clone.Root, v3);
                    var desired = V3AuthorityTests.Snapshot(n + 1);
                    if (operation == "Load") { Assert.Equal(n, store.Load().Workspace!.Revision); }
                    else if (operation == "Save") { Assert.True(store.Save(desired).Succeeded); }
                    else { Assert.Equal(n + 1, store.RestoreRevision(0).Workspace!.Revision); }
                    profiles.Add(new { saves = n, format = v3 ? 3 : 2, operation, metrics = store.Profile });
                }
            }
        }
        recoverable.Dispose();
        using var restarted = WorkspaceV2Store.ForRecoverableTests(newRoot.Root);
        Assert.Equal(1024, restarted.Load().Workspace!.Revision);
        Assert.Equal(StorageError.StaleRevision, restarted.Save(V3AuthorityTests.Snapshot(256)).Error);
        Assert.Equal(StorageError.RevisionConflict, restarted.Save(V3AuthorityTests.Snapshot(1024, true)).Error);
        Assert.Equal(CommitState.Unchanged, restarted.Save(V3AuthorityTests.Snapshot(1024)).Commit);
        Assert.Equal(1025, restarted.RestoreRevision(256).Workspace!.Revision);
        Assert.Equal("revision-256", restarted.Load().Workspace!.Spaces[Samples.SpaceA].Name);
        using (var oldClient = new Probe(newRoot.Root, null, false, before.BaselineAssembly))
        {
            oldClient.RefusesForeignFormat(WorkspaceCodec.Encode(V3AuthorityTests.Snapshot(1026)));
            File.Delete(Path.Combine(newRoot.Root, "state-v2.bin"));
            oldClient.RefusesForeignFormat(WorkspaceCodec.Encode(V3AuthorityTests.Snapshot(1026)));
        }
        Assert.True(restarted.RecoverAuthority().Succeeded);
        Assert.Equal(1025, restarted.Load().Workspace!.Revision);
        V2TransactionMatrixTests.WriteEvidence("v3-performance.json", new
        {
            baselineSha = baseline is null ? null : "514a5594f594728d2093cb041ed3552472384472",
            windowsNtfs = OperatingSystem.IsWindows(),
            realDistinctSavesPerImplementation = 1024,
            checkpoints,
            samples,
            profiles,
            protocol = "warm Load; 3 ABC/CBA/ABC rounds; identical generated inputs; copies/Decode outside timers; all latency instrumentation disabled",
            scope = "format2 before/after same protocol; format2/3 workload comparison; logical metrics, not physical device I/O",
        });
    }

    private sealed class Probe : IDisposable
    {
        private readonly Type _storeType, _codecType;
        private readonly object _store;
        private readonly Assembly? _baselineAssembly;
        internal Assembly? BaselineAssembly => _baselineAssembly;
        internal string Root { get; }
        internal Probe(string root, string? baseline, bool v3, Assembly? existingBaseline = null)
        {
            Root = root;
            _baselineAssembly = existingBaseline;
            if (baseline is not null)
            {
                var context = new BaselineContext(Path.GetDirectoryName(baseline)!);
                _baselineAssembly = context.LoadFromAssemblyPath(baseline);
            }
            var assembly = _baselineAssembly ?? typeof(WorkspaceV2Store).Assembly;
            _storeType = assembly.GetType("Zhuomian.Persistence.WorkspaceV2Store")!;
            _codecType = assembly.GetType("Zhuomian.Persistence.WorkspaceCodec")!;
            _store = _storeType.GetMethod(v3 ? "ForRecoverableTests" : "ForTests", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [root, null])!;
        }
        internal object Decode(byte[] payload) => Property(_codecType.GetMethod("Decode")!.Invoke(null, [new ReadOnlyMemory<byte>(payload)])!, "Workspace")!;
        internal void Initialize(byte[] payload) => Success(Call("InitializeNew", Decode(payload)));
        internal void Save(byte[] payload) => Success(Call("Save", Decode(payload)));
        internal long Load()
        {
            var result = Call("Load"); Assert.Equal("None", Property(result, "Error")!.ToString());
            return (long)Property(Property(result, "Workspace")!, "Revision")!;
        }
        internal void Operation(string operation, object desired)
        {
            if (operation == "Save") { Success(Call("Save", desired)); }
            else { Assert.Equal("None", Property(operation == "Load" ? Call("Load") : Call("RestoreRevision", 0L), "Error")!.ToString()); }
        }
        internal void RefusesForeignFormat(byte[] payload)
        {
            Assert.NotEqual("None", Property(Call("Load"), "Error")!.ToString());
            foreach (string name in new[] { "Save", "InitializeNew" })
            { Assert.False((bool)Property(Call(name, Decode(payload)), "Succeeded")!); }
            Assert.NotEqual("None", Property(Call("RestoreRevision", 0L), "Error")!.ToString());
        }
        private object Call(string name, params object?[] args) => _storeType.GetMethod(name)!.Invoke(_store, args)!;
        private static object? Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value);
        private static void Success(object result) => Assert.True((bool)Property(result, "Succeeded")!);
        public void Dispose() => ((IDisposable)_store).Dispose();
    }
    private sealed class BaselineContext(string directory) : AssemblyLoadContext
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string path = Path.Combine(directory, assemblyName.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
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
