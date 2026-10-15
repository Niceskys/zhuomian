using Zhuomian.Core.Interaction;

namespace Zhuomian.Platform.Windows;

/// <summary>Native adapter policy; handles never cross into Core. Enumeration always completes.</summary>
internal static class ZOrderPlacement
{
    internal static nint SelectAnchor(nint host, nint shell, bool hostAvailable, DesktopAvailability availability,
        Func<Func<nint, bool>, bool> enumerate, Func<nint, bool> mustRemainAboveHost)
    {
        ArgumentNullException.ThrowIfNull(enumerate);
        ArgumentNullException.ThrowIfNull(mustRemainAboveHost);
        // Without an authoritative Shell boundary there is no evidence permitting a raise.
        if (!hostAvailable || availability != DesktopAvailability.DesktopAvailable || shell == 0) { return 1; }
        nint candidate = 0; // HWND_TOP is ordinary Z-order, never HWND_TOPMOST.
        bool shellFound = false;
        bool succeeded = enumerate(window =>
        {
            if (window == shell) { shellFound = true; }
            else if (!shellFound && window != host && mustRemainAboveHost(window)) { candidate = window; }
            // FALSE would also make EnumWindows return zero; stop collecting, not enumerating.
            return true;
        });
        if (!succeeded) { throw new InvalidOperationException("zorder_enumeration_failed"); }
        return shellFound ? candidate : 1; // Missing Shell after a successful enumeration remains bottom.
    }
}
