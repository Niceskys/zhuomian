using System.Diagnostics;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V2KilledMatrixTests
{
    [Theory]
    [MemberData(nameof(V2TransactionMatrixTests.Cases), MemberType = typeof(V2TransactionMatrixTests))]
    public async Task TerminatedProcessAtEveryTransactionCutIsResolvedByANewProcess(string operation, V2Stage stage, int write)
    {
        using var root = new OwnedDirectory();
        var empty = Workspace.Create(new Guid("00000001-0000-0000-0000-000000000000"));
        if (operation != "Initialize")
        { using var setup = WorkspaceV2Store.ForTests(root.Root); Assert.True(setup.InitializeNew(empty).Succeeded); }
        var original = V2TransactionMatrixTests.Evidence(root);
        using var child = Start(root.Root, operation.ToLowerInvariant(), stage, write);
        try
        {
            Assert.Equal("cut", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            using var competing = WorkspaceV2Store.ForTests(root.Root);
            Assert.Equal(StorageError.Busy, competing.Save(empty).Error);
            Assert.Equal(StorageError.Busy, competing.RestoreRevision(0).Error);
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
        long next = operation == "Restore" ? 1 : 10;
        long? main = stage == V2Stage.AfterMain ? next : operation == "Initialize" ? null : 0;
        using var restarted = Start(root.Root, "load", stage, 0);
        string? output = await restarted.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(main is null ? "RecoveryRequired:" : "None:" + main, output);
        await restarted.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(main is null ? 1 : 0, restarted.ExitCode);
        V2TransactionMatrixTests.AssertRetained(root, original);
        bool reserved = stage is V2Stage.AfterReservation or V2Stage.BeforeMain or V2Stage.AfterMain || write == 3;
        var atCut = Directory.GetFiles(root.Root).GroupBy(p => Path.GetFileName(p).Split('-')[0])
            .Select(g => new { family = g.Key, files = g.Count(), bytes = g.Sum(p => new FileInfo(p).Length) }).ToArray();
        using var retry = WorkspaceV2Store.ForTests(root.Root);
        if (reserved || operation != "Initialize")
        {
            var nextSnapshot = Workspace.FromSnapshot(empty.Id, empty.SchemaVersion, next, empty.ItemStore, empty.Spaces.Values);
            if (reserved)
            {
                Assert.Equal(StorageError.StaleRevision, retry.Save(empty).Error);
                Assert.Equal(StorageError.RevisionConflict, retry.Save(V2TransactionMatrixTests.Snapshot(next)).Error);
            }
            Assert.True(retry.Save(nextSnapshot).Succeeded);
            Assert.Equal(next, retry.Load().Workspace!.Revision);
        }
        else
        {
            Assert.False(retry.Save(empty).Succeeded);
            Assert.Equal(stage == V2Stage.BeforeWrite && write == 1, retry.InitializeNew(empty).Succeeded);
        }
        V2TransactionMatrixTests.WriteEvidence($"killed-{operation}-{stage}-{write}.json", new
        {
            operation,
            stage = stage.ToString(),
            write,
            windowsNtfs = OperatingSystem.IsWindows(),
            fault = "owned child Process.Kill; not physical power loss",
            diskAtCut = atCut,
            readableRevision = main,
            trustedReservationRevision = reserved ? next : operation == "Initialize" ? (long?)null : 0,
            mayResumeSave = reserved || operation != "Initialize",
            evidenceRetained = true,
            newProcessResult = output,
            competingSaveAndRestore = "Busy",
        });
    }

    private static Process Start(string root, string mode, V2Stage stage, int write)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "v2-fixture", "Zhuomian.Persistence.V2Fixture.dll"));
        start.ArgumentList.Add(root); start.ArgumentList.Add(stage.ToString()); start.ArgumentList.Add(mode);
        start.ArgumentList.Add(write.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Process.Start(start)!;
    }
}
