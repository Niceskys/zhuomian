using System.Diagnostics;
using Zhuomian.Core.WorkspaceModel;
using Zhuomian.Persistence.Tests;

namespace Zhuomian.Persistence.V2.Tests;

public sealed class V3KilledRecoveryTests
{
    [Theory]
    [InlineData("initialize", V2Stage.BeforeReservation)]
    [InlineData("initialize", V2Stage.AfterReservation)]
    [InlineData("initialize", V2Stage.BeforeMain)]
    [InlineData("initialize", V2Stage.AfterMain)]
    [InlineData("save", V2Stage.BeforeReservation)]
    [InlineData("save", V2Stage.AfterReservation)]
    [InlineData("save", V2Stage.BeforeMain)]
    [InlineData("save", V2Stage.AfterMain)]
    [InlineData("recover", V2Stage.BeforeMain)]
    [InlineData("recover", V2Stage.AfterMain)]
    public async Task RealTerminationPreservesTheIndependentlyExpectedAuthority(string operation, V2Stage stage)
    {
        using var root = new OwnedDirectory();
        var empty = Workspace.Create(new Guid("00000001-0000-0000-0000-000000000000"));
        if (operation != "initialize")
        {
            using var setup = WorkspaceV2Store.ForRecoverableTests(root.Root);
            Assert.True(setup.InitializeNew(empty).Succeeded);
            if (operation == "recover")
            {
                Assert.True(setup.Save(Workspace.FromSnapshot(empty.Id, 1, 10, empty.ItemStore, empty.Spaces.Values)).Succeeded);
                File.Delete(Path.Combine(root.Root, "state-v2.bin"));
            }
        }
        using var child = Start(root.Root, operation, stage);
        try
        {
            Assert.Equal("cut", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            using var competing = WorkspaceV2Store.ForRecoverableTests(root.Root);
            Assert.Equal(StorageError.Busy, competing.RecoverAuthority().Error);
            Assert.Equal(StorageError.Busy, competing.Save(empty).Error);
            Assert.Equal(StorageError.Busy, competing.RestoreRevision(0).Error);
            child.Kill(entireProcessTree: true); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); } }
        long? floor = operation == "initialize" && stage == V2Stage.BeforeReservation ? null :
            operation == "save" && stage == V2Stage.BeforeReservation ? 0 : 10;
        var original = Directory.GetFiles(root.Root).Where(p => Path.GetFileName(p) is not "workspace.lock" and not "state-v2.bin")
            .ToDictionary(p => Path.GetFileName(p), File.ReadAllBytes);
        var diskAtCut = Directory.GetFiles(root.Root).GroupBy(p => Path.GetFileName(p).Split('-')[0])
            .Select(g => new { family = g.Key, files = g.Count(), logicalBytes = g.Sum(p => new FileInfo(p).Length) }).ToArray();
        using var restarted = WorkspaceV2Store.ForRecoverableTests(root.Root);
        var observed = restarted.Load();
        Assert.Equal(floor is not null, restarted.RecoverAuthority().Succeeded);
        foreach (var file in original) { Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(root.Root, file.Key))); }
        if (floor is not null)
        {
            Assert.Equal(floor, restarted.Load().Workspace!.Revision);
            using var reader = Start(root.Root, "load", stage);
            Assert.Equal("None:" + floor, await reader.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30)));
            await reader.WaitForExitAsync(); Assert.Equal(0, reader.ExitCode);
            Assert.Equal(StorageError.RevisionConflict, restarted.Save(V3AuthorityTests.Snapshot(floor.Value)).Error);
            Assert.Equal(floor + 1, restarted.Restore().Workspace!.Revision);
        }
        else { Assert.False(restarted.InitializeNew(empty).Succeeded); }
        V2TransactionMatrixTests.WriteEvidence($"v3-killed-{operation}-{stage}.json", new
        {
            operation,
            stage = stage.ToString(),
            windowsNtfs = OperatingSystem.IsWindows(),
            expectedReservation = floor,
            beforeRepairRevision = observed.Workspace?.Revision,
            beforeRepairError = observed.Error.ToString(),
            diskAtCut,
            mayResumeAfterRepair = floor is not null,
            retained = true,
            competing = "Busy",
            physicalPowerLoss = false,
        });
    }

    private static Process Start(string root, string mode, V2Stage stage)
    {
        var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "v2-fixture", "Zhuomian.Persistence.V2Fixture.dll"));
        info.ArgumentList.Add(root); info.ArgumentList.Add(stage.ToString()); info.ArgumentList.Add("v3-" + mode);
        return Process.Start(info)!;
    }
}
