using Zhuomian.Core.Appearance;
using Zhuomian.Core.WorkspaceModel;
using static Zhuomian.Core.Tests.Appearance.AppearanceFixtures;

namespace Zhuomian.Core.Tests.Appearance;

public sealed class AppearanceSafetyTests
{
    private static AppearanceConfiguration Decorated() => Config(new()
    {
        Material = Set(SurfaceMaterial.SimulatedGlass),
        Tint = Set(White),
        Opacity = Set(0d),
        CornerRadius = Set(20d),
        Shadow = Set(Shadow),
        Image = Set(Image),
        FallbackColor = Set(Red),
    });
    private static ImageAssessment Available(ResourceReference? reference = null, long encoded = 1000,
        int width = 10, int height = 10, long decoded = 400) => new(reference ?? Image.Reference!, ImageAvailability.Available, encoded, width, height, decoded);

    [Fact]
    public void HighContrastOverridesEveryDecorativePropertyAfterSpaceStateOverrides()
    {
        var config = Decorated().WithSpace(A, new(null, new() { Opacity = Set(0d), Tint = Set(White) }));
        var highContrast = new ReadablePalette(White, Black);
        var result = Resolve(config, environment: new(true, highContrastPalette: highContrast), assessment: Available());
        Assert.Equal(0, result.Inherited.Opacity);
        Assert.Equal(SurfaceMaterial.Solid, result.ActualMaterial);
        Assert.Equal(1, result.Effective.Opacity);
        Assert.Equal(White, result.Effective.Tint);
        Assert.Equal(White, result.Effective.FallbackColor);
        Assert.Equal(0, result.Effective.CornerRadius);
        Assert.Equal(ShadowSpec.None, result.Effective.Shadow);
        Assert.Equal(ImageBackground.None, result.Effective.Image);
        Assert.Same(highContrast, result.ContentPalette);
        Assert.False(result.RequiresOpaqueContentBacking);
        Assert.Equal(AppearanceReason.HighContrast, result.Reasons);
    }

    [Fact]
    public void ReducedTransparencyUsesOpaqueSolidFallbackAndRemovesImages()
    {
        var result = Resolve(Decorated(), environment: new(reducedTransparency: true), assessment: Available());
        Assert.Equal(SurfaceMaterial.Solid, result.ActualMaterial);
        Assert.Equal(1, result.Effective.Opacity);
        Assert.Equal(Red, result.Effective.Tint);
        Assert.Equal(ImageBackground.None, result.Effective.Image);
        Assert.True(result.Reasons.HasFlag(AppearanceReason.ReducedTransparency));
        Assert.Equal(0, result.Inherited.Opacity);
    }

    [Fact]
    public void ReducedMotionIsOrthogonalAndDoesNotChangeTheChosenAppearanceState()
    {
        var config = Config(new() { CornerRadius = Set(12d) });
        var normal = Resolve(config, AppearanceState.Focused);
        var reduced = Resolve(config, AppearanceState.Focused, environment: new(reducedMotion: true));
        Assert.Equal(normal.Effective, reduced.Effective);
        Assert.Equal(AppearanceState.Focused, reduced.State);
        Assert.True(normal.AllowMotion);
        Assert.False(reduced.AllowMotion);
        Assert.Equal(AppearanceReason.ReducedMotion, reduced.Reasons);
    }

    [Theory]
    [InlineData(false, true, AppearanceReason.PlatformMaterialUnsupported)]
    [InlineData(true, false, AppearanceReason.MaterialBudgetExceeded)]
    [InlineData(false, false, AppearanceReason.PlatformMaterialUnsupported | AppearanceReason.MaterialBudgetExceeded)]
    public void UnsupportedOrOverBudgetGlassUsesInjectedSolidFallback(bool supported, bool allowed, AppearanceReason expected)
    {
        var config = Config(new() { Material = Set(SurfaceMaterial.SimulatedGlass), Opacity = Set(0.1), FallbackColor = Set(Red) });
        var result = Resolve(config, capabilities: new(supported, true, true),
            budget: new(allowed, true, true, 1000, 1000, 1000, 100, 400));
        Assert.Equal(SurfaceMaterial.Solid, result.ActualMaterial);
        Assert.Equal(Red, result.Effective.Tint);
        Assert.Equal(1, result.Effective.Opacity);
        Assert.Equal(expected, result.Reasons);
        Assert.Equal(SurfaceMaterial.SimulatedGlass, result.Inherited.Material);
    }

    [Theory]
    [InlineData(0, 0.25, true)]
    [InlineData(0.249999999, 0.25, true)]
    [InlineData(0.25, 0.25, false)]
    [InlineData(1, 1, false)]
    public void OpacityFloorCannotBeBypassedBySpaceOverride(double requested, double effective, bool clamped)
    {
        var config = Config().WithSpace(A, new(null, new() { Opacity = Set(requested) }));
        var result = Resolve(config);
        Assert.Equal(requested, result.Inherited.Opacity);
        Assert.Equal(effective, result.Effective.Opacity);
        Assert.Equal(clamped, result.Reasons.HasFlag(AppearanceReason.OpacitySafetyFloor));
        Assert.Equal(effective < 1, result.RequiresOpaqueContentBacking);
    }

