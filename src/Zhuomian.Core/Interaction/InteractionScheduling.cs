namespace Zhuomian.Core.Interaction;

/// <summary>A monotonic clock, independent of wall-clock adjustments.</summary>
public interface IInteractionClock
{
    long NowMilliseconds { get; }
}

public readonly record struct InteractionDeadline(long Token, long DueAtMilliseconds);

/// <summary>One-shot scheduling. Cancellation is best effort; the controller also validates tokens.</summary>
public interface IInteractionScheduler
{
    void Schedule(InteractionDeadline deadline);
    void Cancel(long token);
    IReadOnlyList<InteractionDeadline> TakeDue(long nowMilliseconds);
}

/// <summary>Pull-based deterministic scheduler. The dispatcher wakes at NextDeadline, never polls at idle.</summary>
public sealed class DeterministicInteractionScheduler : IInteractionScheduler
{
    private readonly SortedSet<InteractionDeadline> _deadlines = new(
        Comparer<InteractionDeadline>.Create((a, b) =>
        {
            int due = a.DueAtMilliseconds.CompareTo(b.DueAtMilliseconds);
            return due != 0 ? due : a.Token.CompareTo(b.Token);
        }));
    private readonly Dictionary<long, InteractionDeadline> _byToken = [];

    public long? NextDeadline => _deadlines.Count == 0 ? null : _deadlines.Min.DueAtMilliseconds;

    public void Schedule(InteractionDeadline deadline)
    {
        _byToken.Add(deadline.Token, deadline);
        _deadlines.Add(deadline);
    }

    public void Cancel(long token)
    {
        if (_byToken.Remove(token, out var deadline))
        {
            _deadlines.Remove(deadline);
        }
    }

    public IReadOnlyList<InteractionDeadline> TakeDue(long nowMilliseconds)
    {
        var due = new List<InteractionDeadline>();
        while (_deadlines.Count > 0 && _deadlines.Min.DueAtMilliseconds <= nowMilliseconds)
        {
            var deadline = _deadlines.Min;
            Cancel(deadline.Token);
            due.Add(deadline);
        }

        return due;
    }
}
