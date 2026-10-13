using System.ComponentModel;
using Zhuomian.Core.Launching;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Launching.Windows;

/// <summary>Small exact catalog, not discovery. Default empty; unknown providers can never execute.</summary>
public sealed class WindowsLaunchResolver : ILaunchIdentityResolver
{
    private readonly Dictionary<ApplicationIdentity, ApprovedApplication> _applications;
    private static readonly HashSet<string> ForbiddenHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32",
        "msiexec", "installutil", "regasm", "regsvcs", "wmic", "forfiles", "bash", "wsl",
        "python", "pythonw", "node", "java", "dotnet", "conhost", "wt", "explorer",
    };

    public WindowsLaunchResolver(IEnumerable<ApprovedApplication>? applications = null) =>
        _applications = (applications ?? []).ToDictionary(app => app.Identity);

    public LaunchResolution Resolve(Item item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        var policy = LaunchPolicy.Check(item);
        if (policy.Status != LaunchStatus.Accepted) { return new(policy); }
        if (item is ApplicationItem unknown && (!_applications.TryGetValue(unknown.Identity, out var known) || known.Provenance != unknown.Provenance))
        { return new(new(LaunchStatus.NoTrustedApplication, LaunchReason.UntrustedProvider)); }
        if (!OperatingSystem.IsWindows()) { return new(new(LaunchStatus.Unsupported, LaunchReason.PlatformUnavailable)); }
        try
        {
            if (item is ApplicationItem application)
            {
                var approval = _applications[application.Identity];
                var pathPolicy = LaunchPolicy.CheckPath(approval.Executable, file: false);
                string name = Path.GetFileNameWithoutExtension(approval.Executable.Value);
                if (pathPolicy.Status != LaunchStatus.Accepted ||
                    !Path.GetExtension(approval.Executable.Value).Equals(".exe", StringComparison.OrdinalIgnoreCase) || ForbiddenHosts.Contains(name))
                { return new(new(LaunchStatus.Denied, LaunchReason.UnsafeTarget)); }
                return new(new(LaunchStatus.Accepted), new WindowsResourceLease(approval.Executable, false, null,
                    approval.Sha256, LaunchTargetKind.Application, application.Identity, application.Provenance, cancellationToken));
            }
            // Inspect real local filesystem even when association execution remains disabled. No fallback to ShellExecute.
            if (item is FileItem file)
            {
                using var lease = new WindowsResourceLease(file.Path, false, file.StableFileIdentity, null, LaunchTargetKind.File, cancellationToken: cancellationToken);
                return new(new(LaunchStatus.Unsupported, LaunchReason.UnreviewedAssociation));
            }
            if (item is FolderItem folder)
            {
                using var lease = new WindowsResourceLease(folder.Path, true, folder.StableFileIdentity, null, LaunchTargetKind.Folder, cancellationToken: cancellationToken);
                return new(new(LaunchStatus.Unsupported, LaunchReason.UnreviewedAssociation));
            }
            return new(new(LaunchStatus.Unsupported, LaunchReason.UnreviewedAssociation));
        }
        catch (UnsafeResourceException) { return new(new(LaunchStatus.Denied, LaunchReason.UnsafeTarget)); }
        catch (ReplacedResourceException) { return new(new(LaunchStatus.Denied, LaunchReason.ResourceReplaced)); }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException)
        { return new(new(LaunchStatus.MissingOrInaccessible, LaunchReason.ResourceUnavailable)); }
    }
}
