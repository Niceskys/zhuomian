using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

[CollectionDefinition("Native resource diagnostics", DisableParallelization = true)]
public sealed class NativeResourceTestGroup;

[Collection("Native resource diagnostics")]
public sealed class WorkAreaTests
{
    [Fact]
    public void UnrelatedNotificationsDoNotReplaceTheAcceptedSnapshotOrPoll()
    {
        var initial = new WorkAreaSnapshot(0, 0, 1200, 800); var current = initial; int calls = 0;
        var guard = new WorkAreaGuard(initial, () => { calls++; return current; });
        Assert.Equal(0, calls);
        Assert.Equal(WorkAreaStatus.Unchanged, guard.Check()); Assert.Equal(1, calls);
        current = initial with { Bottom = 760 };
        Assert.Equal(WorkAreaStatus.Changed, guard.Check());
        Assert.Equal(WorkAreaStatus.Changed, guard.Check()); // Never silently accept changed coordinates.
        var restarted = new WorkAreaGuard(current, () => current);
        Assert.Equal(WorkAreaStatus.Unchanged, restarted.Check());
    }

    [Theory]
    [InlineData(-40, 0, 1200, 800)]
    [InlineData(0, 40, 1200, 800)]
    [InlineData(0, 0, 1160, 800)]
    [InlineData(0, 0, 1200, 760)]
    public void EveryWorkAreaEdgeIsPartOfTheCoordinateContract(int left, int top, int right, int bottom)
    {
        var guard = new WorkAreaGuard(new(0, 0, 1200, 800), () => new(left, top, right, bottom));
        Assert.Equal(WorkAreaStatus.Changed, guard.Check());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("inverted")]
    [InlineData("exception")]
    public void UntrustworthyQueriesFailClosed(string failure)
    {
        WorkAreaSnapshot? Query() => failure switch
        {
            "null" => null,
            "empty" => new(0, 0, 0, 800),
            "inverted" => new(1200, 0, 0, 800),
            _ => throw new InvalidOperationException("synthetic_private_payload")
        };
        var guard = new WorkAreaGuard(new(0, 0, 1200, 800), Query);
        Assert.Equal(WorkAreaStatus.Unavailable, guard.Check());
    }

    [Fact]
    public void NativeWorkAreaMessagesReleasePinnedInputTimersCompletionsAndResources()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { return; }
        NativeSmoke.CheckWorkAreaShutdown(_ => { });
    }

    [Fact]
    public void NativeRegionPaintResourcesAndOwnerThreadAreVerified()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { return; }
        NativeSmoke.CheckRegionAndOwnership(_ => { });
    }
}
