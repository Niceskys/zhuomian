using Zhuomian.Core.Appearance;
using static Zhuomian.Core.Tests.Appearance.AppearanceFixtures;

namespace Zhuomian.Core.Tests.Appearance;

public sealed class AppearanceInheritanceTests
{
    private static AppearanceOverrides Layer(int number) => new()
    {
        Material = Set(number % 2 == 0 ? SurfaceMaterial.Solid : SurfaceMaterial.SimulatedGlass),
        Tint = Set(new RgbColor(number, 2 * number, 3 * number)),
        Opacity = Set(number / 10d),
        CornerRadius = Set((double)number),
        Shadow = Set(new ShadowSpec(number / 10d, number, -number, number)),
        Image = Set(number % 2 == 0 ? ImageBackground.None : Image),
        FallbackColor = Set(new RgbColor(3 * number, 2 * number, number)),
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AllPropertiesFollowExactlyFiveLayers(int highest)
    {
        var config = new AppearanceConfiguration(new(highest >= 1 ? Layer(1) : null, highest >= 2 ? Layer(2) : null))
            .WithSpace(A, new(highest >= 3 ? Layer(3) : null, highest >= 4 ? Layer(4) : null));
        var values = Resolve(config).Inherited;
        if (highest == 0)
        {
            Assert.Equal(AppearanceValues.BuiltInSafe(Palette), values);
            return;
        }
        Assert.Equal(highest % 2 == 0 ? SurfaceMaterial.Solid : SurfaceMaterial.SimulatedGlass, values.Material);
        Assert.Equal(new RgbColor(highest, highest * 2, highest * 3), values.Tint);
        Assert.Equal(highest / 10d, values.Opacity);
        Assert.Equal(highest, values.CornerRadius);
        Assert.Equal(new ShadowSpec(highest / 10d, highest, -highest, highest), values.Shadow);
        Assert.Equal(highest % 2 == 0 ? ImageBackground.None : Image, values.Image);
        Assert.Equal(new RgbColor(3 * highest, 2 * highest, highest), values.FallbackColor);
    }

    [Fact]
    public void MixedPropertiesInheritIndependentlyAndSpaceCommonWinsOverGlobalState()
    {
        var config = new AppearanceConfiguration(new(
            new() { Tint = Set(Red), Opacity = Set(0.8), Material = Set(SurfaceMaterial.SimulatedGlass) },
            new() { Opacity = Set(0.7), CornerRadius = Set(10d), FallbackColor = Set(Red) }))
            .WithSpace(A, new(new() { Opacity = Set(0.6), Shadow = Set(Shadow) },
                new() { CornerRadius = Set(20d), Image = Set(Image) }));
        var result = Resolve(config).Inherited;
        Assert.Equal(Red, result.Tint);
        Assert.Equal(0.6, result.Opacity);
        Assert.Equal(20, result.CornerRadius);
        Assert.Equal(Shadow, result.Shadow);
        Assert.Equal(Image, result.Image);
        Assert.Equal(Red, result.FallbackColor);
        Assert.Equal(SurfaceMaterial.SimulatedGlass, result.Material);
    }

    [Theory]
    [InlineData(AppearanceState.Idle, 0.6, 10)]
    [InlineData(AppearanceState.Focused, 0.4, 20)]
    public void IdleAndFocusedSelectTheirOwnGlobalAndSpaceState(AppearanceState state, double opacity, double radius)
    {
        var config = new AppearanceConfiguration(new(null,
            new() { CornerRadius = Set(10d) }, new() { CornerRadius = Set(20d) }))
            .WithSpace(A, new(new() { Tint = Set(Red) },
                new() { Opacity = Set(0.6) }, new() { Opacity = Set(0.4) }));
        var result = Resolve(config, state);
        Assert.Equal(state, result.State);
        Assert.Equal(opacity, result.Effective.Opacity);
        Assert.Equal(radius, result.Effective.CornerRadius);
        Assert.Equal(Red, result.Inherited.Tint);
    }

    [Fact]
    public void ExplicitZeroNoneAndSolidDifferFromResetForEveryProperty()
    {
        var theme = new AppearanceLayers(Layer(1));
        var explicitValues = new AppearanceOverrides
        {
            Material = Set(SurfaceMaterial.Solid), Tint = Set(Black), Opacity = Set(0d), CornerRadius = Set(0d),
            Shadow = Set(ShadowSpec.None), Image = Set(ImageBackground.None), FallbackColor = Set(Black),
        };
        var config = new AppearanceConfiguration(theme).WithSpace(A, new(explicitValues));
        var zero = Resolve(config);
        Assert.Equal(0, zero.Inherited.Opacity);
        Assert.Equal(0.25, zero.Effective.Opacity);
        Assert.Equal(AppearanceReason.OpacitySafetyFloor | AppearanceReason.ContentContrastProtection, zero.Reasons);
        Assert.Equal(0, zero.Inherited.CornerRadius);
        Assert.Equal(SurfaceMaterial.Solid, zero.Inherited.Material);
        Assert.Equal(Black, zero.Inherited.Tint);
        Assert.Equal(Black, zero.Inherited.FallbackColor);
        Assert.Equal(ShadowSpec.None, zero.Inherited.Shadow);
        Assert.Equal(ImageBackground.None, zero.Inherited.Image);
        var reset = explicitValues with
        {
            Material = PropertyOverride.Reset<SurfaceMaterial>(), Tint = PropertyOverride.Reset<RgbColor>(),
            Opacity = PropertyOverride.Reset<double>(), CornerRadius = PropertyOverride.Reset<double>(),
            Shadow = PropertyOverride.Reset<ShadowSpec>(), Image = PropertyOverride.Reset<ImageBackground>(),
            FallbackColor = PropertyOverride.Reset<RgbColor>(),
        };
        var cleared = config.WithSpace(A, new(reset));
        Assert.Equal(Resolve(new(theme)).Inherited, Resolve(cleared).Inherited);
        Assert.Equal(0, Resolve(config).Inherited.Opacity); // Original snapshot is unchanged.
        Assert.Equal(PropertyOverride.Inherit<double>(), PropertyOverride.Reset<double>());
        Assert.Throws<InvalidOperationException>(() => PropertyOverride.Inherit<double>().Value);
    }

    [Fact]
    public void ResetStateReturnsCommonAndResetCommonReturnsGlobalStateImmediately()
    {
        var config = new AppearanceConfiguration(new(null, new() { Opacity = Set(0.8) }))
            .WithSpace(A, new(new() { Opacity = Set(0.6) }, new() { Opacity = Set(0.4) }));
        Assert.Equal(0.4, Resolve(config).Inherited.Opacity);
        var noState = config.WithSpace(A, new(config.Spaces[A].Common, config.Spaces[A].Idle with { Opacity = PropertyOverride.Reset<double>() }));
        Assert.Equal(0.6, Resolve(noState).Inherited.Opacity);
        var noCommon = noState.WithSpace(A, new(noState.Spaces[A].Common with { Opacity = PropertyOverride.Reset<double>() }, noState.Spaces[A].Idle));
        Assert.Equal(0.8, Resolve(noCommon).Inherited.Opacity);
    }

    [Fact]
    public void MultipleSpacesAndThemeReplacementAreIsolatedSnapshots()
    {
        var source = new Dictionary<Guid, AppearanceLayers> { [A] = new(new() { Opacity = Set(0.5) }) };
        var config = new AppearanceConfiguration(new(new() { Tint = Set(Red) }), source);
        source.Clear();
        var changed = config.WithSpace(A, new(new() { Opacity = Set(0.8) })).WithSpace(B, new(new() { CornerRadius = Set(40d) }));
        Assert.Equal(0.5, Resolve(config).Inherited.Opacity);
        Assert.Equal(0.8, Resolve(changed).Inherited.Opacity);
        Assert.Equal(1, Resolve(changed, spaceId: B).Inherited.Opacity);
        Assert.Equal(0, Resolve(changed).Inherited.CornerRadius);
        Assert.Equal(40, Resolve(changed, spaceId: B).Inherited.CornerRadius);
        var themeChanged = changed.WithGlobalTheme(new(new() { Tint = Set(White) }));
        Assert.Equal(Red, Resolve(changed).Inherited.Tint);
        Assert.Equal(White, Resolve(themeChanged).Inherited.Tint);
        Assert.Equal(0.8, Resolve(themeChanged).Inherited.Opacity);
        Assert.Equal(1, Resolve(changed.ResetSpace(A)).Inherited.Opacity);
        Assert.Equal(2, changed.Spaces.Count);
    }

    [Theory]
    [InlineData(17)]
    [InlineData(731)]
    [InlineData(20261013)]
    public void ReproducibleMixedOverridesAndStateChangesMatchIndependentReference(int seed)
    {
        var random = new Random(seed);
        for (int i = 0; i < 500; i++)
        {
            var layers = Enumerable.Range(0, 6).Select(_ => new AppearanceOverrides
            {
                Opacity = random.Next(2) == 0 ? PropertyOverride.Inherit<double>() : Set(random.Next(101) / 100d),
                CornerRadius = random.Next(2) == 0 ? PropertyOverride.Inherit<double>() : Set(random.Next(1001) / 10d),
            }).ToArray();
            var config = new AppearanceConfiguration(new(layers[0], layers[1], layers[2])).WithSpace(A, new(layers[3], layers[4], layers[5]));
            foreach (var state in new[] { AppearanceState.Idle, AppearanceState.Focused, AppearanceState.Idle })
            {
                double opacity = 1, radius = 0;
                foreach (int index in new[] { 0, state == AppearanceState.Idle ? 1 : 2, 3, state == AppearanceState.Idle ? 4 : 5 })
                {
                    if (layers[index].Opacity.IsSet) { opacity = layers[index].Opacity.Value; }
                    if (layers[index].CornerRadius.IsSet) { radius = layers[index].CornerRadius.Value; }
                }
                var result = Resolve(config, state);
                Assert.Equal(opacity, result.Inherited.Opacity);
                Assert.Equal(radius, result.Inherited.CornerRadius);
                Assert.Equal(Math.Max(0.25, opacity), result.Effective.Opacity);
                Assert.Equal(result.Inherited, Resolve(config, state).Inherited);
            }
        }
    }
}
