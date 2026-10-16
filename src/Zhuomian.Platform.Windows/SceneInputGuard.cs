namespace Zhuomian.Platform.Windows;

/// <summary>Conservative submitted-scene consensus, NOT a PresentedFrame oracle.
/// The 250ms engineering lag window is unverified on real displays. Overflow fails closed.
/// Last submission time per geometry is retained; identical geometry does not fill the cache.</summary>
public sealed class SceneInputGuard
{
    public const int Capacity = 32;
    public const long LagWindowMilliseconds = 250;
    private readonly List<(SceneSnapshot Scene, long Time)> _history = [];
    private long _overflowUntil = -1, _lastFrame = -1;
    public int RetainedScenes => _history.Count;
    public long Attempts { get; private set; }
    public long Accepted { get; private set; }
    public long Conflicts { get; private set; }
    public long GenerationRejected { get; private set; }
    public long CoverageRejected { get; private set; }
    public long Evicted { get; private set; }
    public long? PinnedFrameId { get; private set; }
    public void Observe(SceneSnapshot scene, long now)
    {
        if (scene.FrameId <= _lastFrame) { throw new InvalidOperationException("non_monotonic_submission"); }
        _lastFrame = scene.FrameId;
        Prune(now);
        if (_history.Count > 0 && SameGeometry(_history[^1].Scene, scene))
        { _history[^1] = (scene, now); return; }
        _history.Add((scene, now));
        if (_history.Count > Capacity)
        { _history.RemoveAt(0); Evicted++; _overflowUntil = now + LagWindowMilliseconds; }
    }
    public bool TryPointerDown(int x, int y, long generation, long now, out SurfaceHit? target)
    {
        Attempts++; PinnedFrameId = null; target = null; Prune(now);
        if (_history.Count == 0 || now <= _overflowUntil) { CoverageRejected++; return false; }
        var latest = _history[^1].Scene;
        if (latest.HostGeneration != generation || _history.Any(s => s.Scene.HostGeneration != generation))
        { GenerationRejected++; return false; }
        var hit = latest.HitTest(x, y);
        if (_history.Any(s => s.Scene.HitTest(x, y) != hit)) { Conflicts++; return false; }
        target = hit; PinnedFrameId = latest.FrameId; Accepted++; return true;
    }
    public void EndGesture() { PinnedFrameId = null; }
    public void Clear()
    { _history.Clear(); _overflowUntil = -1; PinnedFrameId = null; }
    private void Prune(long now)
    {
        // Retire old geometry only after its last submission leaves the engineering lag window.
        while (_history.Count > 1 && _history[0].Time < now - LagWindowMilliseconds)
        { _history.RemoveAt(0); }
    }
    private static bool SameGeometry(SceneSnapshot left, SceneSnapshot right)
    {
        if (left.HostGeneration != right.HostGeneration || left.PixelsPerDip != right.PixelsPerDip || left.MonitorId != right.MonitorId || left.Spaces.Length != right.Spaces.Length) { return false; }
        for (int i = 0; i < left.Spaces.Length; i++)
        {
            var a = left.Spaces[i]; var b = right.Spaces[i];
            if (a.Id != b.Id || a.Bounds != b.Bounds || a.CloseBounds != b.CloseBounds || a.ExitBounds != b.ExitBounds || a.Items.Length != b.Items.Length) { return false; }
            for (int j = 0; j < a.Items.Length; j++)
            { if (a.Items[j].Id != b.Items[j].Id || a.Items[j].Bounds != b.Items[j].Bounds) { return false; } }
        }
        return true;
    }
}
