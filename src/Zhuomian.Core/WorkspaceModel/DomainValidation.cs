using System.Text;

namespace Zhuomian.Core.WorkspaceModel;

internal static class DomainValidation
{
    internal static Guid Id(Guid id)
    {
        if (id == Guid.Empty) { throw new ArgumentException("Identity must not be empty.", nameof(id)); }
        return id;
    }

    internal static string Text(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(char.IsControl)) { throw new ArgumentException("Control characters are not allowed.", nameof(value)); }
        return value.Trim().Normalize(NormalizationForm.FormC);
    }
}

public enum DomainError
{
    SpaceNotFound,
    ItemNotFound,
    IdentityAlreadyUsed,
    ResourceAlreadyKnown,
    ReferenceAlreadyExists,
    ReferenceNotFound,
    InvalidOrder,
    ConfirmationRequired,
}

/// <summary>Safe codes only: exception messages never echo paths, URLs or user names.</summary>
public sealed class WorkspaceOperationException(DomainError error) : InvalidOperationException(error.ToString())
{
    public DomainError Error { get; } = error;
}
