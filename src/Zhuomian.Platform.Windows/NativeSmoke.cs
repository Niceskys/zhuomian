using System.Diagnostics;
using System.Runtime.Versioning;
using static Zhuomian.Platform.Windows.NativeMethods;

namespace Zhuomian.Platform.Windows;

[SupportedOSPlatform("windows")]
public static class NativeSmoke
{
    /// <summary>Actual hidden HWND checks; never injects mouse input, changes Shell or captures the desktop.</summary>
    public static void Run(Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        nint foreground = GetForegroundWindow();
        var elapsed = Stopwatch.StartNew();
        using var host = new FallbackDesktopHost(log);
        host.Start(visible: false);
        nint window = host.Window;
        long styles = (long)GetWindowLongPtrW(window, -20);
        if (!IsWindow(window) || (styles & 0x08000080) != 0x08000080 || (styles & 8) != 0 ||
            SendMessageW(window, 0x21, 0, 0) != 3 || GetForegroundWindow() != foreground)
        { throw new InvalidOperationException("native_smoke_failed"); }
        log(FormattableString.Invariant($"native_smoke hidden=true windows_build={Environment.OSVersion.Version.Build} dpi={host.Dpi} startup_ms={elapsed.Elapsed.TotalMilliseconds:F3} hwnd_valid=true noactivate=true topmost=false mouseactivate=3 foreground_unchanged=true hooks={host.ActiveHookCount}"));
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        log(FormattableString.Invariant($"hidden_snapshot private_bytes={process.PrivateMemorySize64} working_set_bytes={process.WorkingSet64} handles={process.HandleCount} threads={process.Threads.Count} owned_windows=1 hooks={host.ActiveHookCount} evidence=CI_diagnostic_not_product_idle"));
        host.Dispose();
        if (IsWindow(window) || host.ActiveHookCount != 0 || host.Dispatcher.Controller.PointerCaptured || host.Dispatcher.Controller.FocusOwner is not null)
        { throw new InvalidOperationException("native_smoke_cleanup_failed"); }
        log("native_smoke_cleanup hwnd_destroyed=true hooks=0 pointer_capture=false focus_owner=false");
    }
}
