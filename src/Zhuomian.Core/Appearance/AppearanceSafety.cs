using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Appearance;

/// <summary>Opaque content backing and foreground, supplied by a trusted adapter. No accepted default colors.</summary>
public sealed record ReadablePalette
{
    public ReadablePalette(RgbColor background, RgbColor foreground)
    {
        ArgumentNullException.ThrowIfNull(background);
        ArgumentNullException.ThrowIfNull(foreground);
        if (background.ContrastRatio(foreground) < MinimumContrastRatio) { throw new ArgumentException("Palette fails the content contrast floor."); }
        Background = background;
        Foreground = foreground;
    }
    // Conservative domain safety floor for normal text; not proof of actual rendered accessibility.
    public const double MinimumContrastRatio = 4.5;
    public RgbColor Background { get; }
    public RgbColor Foreground { get; }
}

public sealed record AppearanceEnvironment
{
    public AppearanceEnvironment(bool highContrast = false, bool reducedTransparency = false,
        bool reducedMotion = false, ReadablePalette? highContrastPalette = null)
    {
        if (highContrast && highContrastPalette is null) { throw new ArgumentNullException(nameof(highContrastPalette)); }
        HighContrast = highContrast;
        ReducedTransparency = reducedTransparency;
        ReducedMotion = reducedMotion;
        HighContrastPalette = highContrastPalette;
    }
    public bool HighContrast { get; }
    public bool ReducedTransparency { get; }
    public bool ReducedMotion { get; }
    public ReadablePalette? HighContrastPalette { get; }
}

/// <summary>Capability observations, not claims that a renderer or decoder has been implemented.</summary>
public sealed record AppearanceCapabilities(bool SupportsSimulatedGlass, bool SupportsImages, bool SupportsShadows);

/// <summary>Injected resource/performance limits. They do not freeze provisional product budgets.</summary>
public sealed record AppearanceBudget
{
    public AppearanceBudget(bool allowSimulatedGlass, bool allowImages, bool allowShadows,
        double maximumCornerRadius, double maximumShadowBlur, long maximumEncodedBytes,
        long maximumImagePixels, long maximumDecodedBytes)
    {
        AllowSimulatedGlass = allowSimulatedGlass;
        AllowImages = allowImages;
        AllowShadows = allowShadows;
        MaximumCornerRadius = AppearanceGuard.Number(maximumCornerRadius, nameof(maximumCornerRadius));
        MaximumShadowBlur = AppearanceGuard.Number(maximumShadowBlur, nameof(maximumShadowBlur));
        ArgumentOutOfRangeException.ThrowIfNegative(maximumEncodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumImagePixels);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDecodedBytes);
        MaximumEncodedBytes = maximumEncodedBytes;
        MaximumImagePixels = maximumImagePixels;
        MaximumDecodedBytes = maximumDecodedBytes;
    }
    public bool AllowSimulatedGlass { get; }
    public bool AllowImages { get; }
    public bool AllowShadows { get; }
    public double MaximumCornerRadius { get; }
    public double MaximumShadowBlur { get; }
    public long MaximumEncodedBytes { get; }
    public long MaximumImagePixels { get; }
    public long MaximumDecodedBytes { get; }
}

/// <summary>Trusted safety inputs, separate from user/theme overrides.</summary>
public sealed record AppearanceSafetyPolicy
{
    public AppearanceSafetyPolicy(ReadablePalette palette, double minimumSurfaceOpacity)
    {
        ArgumentNullException.ThrowIfNull(palette);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumSurfaceOpacity);
        Palette = palette;
        MinimumSurfaceOpacity = AppearanceGuard.Number(minimumSurfaceOpacity, nameof(minimumSurfaceOpacity), 0, 1);
    }
    public ReadablePalette Palette { get; }
    public double MinimumSurfaceOpacity { get; }
}

public enum ImageAvailability { Unverified, Available, Missing, Invalid, Unsupported }

/// <summary>Adapter observation bound to one resource. No file inspection, decoding or trust grant.</summary>
public sealed record ImageAssessment
{
    public ImageAssessment(ResourceReference reference, ImageAvailability availability,
        long encodedBytes = 0, int width = 0, int height = 0, long decodedBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(reference);
        Availability = AppearanceGuard.Defined(availability);
        ArgumentOutOfRangeException.ThrowIfNegative(encodedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfNegative(decodedBytes);
        if (availability == ImageAvailability.Available && (encodedBytes == 0 || width == 0 || height == 0 || decodedBytes == 0))
        {
            throw new ArgumentException("Available images require positive resource metadata.");
        }
        Reference = reference;
        EncodedBytes = encodedBytes;
        Width = width;
        Height = height;
        DecodedBytes = decodedBytes;
    }
    public ResourceReference Reference { get; }
    public ImageAvailability Availability { get; }
    public long EncodedBytes { get; }
    public int Width { get; }
    public int Height { get; }
    public long DecodedBytes { get; }
}

/// <summary>Safe diagnostic codes; never include paths, filenames, commands or decoder errors.</summary>
[Flags]
public enum AppearanceReason
{
    None = 0,
    HighContrast = 1 << 0,
    ReducedTransparency = 1 << 1,
    ReducedMotion = 1 << 2,
    PlatformMaterialUnsupported = 1 << 3,
    MaterialBudgetExceeded = 1 << 4,
    OpacitySafetyFloor = 1 << 5,
    ContentContrastProtection = 1 << 6,
    PlatformShadowUnsupported = 1 << 7,
    ShadowBudgetExceeded = 1 << 8,
    CornerRadiusBudgetExceeded = 1 << 9,
    PlatformImageUnsupported = 1 << 10,
    ImageBudgetExceeded = 1 << 11,
    ImageUnverified = 1 << 12,
    ImageMissing = 1 << 13,
    ImageInvalid = 1 << 14,
    ImageUnsupported = 1 << 15,
    ImageAssessmentMismatch = 1 << 16,
}
