using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Appearance;

internal static class AppearanceGuard
{
    // Engineering envelope only, not an accepted visual style or product default.
    internal static double Number(double value, string name, double minimum = 0, double maximum = 1000)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum) { throw new ArgumentOutOfRangeException(name); }
        return value;
    }
    internal static T Defined<T>(T value) where T : struct, Enum => Enum.IsDefined(value)
        ? value : throw new ArgumentOutOfRangeException(nameof(value));
}

/// <summary>Opaque sRGB. Alpha is represented exclusively by the separately validated surface opacity.</summary>
public sealed record RgbColor
{
    public RgbColor(int red, int green, int blue)
    {
        Red = (byte)AppearanceGuard.Number(red, nameof(red), 0, 255);
        Green = (byte)AppearanceGuard.Number(green, nameof(green), 0, 255);
        Blue = (byte)AppearanceGuard.Number(blue, nameof(blue), 0, 255);
    }
    public byte Red { get; }
    public byte Green { get; }
    public byte Blue { get; }
    private double Luminance => 0.2126 * Linear(Red) + 0.7152 * Linear(Green) + 0.0722 * Linear(Blue);
    private static double Linear(byte channel)
    {
        double value = channel / 255d;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
    public double ContrastRatio(RgbColor other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return (Math.Max(Luminance, other.Luminance) + 0.05) / (Math.Min(Luminance, other.Luminance) + 0.05);
    }
}

public enum SurfaceMaterial { Solid, SimulatedGlass }
public enum AppearanceState { Idle, Focused }

public sealed record ShadowSpec
{
    public ShadowSpec(double opacity, double blurRadius, double offsetX, double offsetY)
    {
        Opacity = AppearanceGuard.Number(opacity, nameof(opacity), 0, 1);
        BlurRadius = AppearanceGuard.Number(blurRadius, nameof(blurRadius));
        OffsetX = AppearanceGuard.Number(offsetX, nameof(offsetX), -1000);
        OffsetY = AppearanceGuard.Number(offsetY, nameof(offsetY), -1000);
    }
    public double Opacity { get; }
    public double BlurRadius { get; }
    public double OffsetX { get; }
    public double OffsetY { get; }
    public static ShadowSpec None { get; } = new(0, 0, 0, 0);
}

/// <summary>None is an explicit background value, not inheritance. No I/O or decoding authority.</summary>
public sealed record ImageBackground
{
    private ImageBackground(ResourceReference? reference) => Reference = reference;
    public ResourceReference? Reference { get; }
    public static ImageBackground None { get; } = new((ResourceReference?)null);
    public static ImageBackground FromReference(ResourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference is not ExternalResourceReference and not ManagedAssetReference) { throw new ArgumentException("Unsupported resource reference.", nameof(reference)); }
        return new(reference);
    }
}

/// <summary>Complete immutable appearance, including an injected fallback color. Never a render resource.</summary>
public sealed record AppearanceValues
{
    public AppearanceValues(SurfaceMaterial material, RgbColor tint, double opacity, double cornerRadius,
        ShadowSpec shadow, ImageBackground image, RgbColor fallbackColor)
    {
        Material = AppearanceGuard.Defined(material);
        ArgumentNullException.ThrowIfNull(tint);
        ArgumentNullException.ThrowIfNull(shadow);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(fallbackColor);
        Tint = tint;
        Opacity = AppearanceGuard.Number(opacity, nameof(opacity), 0, 1);
        CornerRadius = AppearanceGuard.Number(cornerRadius, nameof(cornerRadius));
        Shadow = shadow;
        Image = image;
        FallbackColor = fallbackColor;
    }
    public SurfaceMaterial Material { get; }
    public RgbColor Tint { get; }
    public double Opacity { get; }
    public double CornerRadius { get; }
    public ShadowSpec Shadow { get; }
    public ImageBackground Image { get; }
    public RgbColor FallbackColor { get; }

    /// <summary>Structural safety defaults; palette is caller-injected, not a validated product color scheme.</summary>
    public static AppearanceValues BuiltInSafe(ReadablePalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return new(SurfaceMaterial.Solid, palette.Background, 1, 0, ShadowSpec.None, ImageBackground.None, palette.Background);
    }
}
