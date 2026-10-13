using Zhuomian.Core.Appearance;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Tests.Appearance;

internal static class AppearanceFixtures
{
    internal static readonly Guid A = new("10000000-0000-0000-0000-000000000001");
    internal static readonly Guid B = new("10000000-0000-0000-0000-000000000002");
    internal static readonly RgbColor Black = new(0, 0, 0);
    internal static readonly RgbColor White = new(255, 255, 255);
    internal static readonly RgbColor Red = new(120, 0, 0);
    internal static readonly ReadablePalette Palette = new(Black, White);
    internal static readonly AppearanceSafetyPolicy Safety = new(Palette, 0.25);
    internal static readonly AppearanceCapabilities Capabilities = new(true, true, true);
    internal static readonly AppearanceBudget Budget = new(true, true, true, 1000, 1000, 1000, 100, 400);
    internal static readonly ImageBackground Image = ImageBackground.FromReference(new ManagedAssetReference(new("20000000-0000-0000-0000-000000000001")));
    internal static readonly ShadowSpec Shadow = new(0.5, 10, -2, 2);
    internal static PropertyOverride<T> Set<T>(T value) => PropertyOverride.Set(value);
    internal static AppearanceResolution Resolve(AppearanceConfiguration configuration, AppearanceState state = AppearanceState.Idle,
        Guid? spaceId = null, AppearanceEnvironment? environment = null, AppearanceCapabilities? capabilities = null,
        AppearanceBudget? budget = null, ImageAssessment? assessment = null) => AppearanceResolver.Resolve(
            spaceId ?? A, state, configuration, Safety, environment ?? new(), capabilities ?? Capabilities, budget ?? Budget, assessment);
    internal static AppearanceConfiguration Config(AppearanceOverrides? common = null) => new(new(common));
}
