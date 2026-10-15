namespace Zhuomian.Platform.Windows;

/// <summary>Ownership and rollback, isolated from Core. Dispose is dispatcher-affine.</summary>
public sealed class HostLease : IDisposable
{
    private readonly Action<nint> _release;
    private nint _handle;
    public HostLease(Action<nint> release) => _release = release ?? throw new ArgumentNullException(nameof(release));
    public nint Handle => _handle;
    public void Create(Func<nint> create, Action<nint> configure)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(configure);
        if (_handle != 0) { throw new InvalidOperationException("host_already_created"); }
        nint candidate = create();
        if (candidate == 0) { throw new InvalidOperationException("host_create_failed"); }
        try { configure(candidate); _handle = candidate; }
        catch { _release(candidate); throw; }
    }
    public void Dispose()
    {
        nint owned = _handle;
        _handle = 0;
        if (owned != 0) { _release(owned); }
    }
}
