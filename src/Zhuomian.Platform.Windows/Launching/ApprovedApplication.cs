using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Platform.Windows.Launching;

/// <summary>
/// Code-composition approval, never populated directly from Workspace/import/provider output.
/// Digest must come from a reviewed deployment manifest, not be learned from the file at click time.
/// Exact identity AND provenance required. There is deliberately no argument/verb/working-directory field.
/// </summary>
public sealed class ApprovedApplication
{
    public ApprovedApplication(ApplicationIdentity identity, ApplicationProvenance provenance, WindowsPath executable, string sha256)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentNullException.ThrowIfNull(sha256);
        if (sha256.Length != 64 || !sha256.All(char.IsAsciiHexDigit)) { throw new ArgumentException("A deployment SHA256 is required.", nameof(sha256)); }
        Identity = identity;
        Provenance = provenance;
        Executable = executable;
        Sha256 = sha256.ToUpperInvariant();
    }
    public ApplicationIdentity Identity { get; }
    public ApplicationProvenance Provenance { get; }
    public WindowsPath Executable { get; }
    public string Sha256 { get; }
    public override string ToString() => nameof(ApprovedApplication);
}