    [Fact]
    public void LowContrastTintRequiresIndependentOpaqueContentAndCannotHideForeground()
    {
        var result = Resolve(Config(new() { Tint = Set(White), Opacity = Set(1d) }));
        Assert.True(result.RequiresOpaqueContentBacking);
        Assert.Equal(AppearanceReason.ContentContrastProtection, result.Reasons);
        Assert.Same(Palette, result.ContentPalette);
        Assert.True(result.ContentPalette.Background.ContrastRatio(result.ContentPalette.Foreground) >= ReadablePalette.MinimumContrastRatio);
        Assert.Equal(White, result.Effective.Tint); // Decorative layer only.
    }

    [Theory]
    [InlineData(false, true, 10, AppearanceReason.PlatformShadowUnsupported)]
    [InlineData(true, false, 10, AppearanceReason.ShadowBudgetExceeded)]
    [InlineData(true, true, 9, AppearanceReason.ShadowBudgetExceeded)]
    public void ShadowsRespectPlatformAndBudget(bool supported, bool allowed, double blurLimit, AppearanceReason reason)
    {
        var result = Resolve(Config(new() { Shadow = Set(Shadow), CornerRadius = Set(20d) }),
            capabilities: new(true, true, supported), budget: new(true, true, allowed, 10, blurLimit, 1000, 100, 400));
        Assert.Equal(ShadowSpec.None, result.Effective.Shadow);
        Assert.Equal(10, result.Effective.CornerRadius);
        Assert.Equal(reason | AppearanceReason.CornerRadiusBudgetExceeded, result.Reasons);
    }

    [Theory]
    [InlineData(ImageAvailability.Unverified, AppearanceReason.ImageUnverified)]
    [InlineData(ImageAvailability.Missing, AppearanceReason.ImageMissing)]
    [InlineData(ImageAvailability.Invalid, AppearanceReason.ImageInvalid)]
    [InlineData(ImageAvailability.Unsupported, AppearanceReason.ImageUnsupported)]
    public void UnusableImageFallsBackWithoutChangingSavedReference(ImageAvailability status, AppearanceReason reason)
    {
        var result = Resolve(Decorated(), assessment: new(Image.Reference!, status));
        Assert.Equal(ImageBackground.None, result.Effective.Image);
        Assert.Equal(SurfaceMaterial.Solid, result.ActualMaterial);
        Assert.Equal(1, result.Effective.Opacity);
        Assert.Equal(Red, result.Effective.Tint);
        Assert.Equal(reason, result.Reasons);
        Assert.Same(Image, result.Inherited.Image);
    }

    [Fact]
    public void MissingObservationAndWrongResourceObservationFailClosed()
    {
        Assert.True(Resolve(Decorated()).Reasons.HasFlag(AppearanceReason.ImageUnverified));
        var wrong = new ExternalResourceReference(new WindowsPath(@"C:\Fixtures\other.png"));
        var mismatch = Resolve(Decorated(), assessment: Available(wrong));
        Assert.Equal(AppearanceReason.ImageAssessmentMismatch, mismatch.Reasons);
        Assert.Equal(ImageBackground.None, mismatch.Effective.Image);
    }

    [Theory]
    [InlineData(1000, 10, 10, 400, AppearanceReason.None)]
    [InlineData(1001, 10, 10, 400, AppearanceReason.ImageBudgetExceeded)]
    [InlineData(1000, 11, 10, 440, AppearanceReason.ImageBudgetExceeded)]
    [InlineData(1000, 10, 10, 401, AppearanceReason.ImageBudgetExceeded)]
    [InlineData(1000, 10, 10, 399, AppearanceReason.ImageInvalid)]
    [InlineData(long.MaxValue, int.MaxValue, int.MaxValue, long.MaxValue, AppearanceReason.ImageBudgetExceeded | AppearanceReason.ImageInvalid)]
    public void ImageResourceBoundariesAndHostileDimensionsAreSafe(long encoded, int width, int height, long decoded, AppearanceReason reason)
    {
        var result = Resolve(Config(new() { Image = Set(Image) }), assessment: Available(encoded: encoded, width: width, height: height, decoded: decoded));
        Assert.Equal(reason, result.Reasons & ~AppearanceReason.ContentContrastProtection);
        Assert.Equal(reason == AppearanceReason.None ? Image : ImageBackground.None, result.Effective.Image);
        if (reason == AppearanceReason.None) { Assert.True(result.RequiresOpaqueContentBacking); }
    }

