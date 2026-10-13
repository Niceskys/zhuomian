using System.ComponentModel;
using System.Diagnostics;
using Zhuomian.Core.Launching;

namespace Zhuomian.Platform.Windows.Launching;

public sealed class WindowsLaunchExecutor : ILaunchExecutor
{
    public LaunchResult Submit(ILaunchLease lease, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lease);
        if (cancellationToken.IsCancellationRequested) { return new(LaunchStatus.Cancelled, LaunchReason.RequestCancelled); }
        if (!OperatingSystem.IsWindows()) { return new(LaunchStatus.Unsupported, LaunchReason.PlatformUnavailable); }
        // Only this adapter's private, pinned lease can reach CreateProcess. Injected descriptions are not permissions.
        if (lease is not WindowsResourceLease || lease.Target.Kind != LaunchTargetKind.Application)
        { return new(LaunchStatus.Unsupported, LaunchReason.UnreviewedAssociation); }
        var verified = lease.Revalidate();
        if (verified.Status != LaunchStatus.Accepted) { return verified; }
        if (cancellationToken.IsCancellationRequested) { return new(LaunchStatus.Cancelled, LaunchReason.RequestCancelled); }
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = lease.Target.Target,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(lease.Target.Target)!,
                CreateNoWindow = true,
            };
            // Zero arguments. Absolute path; no PATH lookup, verbs, shell command, UAC or download automation.
            using var process = Process.Start(start);
            return process is null ? new(LaunchStatus.OperatingSystemFailure, LaunchReason.ExecutorFailure) :
                new(LaunchStatus.SubmittedToOperatingSystem);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return new(LaunchStatus.OperatingSystemFailure, LaunchReason.ExecutorFailure); }
        // Process wrapper/handles disposed. User application is never killed on cancellation after submission.
    }
}
