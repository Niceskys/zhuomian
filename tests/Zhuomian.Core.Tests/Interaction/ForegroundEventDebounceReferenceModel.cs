namespace Zhuomian.Core.Tests.Interaction;

internal readonly record struct ForegroundReconciliationRequest(
    long DueAtMilliseconds,
    long Generation);

internal sealed class ForegroundEventDebounceReferenceModel
{
    internal const long SettleWindowMilliseconds = 50;
    internal const long MaximumReconciliationGapMilliseconds = 100;

    private long _generation;
    private long? _lastHintAtMilliseconds;
    private long? _lastReconciliationStartedAtMilliseconds;
    private ForegroundReconciliationRequest? _pending;

    internal ForegroundReconciliationRequest? Pending => _pending;

    internal ForegroundReconciliationRequest ObserveHint(long atMilliseconds)
    {
        if (_lastHintAtMilliseconds is { } lastHint && atMilliseconds < lastHint)
        {
            throw new ArgumentOutOfRangeException(
                nameof(atMilliseconds),
                "Foreground hints must be observed in monotonic order.");
        }

        _lastHintAtMilliseconds = atMilliseconds;
        _generation++;

        if (_lastReconciliationStartedAtMilliseconds is null)
        {
            var dueAt = _pending?.DueAtMilliseconds ?? atMilliseconds;
            _pending = new(dueAt, _generation);
            return _pending.Value;
        }

        var settleDeadline = atMilliseconds + SettleWindowMilliseconds;
        var hardDeadline = _lastReconciliationStartedAtMilliseconds.Value +
            MaximumReconciliationGapMilliseconds;
        var nextDueAt = Math.Min(settleDeadline, hardDeadline);

        _pending = new(nextDueAt, _generation);
        return _pending.Value;
    }

    internal long? TryStart(long atMilliseconds, long expectedGeneration)
    {
        if (_pending is not { } pending ||
            pending.Generation != expectedGeneration ||
            atMilliseconds < pending.DueAtMilliseconds)
        {
            return null;
        }

        _pending = null;
        _lastReconciliationStartedAtMilliseconds = atMilliseconds;
        return pending.Generation;
    }

    internal bool CanApply(long generation) => generation == _generation;
}
