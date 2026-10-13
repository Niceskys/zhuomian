using Zhuomian.Core.Interaction;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Platform.Tests;

public sealed class ZOrderPlacementTests
{
    [Fact]
    public void ShellBoundaryCompletesEnumerationWithoutFalseFailureAndKeepsUnknownWindowsAboveHost()
    {
        var visited = new List<nint>(); var inspected = new List<nint>();
        nint anchor = ZOrderPlacement.SelectAnchor(10, 99, true, DesktopAvailability.DesktopAvailable,
            callback =>
            {
                foreach (nint window in new nint[] { 10, 20, 30, 99, 40 })
                {
                    visited.Add(window);
                    if (!callback(window)) { return false; } // Real EnumWindows stop semantics.
                }
                return true;
            }, window => { inspected.Add(window); return true; }); // Unknown visible windows are conservatively retained.
        Assert.Equal(30, anchor);
        Assert.Equal([10, 20, 30, 99, 40], visited);
        Assert.Equal([20, 30], inspected);
    }
    [Fact]
    public void MissingShellHandleKeepsBottomWithoutEnumerating()
    {
        Assert.Equal(1, ZOrderPlacement.SelectAnchor(10, 0, true, DesktopAvailability.DesktopAvailable,
            _ => throw new InvalidOperationException("must_not_enumerate"), _ => true));
    }
    [Fact]
    public void ShellNotFoundInSuccessfulEnumerationKeepsBottom()
    {
        Assert.Equal(1, ZOrderPlacement.SelectAnchor(10, 99, true, DesktopAvailability.DesktopAvailable,
            callback => callback(20) && callback(30), _ => true));
    }
    [Fact]
    public void ExposedDesktopWithNoCoveringWindowUsesOnlyNormalTop()
    {
        Assert.Equal(0, ZOrderPlacement.SelectAnchor(10, 99, true, DesktopAvailability.DesktopAvailable,
            callback => callback(10) && callback(20) && callback(99), _ => false));
    }
    [Theory]
    [InlineData(DesktopAvailability.ExternalForeground, true)]
    [InlineData(DesktopAvailability.Suspended, true)]
    [InlineData(DesktopAvailability.DesktopAvailable, false)]
    public void ExternalUnsafeOrLostHostNeverRaises(DesktopAvailability availability, bool hostAvailable)
    {
        Assert.Equal(1, ZOrderPlacement.SelectAnchor(10, 99, hostAvailable, availability,
            _ => throw new InvalidOperationException("must_not_enumerate"), _ => true));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealEnumerationFailureIsNotHiddenByShellFound(bool shellFound)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ZOrderPlacement.SelectAnchor(10, 99, true,
            DesktopAvailability.DesktopAvailable, callback => { if (shellFound) { Assert.True(callback(99)); } return false; }, _ => true));
        Assert.Equal("zorder_enumeration_failed", error.Message);
    }
    [Fact]
    public void ActualUser32EnumerationFindsControlledBoundaryWithoutChangingForegroundOrStyles()
    {
        // Controlled hidden HWND boundary, not Shell foreground/Win+D/visible desktop acceptance.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) { return; }
        nint foreground = NativeMethods.GetForegroundWindow();
        using var host = new FallbackDesktopHost(_ => { }); host.Start(visible: false);
        nint window = host.Window;
        nint anchor = ZOrderPlacement.SelectAnchor(window, window, true, DesktopAvailability.DesktopAvailable,
            callback => NativeMethods.EnumWindows((handle, _) => callback(handle), 0), _ => false);
        Assert.Equal(0, anchor); // Proves the controlled boundary was actually enumerated.
        Assert.True(NativeMethods.SetWindowPos(window, anchor, 0, 0, 0, 0, 0x13));
        Assert.Equal(foreground, NativeMethods.GetForegroundWindow());
        long styles = (long)NativeMethods.GetWindowLongPtrW(window, -20);
        Assert.Equal(0, styles & 8); Assert.NotEqual(0, styles & 0x08000000);
        Assert.Equal(3, NativeMethods.SendMessageW(window, 0x21, 0, 0));
        Assert.Equal(1, ZOrderPlacement.SelectAnchor(window, NativeMethods.GetShellWindow(), true,
            DesktopAvailability.ExternalForeground, _ => throw new InvalidOperationException("must_not_enumerate"), _ => true));
        host.Dispose(); Assert.False(NativeMethods.IsWindow(window)); Assert.Equal(0, host.ActiveHookCount);
    }
}
