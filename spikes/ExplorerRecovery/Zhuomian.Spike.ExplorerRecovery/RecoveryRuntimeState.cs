namespace Zhuomian.Spike.ExplorerRecovery;

internal sealed class RecoveryRuntimeState
{
    public string DesktopAvailability { get; private set; } = "DesktopAvailable";

    public string IntentGate { get; private set; } = "Armed";

    public string KeyboardMode { get; private set; } = "ZhuomianKeyboardActive";

    public string SpaceState { get; private set; } = "Focused";

    public bool InputCaptureReleased { get; private set; }

    public bool AnimationCallbacksStopped { get; private set; }

    public bool MediaResourcesReleased { get; private set; }

    public void OnHostLost()
    {
        DesktopAvailability = "Suspended";
        IntentGate = "Disarmed";
        KeyboardMode = "NoKeyboardCapture";
        SpaceState = "Idle";
        InputCaptureReleased = true;
        AnimationCallbacksStopped = true;
        MediaResourcesReleased = true;
    }

    public void OnHostRestored()
    {
        DesktopAvailability = "DesktopAvailable";
        IntentGate = "Disarmed";
        KeyboardMode = "NoKeyboardCapture";
        SpaceState = "Idle";
    }
}
