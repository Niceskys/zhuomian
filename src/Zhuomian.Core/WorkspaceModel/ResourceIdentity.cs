namespace Zhuomian.Core.WorkspaceModel;

/// <summary>Pure lexical Windows identity. Does not inspect existence, links, permissions or trust.</summary>
public sealed record WindowsPath
{
    public WindowsPath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl) || value.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0)
        {
            throw new ArgumentException("Invalid path syntax.", nameof(value));
        }
        string path = value.Replace('/', '\\');
        string root;
        string remainder;
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\')
        {
            root = char.ToUpperInvariant(path[0]) + ":\\";
            remainder = path[3..];
        }
        else if (path.StartsWith("\\\\", StringComparison.Ordinal) &&
            !path.StartsWith("\\\\.\\", StringComparison.Ordinal) && !path.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            string[] parts = path[2..].Split('\\');
            if (parts.Length < 2) { throw new ArgumentException("UNC path requires a server and share.", nameof(value)); }
            ValidateSegment(parts[0]);
            ValidateSegment(parts[1]);
            root = "\\\\" + parts[0] + "\\" + parts[1] + "\\";
            remainder = string.Join('\\', parts.Skip(2));
        }
        else { throw new ArgumentException("An absolute drive or UNC path is required.", nameof(value)); }

        var segments = new List<string>();
        foreach (string segment in remainder.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") { continue; }
            if (segment == "..")
            {
                if (segments.Count == 0) { throw new ArgumentException("Path escapes its root.", nameof(value)); }
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            ValidateSegment(segment);
            segments.Add(segment);
        }
        Value = root + string.Join('\\', segments);
        CanonicalKey = Value.ToUpperInvariant();
    }

    private static void ValidateSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." || segment.Contains(':', StringComparison.Ordinal) ||
            segment.EndsWith(' ') || segment.EndsWith('.'))
        {
            throw new ArgumentException("Ambiguous path segment.", nameof(segment));
        }
    }

    public string Value { get; }
    public string CanonicalKey { get; }
    public override string ToString() => nameof(WindowsPath);
}

/// <summary>Provider-scoped opaque application identity; contains no command line or arguments.</summary>
public sealed record ApplicationIdentity
{
    public ApplicationIdentity(string providerId, string applicationId)
    {
        ProviderId = DomainValidation.Text(providerId);
        ApplicationId = DomainValidation.Text(applicationId);
    }

    public string ProviderId { get; }
    public string ApplicationId { get; }
    public override string ToString() => nameof(ApplicationIdentity);
}

public enum ApplicationOrigin { DiscoveryProvider, ExplicitSelection }

public sealed record ApplicationProvenance
{
    public ApplicationProvenance(ApplicationOrigin origin, string sourceId)
    {
        if (!Enum.IsDefined(origin)) { throw new ArgumentOutOfRangeException(nameof(origin)); }
        Origin = origin;
        SourceId = DomainValidation.Text(sourceId);
    }
    public ApplicationOrigin Origin { get; }
    public string SourceId { get; }
    public override string ToString() => nameof(ApplicationProvenance);
}

public sealed record HttpUrl
{
    public HttpUrl(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ArgumentException("An HTTP(S) destination without credentials is required.", nameof(value));
        }
        Value = uri.AbsoluteUri;
    }
    public string Value { get; }
    public override string ToString() => nameof(HttpUrl);
}

/// <summary>Ownership is explicit; references never grant deletion or decoding authority.</summary>
public abstract record ResourceReference
{
    private protected ResourceReference() { }
}

public sealed record ExternalResourceReference : ResourceReference
{
    public ExternalResourceReference(WindowsPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
    }
    public WindowsPath Path { get; }
    public override string ToString() => nameof(ExternalResourceReference);
}

public sealed record ManagedAssetReference : ResourceReference
{
    public ManagedAssetReference(Guid assetId) => AssetId = DomainValidation.Id(assetId);
    public Guid AssetId { get; }
}

public sealed record ItemMetadata
{
    public ItemMetadata(string displayName, ResourceReference? icon = null)
    {
        DisplayName = DomainValidation.Text(displayName);
        Icon = icon;
    }
    public string DisplayName { get; }
    public ResourceReference? Icon { get; }
    public override string ToString() => nameof(ItemMetadata);
}