    [Theory]
    [InlineData(false, true, AppearanceReason.PlatformImageUnsupported)]
    [InlineData(true, false, AppearanceReason.ImageBudgetExceeded)]
    public void ImagesCannotBypassCapabilityOrPerformanceRestrictions(bool supported, bool allowed, AppearanceReason expected)
    {
        var result = Resolve(Decorated(), capabilities: new(true, supported, true),
            budget: new(true, allowed, true, 1000, 1000, 1000, 100, 400), assessment: Available());
        Assert.Equal(expected, result.Reasons);
        Assert.Equal(ImageBackground.None, result.Effective.Image);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothResourceOwnershipKindsUseReferencesOnly(bool external)
    {
        var reference = external ? (ResourceReference)new ExternalResourceReference(new WindowsPath(@"C:\Fixtures\image.png")) : Image.Reference!;
        var background = ImageBackground.FromReference(reference);
        var result = Resolve(Config(new() { Image = Set(background) }), assessment: Available(reference));
        Assert.Same(reference, result.Effective.Image.Reference);
        Assert.Equal(SurfaceMaterial.Solid, result.ActualMaterial);
        Assert.True(result.RequiresOpaqueContentBacking);
    }

    [Fact]
    public void CombinedRestrictionsAccumulateSafeCodesAndRestoringEnvironmentRecoversUserValues()
    {
        var config = Decorated();
        var result = Resolve(config, environment: new(true, true, true, Palette), capabilities: new(false, false, false),
            budget: new(false, false, false, 0, 0, 0, 0, 0), assessment: Available());
        Assert.Equal(AppearanceReason.HighContrast | AppearanceReason.ReducedTransparency | AppearanceReason.ReducedMotion |
            AppearanceReason.PlatformMaterialUnsupported | AppearanceReason.MaterialBudgetExceeded |
            AppearanceReason.PlatformShadowUnsupported | AppearanceReason.ShadowBudgetExceeded |
            AppearanceReason.CornerRadiusBudgetExceeded | AppearanceReason.PlatformImageUnsupported | AppearanceReason.ImageBudgetExceeded, result.Reasons);
        Assert.Equal(AppearanceValues.BuiltInSafe(Palette), result.Effective);
        var recovered = Resolve(config, assessment: Available());
        Assert.Equal(Image, recovered.Effective.Image);
        Assert.Equal(Shadow, recovered.Effective.Shadow);
        Assert.Equal(SurfaceMaterial.SimulatedGlass, recovered.ActualMaterial);
        Assert.Equal(0, recovered.Inherited.Opacity);
        Assert.True(recovered.AllowMotion);
    }
    [Fact]
    public void ZeroOpacityShadowNeverRequestsHiddenShadowWork()
    {
        var requested = new ShadowSpec(0, 1000, -1000, 1000);
        var result = Resolve(Config(new() { Shadow = Set(requested) }), capabilities: new(true, true, false),
            budget: new(true, true, false, 1000, 0, 1000, 100, 400));
        Assert.Same(requested, result.Inherited.Shadow);
        Assert.Equal(ShadowSpec.None, result.Effective.Shadow);
        Assert.Equal(AppearanceReason.None, result.Reasons);
    }

    [Fact]
    public void All512EnvironmentCapabilityBudgetCombinationsPreserveFinalSafety()
    {
        var config = Decorated();
        for (int environmentBits = 0; environmentBits < 8; environmentBits++)
        {
            bool highContrast = (environmentBits & 1) != 0;
            bool transparency = (environmentBits & 2) != 0;
            bool motion = (environmentBits & 4) != 0;
            var environment = new AppearanceEnvironment(highContrast, transparency, motion, Palette);
            for (int capabilityBits = 0; capabilityBits < 8; capabilityBits++)
            {
                var capabilities = new AppearanceCapabilities((capabilityBits & 1) != 0, (capabilityBits & 2) != 0, (capabilityBits & 4) != 0);
                for (int budgetBits = 0; budgetBits < 8; budgetBits++)
                {
                    var budget = new AppearanceBudget((budgetBits & 1) != 0, (budgetBits & 2) != 0, (budgetBits & 4) != 0, 10, 10, 1000, 100, 400);
                    var result = Resolve(config, environment: environment, capabilities: capabilities, budget: budget, assessment: Available());
                    Assert.Equal(!motion, result.AllowMotion);
                    Assert.InRange(result.Effective.Opacity, Safety.MinimumSurfaceOpacity, 1);
                    Assert.InRange(result.Effective.CornerRadius, 0, 10);
                    if (highContrast || transparency || !capabilities.SupportsSimulatedGlass || !budget.AllowSimulatedGlass)
                    {
                        Assert.Equal(SurfaceMaterial.Solid, result.ActualMaterial);
                        Assert.Equal(1, result.Effective.Opacity);
                    }
                    if (highContrast || transparency || !capabilities.SupportsImages || !budget.AllowImages)
                    {
                        Assert.Equal(ImageBackground.None, result.Effective.Image);
                    }
                    if (highContrast || !capabilities.SupportsShadows || !budget.AllowShadows)
                    {
                        Assert.Equal(ShadowSpec.None, result.Effective.Shadow);
                    }
                    if (highContrast) { Assert.Equal(Palette.Background, result.Effective.Tint); }
                    Assert.Equal(0, result.Inherited.Opacity);
                    Assert.True(result.ContentPalette.Background.ContrastRatio(result.ContentPalette.Foreground) >= ReadablePalette.MinimumContrastRatio);
                    Assert.Equal(result.Effective, Resolve(config, environment: environment, capabilities: capabilities, budget: budget, assessment: Available()).Effective);
                }
            }
        }
    }

}
