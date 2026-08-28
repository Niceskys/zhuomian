using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Zhuomian.Spike.FallbackVisualUsability;

internal sealed class ShellDesktopController : IDisposable
{
    private readonly Type _shellType;
    private object? _shell;
    private bool _minimized;

    internal ShellDesktopController()
    {
        _shellType = Type.GetTypeFromProgID("Shell.Application") ??
            throw new InvalidOperationException("Shell.Application is unavailable.");
        _shell = Activator.CreateInstance(_shellType) ??
            throw new InvalidOperationException("Shell.Application could not be created.");
    }

    internal void MinimizeAll()
    {
        Invoke("MinimizeAll");
        _minimized = true;
    }

    internal void RestoreAll()
    {
        if (!_minimized)
        {
            return;
        }

        Invoke("UndoMinimizeALL");
        _minimized = false;
    }

    public void Dispose()
    {
        RestoreAll();
        if (_shell is not null && Marshal.IsComObject(_shell))
        {
            Marshal.FinalReleaseComObject(_shell);
        }

        _shell = null;
    }

    private void Invoke(string method)
    {
        _shellType.InvokeMember(
            method,
            BindingFlags.InvokeMethod,
            binder: null,
            target: _shell,
            args: null,
            modifiers: null,
            culture: CultureInfo.InvariantCulture,
            namedParameters: null);
    }
}
