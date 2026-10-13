using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

public sealed class HostTests
{
    [Fact]
    public void ConfigureFailureReleasesOwnedCandidateExactlyOnce()
    {
        var released = new List<nint>();
        using var lease = new HostLease(released.Add);
        Assert.Throws<InvalidOperationException>(() => lease.Create(() => 123, _ => throw new InvalidOperationException()));
        Assert.Equal(0, lease.Handle); Assert.Equal([123], released);
    }
    [Fact]
    public void CreateFailureCannotReleaseAnUnownedWindow()
    {
        var released = new List<nint>(); using var lease = new HostLease(released.Add);
        Assert.Throws<InvalidOperationException>(() => lease.Create(() => 0, _ => { }));
        Assert.Empty(released);
    }
    [Fact]
    public void DuplicateCreateAndDisposeNeverLeakOrDoubleRelease()
    {
        var released = new List<nint>(); using var lease = new HostLease(released.Add);
        lease.Create(() => 123, _ => { });
        Assert.Throws<InvalidOperationException>(() => lease.Create(() => 456, _ => { }));
        lease.Dispose(); lease.Dispose(); Assert.Equal([123], released);
        lease.Create(() => 456, _ => { }); lease.Dispose(); Assert.Equal([123, 456], released);
    }
    [Fact]
    public void NativeHiddenAnimationRegionHitPaintingAndCancellation()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { return; }
        NativeSmoke.CheckHiddenMotion(_ => { });
    }
    [Fact]
    public void NativeHiddenWindowStylesNoActivateRollbackAndCleanup()
    {
        // Linux exercises portable adapters; Windows CI executes the real User32 path without physical input.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { return; }
        for (int i = 0; i < 3; i++)
        {
            nint foreground = NativeMethods.GetForegroundWindow();
            using var host = new FallbackDesktopHost(_ => { }); host.Start(visible: false);
            nint window = host.Window;
            long styles = (long)NativeMethods.GetWindowLongPtrW(window, -20);
            Assert.NotEqual(0, styles & 0x08000000); Assert.Equal(0, styles & 8);
            Assert.Equal(3, NativeMethods.SendMessageW(window, 0x21, 0, 0));
            Assert.Equal(foreground, NativeMethods.GetForegroundWindow());
            Assert.Equal(1, host.ActiveHookCount); Assert.True(NativeMethods.IsWindow(window));
            host.Dispose(); Assert.False(NativeMethods.IsWindow(window)); Assert.Equal(0, host.ActiveHookCount);
        }
        using var failed = new FallbackDesktopHost(_ => { }, () => throw new InvalidOperationException("injected_configure_failure"));
        Assert.Throws<InvalidOperationException>(() => { if (OperatingSystem.IsWindows()) { failed.Start(visible: false); } });
        Assert.Equal(0, failed.Window); Assert.Equal(0, failed.ActiveHookCount);
    }
}
