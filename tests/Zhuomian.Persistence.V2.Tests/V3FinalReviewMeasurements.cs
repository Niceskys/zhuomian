using System.Diagnostics;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed partial class V3PerformanceTests
{
    // Add measurements to the existing real-capacity fixture, without replacing any
    // original sample, assertion, fault cut or capacity Save.
    private static void MeasureFinalReview(Probe[] sources, string[] labels, int n, List<object> samples)
    {
        int rounds = n == 1024 ? 7 : 1;
        for (int round = 0; round < rounds; round++)
        {
            for (int variant = 0; variant < 3; variant++)
            {
                int index = (variant + round) % 3;
                string[] operations = ["Load", "Save", "Restore"];
                for (int op = 0; op < 3; op++)
                {
                    string operation = operations[(op + round) % 3];
                    // All three variants share uninstrumented/fresh conditions.
                    // Current binaries additionally pair profiles on fresh versus
                    // verified-Load-warmed clones, to expose metadata/write waits.
                    int conditions = index == 0 ? 1 : 3;
                    for (int c = 0; c < conditions; c++)
                    {
                        int condition = (c + round) % conditions;
                        using var clone = new OwnedDirectory();
                        var setup = Stopwatch.StartNew();
                        foreach (string path in Directory.GetFiles(sources[index].Root))
                        {
                            if (Path.GetFileName(path) != "workspace.lock")
                            { File.Copy(path, Path.Combine(clone.Root, Path.GetFileName(path))); }
                        }
                        setup.Stop();
                        double warmMs = 0;
                        if (condition == 2)
                        {
                            using var warm = new Probe(clone.Root, null, index == 2);
                            var timer = Stopwatch.StartNew(); Assert.Equal(n, warm.Load());
                            warmMs = timer.Elapsed.TotalMilliseconds;
                        }
                        bool profile = condition != 0;
                        using var selected = new Probe(clone.Root, null, index == 2,
                            index == 0 ? sources[0].BaselineAssembly : null, profile);
                        object desired = selected.Decode(WorkspaceCodec.Encode(V3AuthorityTests.Snapshot(n + 1)));
                        using var process = Process.GetCurrentProcess();
                        TimeSpan cpu = process.TotalProcessorTime, pause = GC.GetTotalPauseDuration();
                        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
                        long allocation = GC.GetAllocatedBytesForCurrentThread();
                        var elapsed = Stopwatch.StartNew();
                        selected.Operation(operation, desired);
                        elapsed.Stop();
                        long managedBytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
                        double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                        double gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
                        int[] collections = [GC.CollectionCount(0) - gc0, GC.CollectionCount(1) - gc1, GC.CollectionCount(2) - gc2];
                        // Read counters before the independent post-operation full Load.
                        object? metrics = profile ? selected.Metrics : null;
                        Assert.Equal(operation == "Load" ? n : n + 1, selected.Load());
                        var files = Directory.GetFiles(clone.Root);
                        samples.Add(new
                        {
                            saves = n,
                            round = round + 1,
                            implementation = labels[index],
                            operation,
                            condition = condition switch { 0 => "uninstrumented-fresh", 1 => "profiled-fresh", _ => "profiled-warm" },
                            elapsedMs = elapsed.Elapsed.TotalMilliseconds,
                            managedBytes,
                            cpuMs,
                            gcPauseMs,
                            collections,
                            copyMs = setup.Elapsed.TotalMilliseconds,
                            warmMs,
                            metrics,
                            files = files.Length,
                            logicalBytes = files.Sum(p => new FileInfo(p).Length),
                            ntfsAllocationBytes = OperatingSystem.IsWindows() ? files.Sum(AllocatedBytes) : (long?)null,
                            freeBytes = new DriveInfo(Path.GetPathRoot(clone.Root)!).AvailableFreeSpace,
                            expectedRevision = operation == "Load" ? n : n + 1,
                        });
                    }
                }
            }
        }
    }
}
