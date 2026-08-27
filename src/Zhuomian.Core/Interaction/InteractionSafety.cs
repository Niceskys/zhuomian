namespace Zhuomian.Core.Interaction;

public static class InteractionSafety
{
    public static bool CanHoverEnterIntent(
        in InteractionSnapshot snapshot,
        ActivationMode activationMode)
    {
        if (activationMode is not ActivationMode.Hover ||
            snapshot.Availability is DesktopAvailability.Suspended)
        {
            return false;
        }

        return snapshot.Availability is DesktopAvailability.DesktopAvailable ||
            (snapshot.Availability is DesktopAvailability.ExternalForeground &&
             snapshot.IntentGate is DesktopIntentGate.Armed);
    }

    public static bool CanCaptureKeyboard(in InteractionSnapshot snapshot) =>
        snapshot.KeyboardMode is KeyboardMode.ZhuomianKeyboardActive;

    public static bool SuppressesAutoCollapse(in InteractionSnapshot snapshot) =>
        snapshot.Activities is not InteractionActivity.None;
}
