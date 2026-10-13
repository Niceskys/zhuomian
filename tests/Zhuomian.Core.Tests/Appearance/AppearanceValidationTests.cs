using Zhuomian.Core.Appearance;
using static Zhuomian.Core.Tests.Appearance.AppearanceFixtures;

namespace Zhuomian.Core.Tests.Appearance;

public sealed class AppearanceValidationTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void InvalidOpacityRejectsAtCreationAndRecordCopy(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceValues(SurfaceMaterial.Solid, Black, value, 0, ShadowSpec.None, ImageBackground.None, Black));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceOverrides { Opacity = Set(value) });
        var original = new AppearanceOverrides { Opacity = Set(0.5) };
        Assert.Throws<ArgumentOutOfRangeException>(() => original with { Opacity = Set(value) });
        Assert.Equal(0.5, original.Opacity.Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowSpec(value, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceSafetyPolicy(Palette, value));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(1001)]
    public void InvalidDimensionsRejectInValuesOverridesAndBudget(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceOverrides { CornerRadius = Set(value) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceValues(SurfaceMaterial.Solid, Black, 1, value, ShadowSpec.None, ImageBackground.None, Black));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowSpec(1, value, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceBudget(true, true, true, value, 0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceBudget(true, true, true, 0, value, 0, 0, 0));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1001)]
    [InlineData(1001)]
    public void InvalidShadowOffsetsReject(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowSpec(1, 0, value, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowSpec(1, 0, 0, value));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    [InlineData(256, 0, 0)]
    [InlineData(0, 256, 0)]
    [InlineData(0, 0, 256)]
    public void IllegalColorChannelsRejectRatherThanWrap(int r, int g, int b) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RgbColor(r, g, b));

    [Fact]
    public void InvalidEnumsIdentitiesAndDuplicatesReject()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceOverrides { Material = Set((SurfaceMaterial)99) });
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceValues((SurfaceMaterial)99, Black, 1, 0, ShadowSpec.None, ImageBackground.None, Black));
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(Config(), (AppearanceState)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceLayers().ForState((AppearanceState)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageAssessment(Image.Reference!, (ImageAvailability)99));
        Assert.Throws<ArgumentException>(() => Resolve(Config(), spaceId: Guid.Empty));
        Assert.Throws<ArgumentException>(() => Config().WithSpace(Guid.Empty, new()));
        Assert.Throws<ArgumentException>(() => Config().ResetSpace(Guid.Empty));
        Assert.Throws<ArgumentException>(() => new AppearanceConfiguration(new(), new Dictionary<Guid, AppearanceLayers> { [Guid.Empty] = new() }));
        var duplicate = new KeyValuePair<Guid, AppearanceLayers>(A, new());
        Assert.Throws<ArgumentException>(() => new AppearanceConfiguration(new(), new[] { duplicate, duplicate }));
    }

    [Fact]
    public void LowContrastPaletteAndUnsafeSafetyFloorReject()
    {
        Assert.Throws<ArgumentException>(() => new ReadablePalette(White, White));
        Assert.Throws<ArgumentException>(() => new ReadablePalette(new(128, 128, 128), White));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceSafetyPolicy(Palette, 0));
        Assert.Throws<ArgumentNullException>(() => new AppearanceEnvironment(highContrast: true));
        Assert.Equal(21, Black.ContrastRatio(White));
        Assert.Equal(1, White.ContrastRatio(White));
        Assert.Equal(Black.ContrastRatio(Red), Red.ContrastRatio(Black));
    }

    [Theory]
    [InlineData(0, 1, 1, 4)]
    [InlineData(1, 0, 1, 4)]
    [InlineData(1, 1, 0, 4)]
    [InlineData(1, 1, 1, 0)]
    [InlineData(-1, 1, 1, 4)]
    [InlineData(1, -1, 1, 4)]
    [InlineData(1, 1, -1, 4)]
    [InlineData(1, 1, 1, -1)]
    public void AvailableImageRequiresValidPositiveMetadata(long encoded, int width, int height, long decoded) =>
        Assert.ThrowsAny<ArgumentException>(() => new ImageAssessment(Image.Reference!, ImageAvailability.Available, encoded, width, height, decoded));

    [Fact]
    public void NegativeResourceBudgetsReject()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceBudget(true, true, true, 0, 0, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceBudget(true, true, true, 0, 0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppearanceBudget(true, true, true, 0, 0, 0, 0, -1));
    }

    [Fact]
    public void NullInputsRejectWithoutUninitializedExplicitValues()
    {
        Assert.Throws<ArgumentNullException>(() => PropertyOverride.Set<RgbColor>(null!));
        Assert.Throws<ArgumentNullException>(() => ImageBackground.FromReference(null!));
        Assert.Throws<ArgumentNullException>(() => new ImageAssessment(null!, ImageAvailability.Missing));
        Assert.Throws<ArgumentNullException>(() => new ReadablePalette(null!, White));
        Assert.Throws<ArgumentNullException>(() => new ReadablePalette(Black, null!));
        Assert.Throws<ArgumentNullException>(() => Black.ContrastRatio(null!));
        Assert.Throws<ArgumentNullException>(() => new AppearanceSafetyPolicy(null!, 1));
        Assert.Throws<ArgumentNullException>(() => AppearanceValues.BuiltInSafe(null!));
        Assert.Throws<ArgumentNullException>(() => new AppearanceConfiguration(null!));
        Assert.Throws<ArgumentNullException>(() => Config().WithGlobalTheme(null!));
        Assert.Throws<ArgumentNullException>(() => Config().WithSpace(A, null!));
        Assert.Throws<ArgumentNullException>(() => new AppearanceConfiguration(new(), new Dictionary<Guid, AppearanceLayers> { [A] = null! }));
        Assert.Throws<ArgumentNullException>(() => new AppearanceValues(SurfaceMaterial.Solid, null!, 1, 0, ShadowSpec.None, ImageBackground.None, Black));
        Assert.Throws<ArgumentNullException>(() => new AppearanceValues(SurfaceMaterial.Solid, Black, 1, 0, null!, ImageBackground.None, Black));
        Assert.Throws<ArgumentNullException>(() => new AppearanceValues(SurfaceMaterial.Solid, Black, 1, 0, ShadowSpec.None, null!, Black));
        Assert.Throws<ArgumentNullException>(() => new AppearanceValues(SurfaceMaterial.Solid, Black, 1, 0, ShadowSpec.None, ImageBackground.None, null!));
        Assert.Throws<ArgumentNullException>(() => AppearanceResolver.Resolve(A, AppearanceState.Idle, null!, Safety, new(), Capabilities, Budget));
        Assert.Throws<ArgumentNullException>(() => AppearanceResolver.Resolve(A, AppearanceState.Idle, Config(), null!, new(), Capabilities, Budget));
        Assert.Throws<ArgumentNullException>(() => AppearanceResolver.Resolve(A, AppearanceState.Idle, Config(), Safety, null!, Capabilities, Budget));
        Assert.Throws<ArgumentNullException>(() => AppearanceResolver.Resolve(A, AppearanceState.Idle, Config(), Safety, new(), null!, Budget));
        Assert.Throws<ArgumentNullException>(() => AppearanceResolver.Resolve(A, AppearanceState.Idle, Config(), Safety, new(), Capabilities, null!));
    }

    [Fact]
    public void InputAndResultValueObjectsHaveNoMutableCollectionsOrSetters()
    {
        foreach (var type in new[] { typeof(AppearanceValues), typeof(RgbColor), typeof(ShadowSpec), typeof(ImageBackground),
            typeof(AppearanceLayers), typeof(AppearanceConfiguration), typeof(AppearanceSafetyPolicy), typeof(ReadablePalette),
            typeof(AppearanceBudget), typeof(AppearanceEnvironment), typeof(ImageAssessment), typeof(AppearanceResolution) })
        {
            Assert.All(type.GetProperties(), property => Assert.Null(property.SetMethod));
        }
        var empty = AppearanceOverrides.Empty;
        var copy = empty with { Opacity = Set(0d) };
        Assert.False(empty.Opacity.IsSet);
        Assert.True(copy.Opacity.IsSet);
        Assert.Equal(0, copy.Opacity.Value);
        var result = Resolve(Config());
        Assert.Equal(AppearanceValues.BuiltInSafe(Palette), result.Effective);
        Assert.Equal(AppearanceReason.None, result.Reasons);
    }

    [Fact]
    public void AcceptedEngineeringBoundariesRemainFiniteAndDeterministic()
    {
        var config = Config(new()
        {
            CornerRadius = Set(1000d), Shadow = Set(new ShadowSpec(1, 1000, -1000, 1000)), Opacity = Set(1d),
        });
        var result = Resolve(config);
        Assert.Equal(1000, result.Effective.CornerRadius);
        Assert.Equal(1000, result.Effective.Shadow.BlurRadius);
        Assert.Equal(result.Effective, Resolve(config).Effective);
        Assert.Equal(0, new RgbColor(0, 0, 0).Red);
        Assert.Equal(255, new RgbColor(255, 255, 255).Blue);
    }
}
