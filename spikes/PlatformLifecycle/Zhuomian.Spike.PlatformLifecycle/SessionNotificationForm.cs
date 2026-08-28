namespace Zhuomian.Spike.PlatformLifecycle;

internal sealed class SessionNotificationForm : Form
{
    private const uint NotifyForThisSession = 0;

    internal bool Register() => NativeMethods.WTSRegisterSessionNotification(Handle, NotifyForThisSession);

    internal bool Unregister() => NativeMethods.WTSUnRegisterSessionNotification(Handle);
}
