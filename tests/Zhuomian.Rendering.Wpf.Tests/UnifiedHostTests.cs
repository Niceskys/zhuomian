using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Zhuomian.Platform.Windows;

namespace Zhuomian.Rendering.Wpf.Tests;

public sealed class UnifiedHostTests
{
    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern nint SetThreadDpiAwarenessContext(nint context);
    }
    private static void OnStaThread(nint dpiContext, Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            nint previous = NativeMethods.SetThreadDpiAwarenessContext(dpiContext);
            try
            {
                Assert.NotEqual(0, previous);
                test();
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
                NativeMethods.SetThreadDpiAwarenessContext(previous);
            }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "WPF smoke owner thread did not terminate.");
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }
    [Fact]
    public void ActualHiddenWpfSourceUsesOneHostAndReleasesAllOwnedResources()
    {
        var messages = new List<string>();
        // VSTest's apphost has no product manifest. Model its native owner-thread context
        // explicitly; the published EXE separately verifies the real PerMonitorV2 manifest.
        OnStaThread(-4, () => WpfSmoke.Run(messages.Add));
        Assert.Contains(messages, line => line.Contains("wpf_smoke hidden=true", StringComparison.Ordinal));
        Assert.Contains(messages, line => line.Contains("wpf_rollback", StringComparison.Ordinal));
    }
    [Fact]
    public void NonPerMonitorThreadIsRejectedBeforeHostCreation()
    {
        OnStaThread(-1, () =>
        {
            using var renderer = new WpfHostRenderer();
            using var host = new FallbackDesktopHost(_ => { }, renderer);
            var error = Assert.Throws<InvalidOperationException>(() => host.Start(false));
            Assert.Equal("dpi_context_rejected", error.Message);
            Assert.Equal(0, renderer.Handle);
            Assert.Equal(0, host.ActiveHookCount);
        });
    }
}
