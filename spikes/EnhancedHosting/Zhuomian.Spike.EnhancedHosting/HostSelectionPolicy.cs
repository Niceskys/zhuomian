namespace Zhuomian.Spike.EnhancedHosting;

internal enum HostMode
{
    EnhancedWorkerW,
    PublicWin32Fallback,
}

internal sealed record SelectionInput(
    bool CandidateValid,
    bool AttachmentSucceeded,
    bool DpiContextPreserved);

internal static class HostSelectionPolicy
{
    public static HostMode Select(SelectionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.CandidateValid && input.AttachmentSucceeded && input.DpiContextPreserved
            ? HostMode.EnhancedWorkerW
            : HostMode.PublicWin32Fallback;
    }
}
