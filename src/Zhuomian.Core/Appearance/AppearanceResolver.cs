using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Appearance;

/// <summary>Resolved render description only. A renderer must honor opaque content backing and all final values.</summary>
public sealed class AppearanceResolution
{
    internal AppearanceResolution(Guid spaceId, AppearanceState state, AppearanceValues inherited,
        AppearanceValues effective, ReadablePalette contentPalette, bool requiresOpaqueContentBacking,
        bool allowMotion, AppearanceReason reasons)
    {
        SpaceId = spaceId;
        State = state;
        Inherited = inherited;
        Effective = effective;
        ContentPalette = contentPalette;
        RequiresOpaqueContentBacking = requiresOpaqueContentBacking;
        AllowMotion = allowMotion;
        Reasons = reasons;
    }
    public Guid SpaceId { get; }
    public AppearanceState State { get; }
    public AppearanceValues Inherited { get; }
    public AppearanceValues Effective { get; }
    public SurfaceMaterial ActualMaterial => Effective.Material;
    public ReadablePalette ContentPalette { get; }
    public bool RequiresOpaqueContentBacking { get; }
    public bool AllowMotion { get; }
    public AppearanceReason Reasons { get; }
}

public static class AppearanceResolver
{
    /// <summary>O(1) five-layer inheritance per Space; no Workspace mutation, layout calculation or runtime ownership.</summary>
    public static AppearanceResolution Resolve(Guid spaceId, AppearanceState state, AppearanceConfiguration configuration,
        AppearanceSafetyPolicy safety, AppearanceEnvironment environment, AppearanceCapabilities capabilities,
        AppearanceBudget budget, ImageAssessment? imageAssessment = null)
    {
        DomainValidation.Id(spaceId);
        AppearanceGuard.Defined(state);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(safety);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(budget);
        var inherited = AppearanceValues.BuiltInSafe(safety.Palette);
        inherited = configuration.GlobalTheme.Common.Apply(inherited);
        inherited = configuration.GlobalTheme.ForState(state).Apply(inherited);
        if (configuration.Spaces.TryGetValue(spaceId, out var layers))
        {
            inherited = layers.Common.Apply(inherited);
            inherited = layers.ForState(state).Apply(inherited);
        }
        return ApplySafety(spaceId, state, inherited, safety, environment, capabilities, budget, imageAssessment);
    }

