namespace Zhuomian.Core.Tests.Interaction;

public sealed class ForegroundEventDebouncePolicyTests
{
    [Fact]
    public void FirstHintRequestsLeadingEdgeReconciliationImmediately()
    {
        var model = new ForegroundEventDebounceReferenceModel();

        var request = model.ObserveHint(125);

        Assert.Equal(125, request.DueAtMilliseconds);
        var startedGeneration = model.TryStart(125, request.Generation);
        Assert.True(startedGeneration.HasValue);
        Assert.Equal(request.Generation, startedGeneration.Value);
        Assert.True(model.CanApply(startedGeneration.Value));
    }

    [Fact]
    public void QuietBurstUsesTrailingSettleWindow()
    {
        var model = StartAtZero();

        Assert.Equal(60, model.ObserveHint(10).DueAtMilliseconds);
        Assert.Equal(70, model.ObserveHint(20).DueAtMilliseconds);
        var trailing = model.ObserveHint(40);

        Assert.Equal(90, trailing.DueAtMilliseconds);
        Assert.Null(model.TryStart(89, trailing.Generation));
        Assert.Equal(trailing.Generation, model.TryStart(90, trailing.Generation));
    }

    [Fact]
    public void ContinuousHintsCannotStarveAuthoritativeReconciliation()
    {
        var model = StartAtZero();

        model.ObserveHint(20);
        model.ObserveHint(40);
        model.ObserveHint(60);
        model.ObserveHint(80);
        var bounded = model.ObserveHint(99);

        Assert.Equal(
            ForegroundEventDebounceReferenceModel.MaximumReconciliationGapMilliseconds,
            bounded.DueAtMilliseconds);
        Assert.Null(model.TryStart(99, bounded.Generation));
        Assert.Equal(bounded.Generation, model.TryStart(100, bounded.Generation));
    }

    [Fact]
    public void NewHintInvalidatesAnOlderReconciliationResult()
    {
        var model = new ForegroundEventDebounceReferenceModel();
        var first = model.ObserveHint(0);
        var firstGeneration = model.TryStart(0, first.Generation);
        Assert.True(firstGeneration.HasValue);

        var newer = model.ObserveHint(10);

        Assert.False(model.CanApply(firstGeneration.Value));
        Assert.Null(model.TryStart(59, newer.Generation));
        var newerGeneration = model.TryStart(60, newer.Generation);
        Assert.True(newerGeneration.HasValue);
        Assert.True(model.CanApply(newerGeneration.Value));
    }

    [Fact]
    public void ObsoleteTimerGenerationCannotConsumeNewerPendingWork()
    {
        var model = StartAtZero();
        var obsolete = model.ObserveHint(10);
        var current = model.ObserveHint(20);

        Assert.Null(model.TryStart(60, obsolete.Generation));
        Assert.True(model.Pending.HasValue);
        Assert.Equal(current, model.Pending.Value);
        Assert.Equal(current.Generation, model.TryStart(70, current.Generation));
    }

    [Fact]
    public void DrainedPolicyHasNoIdlePollingWork()
    {
        var model = new ForegroundEventDebounceReferenceModel();
        var request = model.ObserveHint(0);

        Assert.Equal(request.Generation, model.TryStart(0, request.Generation));
        Assert.Null(model.Pending);
    }

    private static ForegroundEventDebounceReferenceModel StartAtZero()
    {
        var model = new ForegroundEventDebounceReferenceModel();
        var request = model.ObserveHint(0);
        Assert.Equal(request.Generation, model.TryStart(0, request.Generation));
        return model;
    }
}
