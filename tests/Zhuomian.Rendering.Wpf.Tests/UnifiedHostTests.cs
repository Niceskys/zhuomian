using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Zhuomian.Rendering.Wpf;

namespace Zhuomian.Rendering.Wpf.Tests;

public sealed class UnifiedHostTests
{
    [Fact]
    public void ActualHiddenWpfSourceUsesOneHostAndReleasesAllOwnedResources()
    {
        Exception? failure = null;
        var messages = new List<string>();
        var thread = new Thread(() =>
        {
            try { WpfSmoke.Run(messages.Add); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "WPF smoke owner thread did not terminate.");
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
        Assert.Contains(messages, line => line.Contains("wpf_smoke hidden=true", StringComparison.Ordinal));
        Assert.Contains(messages, line => line.Contains("wpf_rollback", StringComparison.Ordinal));
    }
}