    private static AppearanceResolution ApplySafety(Guid spaceId, AppearanceState state, AppearanceValues requested,
        AppearanceSafetyPolicy safety, AppearanceEnvironment environment, AppearanceCapabilities capabilities,
        AppearanceBudget budget, ImageAssessment? assessment)
    {
        AppearanceReason reasons = AppearanceReason.None;
        var material = requested.Material;
        var tint = requested.Tint;
        double opacity = requested.Opacity;
        double radius = requested.CornerRadius;
        var shadow = requested.Shadow;
        var image = requested.Image;
        var fallback = requested.FallbackColor;
        var palette = environment.HighContrast ? environment.HighContrastPalette! : safety.Palette;

        if (environment.ReducedMotion) { reasons |= AppearanceReason.ReducedMotion; }
        if (material == SurfaceMaterial.SimulatedGlass)
        {
            if (!capabilities.SupportsSimulatedGlass) { reasons |= AppearanceReason.PlatformMaterialUnsupported; }
            if (!budget.AllowSimulatedGlass) { reasons |= AppearanceReason.MaterialBudgetExceeded; }
            if (!capabilities.SupportsSimulatedGlass || !budget.AllowSimulatedGlass)
            {
                material = SurfaceMaterial.Solid;
                tint = fallback;
                opacity = 1;
            }
        }
        if (shadow.Opacity == 0) { shadow = ShadowSpec.None; }
        if (shadow.Opacity > 0)
        {
            if (!capabilities.SupportsShadows) { reasons |= AppearanceReason.PlatformShadowUnsupported; }
            if (!budget.AllowShadows || shadow.BlurRadius > budget.MaximumShadowBlur) { reasons |= AppearanceReason.ShadowBudgetExceeded; }
            if (!capabilities.SupportsShadows || !budget.AllowShadows || shadow.BlurRadius > budget.MaximumShadowBlur) { shadow = ShadowSpec.None; }
        }
        if (radius > budget.MaximumCornerRadius)
        {
            radius = budget.MaximumCornerRadius;
            reasons |= AppearanceReason.CornerRadiusBudgetExceeded;
        }
        if (image.Reference is { } reference)
        {
            var imageReasons = AssessImage(reference, assessment, capabilities, budget);
            reasons |= imageReasons;
            if (imageReasons != AppearanceReason.None)
            {
                image = ImageBackground.None;
                material = SurfaceMaterial.Solid;
                tint = fallback;
                opacity = 1;
            }
        }
        // Accessibility restrictions are final and cannot be restored by a user override.
        if (environment.ReducedTransparency)
        {
            reasons |= AppearanceReason.ReducedTransparency;
            material = SurfaceMaterial.Solid;
            opacity = 1;
            tint = fallback;
            image = ImageBackground.None;
        }
        if (environment.HighContrast)
        {
            reasons |= AppearanceReason.HighContrast;
            material = SurfaceMaterial.Solid;
            opacity = 1;
            tint = palette.Background;
            fallback = palette.Background;
            shadow = ShadowSpec.None;
            image = ImageBackground.None;
            radius = 0;
        }
        if (opacity < safety.MinimumSurfaceOpacity)
        {
            opacity = safety.MinimumSurfaceOpacity;
            reasons |= AppearanceReason.OpacitySafetyFloor;
        }
        // Wallpaper and image colors are unknown. Decorative tint must never tint/fade the content layer.
        bool backing = material != SurfaceMaterial.Solid || opacity < 1 || image.Reference is not null ||
            tint.ContrastRatio(palette.Foreground) < ReadablePalette.MinimumContrastRatio;
        if (backing) { reasons |= AppearanceReason.ContentContrastProtection; }
        var effective = new AppearanceValues(material, tint, opacity, radius, shadow, image, fallback);
        return new(spaceId, state, requested, effective, palette, backing, !environment.ReducedMotion, reasons);
    }

    private static AppearanceReason AssessImage(ResourceReference reference, ImageAssessment? assessment,
        AppearanceCapabilities capabilities, AppearanceBudget budget)
    {
        AppearanceReason reasons = AppearanceReason.None;
        if (!capabilities.SupportsImages) { reasons |= AppearanceReason.PlatformImageUnsupported; }
        if (!budget.AllowImages) { reasons |= AppearanceReason.ImageBudgetExceeded; }
        if (assessment is null) { return reasons | AppearanceReason.ImageUnverified; }
        if (assessment.Reference != reference) { return reasons | AppearanceReason.ImageAssessmentMismatch; }
        reasons |= assessment.Availability switch
        {
            ImageAvailability.Unverified => AppearanceReason.ImageUnverified,
            ImageAvailability.Missing => AppearanceReason.ImageMissing,
            ImageAvailability.Invalid => AppearanceReason.ImageInvalid,
            ImageAvailability.Unsupported => AppearanceReason.ImageUnsupported,
            _ => AppearanceReason.None,
        };
        if (assessment.Availability != ImageAvailability.Available) { return reasons; }
        long pixels = (long)assessment.Width * assessment.Height; // int.MaxValue squared fits in long.
        // Division avoids overflow for hostile dimensions. At least RGBA8 memory is required.
        if (pixels > assessment.DecodedBytes / 4) { reasons |= AppearanceReason.ImageInvalid; }
        if (assessment.EncodedBytes > budget.MaximumEncodedBytes || pixels > budget.MaximumImagePixels ||
            assessment.DecodedBytes > budget.MaximumDecodedBytes || pixels > budget.MaximumDecodedBytes / 4)
        {
            reasons |= AppearanceReason.ImageBudgetExceeded;
        }
        return reasons;
    }
}
