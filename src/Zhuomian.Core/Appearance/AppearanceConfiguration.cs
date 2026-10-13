using System.Collections.Immutable;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Appearance;

/// <summary>Default/Inherit/Reset all clear this layer. Set(0) and Set(None) remain explicit values.</summary>
public readonly record struct PropertyOverride<T>
{
    private readonly T? _value;
    internal PropertyOverride(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
        IsSet = true;
    }
    public bool IsSet { get; }
    public T Value => IsSet ? _value! : throw new InvalidOperationException("Property is inherited.");
    public T Resolve(T inherited) => IsSet ? Value : inherited;
}

/// <summary>Factories keep unset/reset separate from explicit values without generic static members.</summary>
public static class PropertyOverride
{
    public static PropertyOverride<T> Inherit<T>() => default;
    public static PropertyOverride<T> Reset<T>() => default;
    public static PropertyOverride<T> Set<T>(T value) => new(value);
}

/// <summary>Each validated init property can be independently replaced or cleared using a record copy.</summary>
public sealed record AppearanceOverrides
{
    private PropertyOverride<SurfaceMaterial> _material;
    private PropertyOverride<RgbColor> _tint;
    private PropertyOverride<double> _opacity;
    private PropertyOverride<double> _cornerRadius;
    private PropertyOverride<ShadowSpec> _shadow;
    private PropertyOverride<ImageBackground> _image;
    private PropertyOverride<RgbColor> _fallbackColor;
    public PropertyOverride<SurfaceMaterial> Material
    {
        get => _material;
        init { if (value.IsSet) { AppearanceGuard.Defined(value.Value); } _material = value; }
    }
    public PropertyOverride<RgbColor> Tint { get => _tint; init => _tint = value; }
    public PropertyOverride<double> Opacity
    {
        get => _opacity;
        init { if (value.IsSet) { AppearanceGuard.Number(value.Value, nameof(Opacity), 0, 1); } _opacity = value; }
    }
    public PropertyOverride<double> CornerRadius
    {
        get => _cornerRadius;
        init { if (value.IsSet) { AppearanceGuard.Number(value.Value, nameof(CornerRadius)); } _cornerRadius = value; }
    }
    public PropertyOverride<ShadowSpec> Shadow { get => _shadow; init => _shadow = value; }
    public PropertyOverride<ImageBackground> Image { get => _image; init => _image = value; }
    public PropertyOverride<RgbColor> FallbackColor { get => _fallbackColor; init => _fallbackColor = value; }
    public static AppearanceOverrides Empty { get; } = new();
    internal AppearanceValues Apply(AppearanceValues inherited) => new(
        Material.Resolve(inherited.Material), Tint.Resolve(inherited.Tint), Opacity.Resolve(inherited.Opacity),
        CornerRadius.Resolve(inherited.CornerRadius), Shadow.Resolve(inherited.Shadow), Image.Resolve(inherited.Image),
        FallbackColor.Resolve(inherited.FallbackColor));
}

/// <summary>Common and visual-state layers. ActivationMode A/B is deliberately absent.</summary>
public sealed record AppearanceLayers
{
    public AppearanceLayers(AppearanceOverrides? common = null, AppearanceOverrides? idle = null, AppearanceOverrides? focused = null)
    {
        Common = common ?? AppearanceOverrides.Empty;
        Idle = idle ?? AppearanceOverrides.Empty;
        Focused = focused ?? AppearanceOverrides.Empty;
    }
    public AppearanceOverrides Common { get; }
    public AppearanceOverrides Idle { get; }
    public AppearanceOverrides Focused { get; }
    public AppearanceOverrides ForState(AppearanceState state) => AppearanceGuard.Defined(state) == AppearanceState.Idle ? Idle : Focused;
}

/// <summary>Independent typed candidate; not attached to Workspace, serialized or a frozen persistence schema.</summary>
public sealed class AppearanceConfiguration
{
    public AppearanceConfiguration(AppearanceLayers globalTheme, IEnumerable<KeyValuePair<Guid, AppearanceLayers>>? spaces = null)
    {
        ArgumentNullException.ThrowIfNull(globalTheme);
        GlobalTheme = globalTheme;
        var builder = ImmutableDictionary.CreateBuilder<Guid, AppearanceLayers>();
        foreach (var (id, layers) in spaces ?? [])
        {
            DomainValidation.Id(id);
            ArgumentNullException.ThrowIfNull(layers);
            if (!builder.TryAdd(id, layers)) { throw new ArgumentException("Duplicate Space identity.", nameof(spaces)); }
        }
        Spaces = builder.ToImmutable();
    }
    private AppearanceConfiguration(AppearanceLayers globalTheme, ImmutableDictionary<Guid, AppearanceLayers> spaces)
    {
        GlobalTheme = globalTheme;
        Spaces = spaces;
    }
    public AppearanceLayers GlobalTheme { get; }
    public ImmutableDictionary<Guid, AppearanceLayers> Spaces { get; }
    public AppearanceConfiguration WithGlobalTheme(AppearanceLayers globalTheme)
    {
        ArgumentNullException.ThrowIfNull(globalTheme);
        return new(globalTheme, Spaces);
    }
    public AppearanceConfiguration WithSpace(Guid spaceId, AppearanceLayers layers)
    {
        DomainValidation.Id(spaceId);
        ArgumentNullException.ThrowIfNull(layers);
        return new(GlobalTheme, Spaces.SetItem(spaceId, layers));
    }
    public AppearanceConfiguration ResetSpace(Guid spaceId) => new(GlobalTheme, Spaces.Remove(DomainValidation.Id(spaceId)));
}
