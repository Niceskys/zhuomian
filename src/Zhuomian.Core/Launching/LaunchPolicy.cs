using System.Globalization;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Launching;

/// <summary>Lexical rejection only. Permission still requires a live reviewed resolver and executor.</summary>
public static class LaunchPolicy
{
    private static readonly string[] DataExtensions = [".txt", ".pdf", ".png", ".jpg", ".jpeg"];
    public static LaunchResult Check(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Availability == ItemAvailability.Missing) { return new(LaunchStatus.MissingOrInaccessible, LaunchReason.ResourceUnavailable); }
        if (item.Availability == ItemAvailability.Unsupported) { return new(LaunchStatus.Unsupported, LaunchReason.UnsafeTarget); }
        return item switch
        {
            FileItem file => CheckPath(file.Path, file: true),
            FolderItem folder => CheckPath(folder.Path, file: false),
            UrlItem url => IsSafeUrl(url.Destination.Value) ? new(LaunchStatus.Accepted) : Deny(),
            ApplicationItem => new(LaunchStatus.Accepted),
            _ => new(LaunchStatus.Unsupported, LaunchReason.UnsafeTarget),
        };
    }

    public static LaunchResult CheckPath(WindowsPath path, bool file)
    {
        ArgumentNullException.ThrowIfNull(path);
        string value = path.Value;
        if (value.Length < 3 || !char.IsAsciiLetter(value[0]) || value[1] != ':' || value[2] != '\\' ||
            value.Length > 240 || HasInvisibleCharacters(value)) { return Deny(); }
        foreach (string segment in value[3..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = segment.Split('.')[0];
            if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && char.IsDigit(name[3]))) { return Deny(); }
        }
        // A positive data-extension allowlist. Association approval is an additional independent gate.
        if (file && !DataExtensions.Contains(
            Path.GetExtension(value), StringComparer.OrdinalIgnoreCase)) { return Deny(); }
        return new(LaunchStatus.Accepted);
    }

    public static bool IsSafeUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || HasInvisibleCharacters(value) || value.Contains('\\', StringComparison.Ordinal) ||
            (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length != 0 ||
            uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback || uri.Host.EndsWith('.')) { return false; }
        // Decode once to reject hidden delimiters/control characters. Encoded % is refused to avoid nested encodings.
        string decoded = Uri.UnescapeDataString(value);
        return !HasInvisibleCharacters(decoded) && !decoded.Contains('\\', StringComparison.Ordinal) &&
            !value.Contains("%25", StringComparison.OrdinalIgnoreCase) && !value.Contains("%40", StringComparison.OrdinalIgnoreCase) &&
            !value.Contains("%2f", StringComparison.OrdinalIgnoreCase) && !value.Contains("%3a", StringComparison.OrdinalIgnoreCase) &&
            uri.IdnHost.Contains('.', StringComparison.Ordinal) && !uri.IdnHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasInvisibleCharacters(string value) => value.Any(c => char.IsControl(c) ||
        char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
    private static LaunchResult Deny() => new(LaunchStatus.Denied, LaunchReason.UnsafeTarget);
}
