using System.Windows;
using Zhuomian.Core.Appearance;
using Zhuomian.Core.Interaction;
using Zhuomian.Core.Layout;
using Zhuomian.Platform.Windows;
using Zhuomian.Ui.WpfCandidate;

namespace Zhuomian.Ui.Tests;

public sealed class CandidateTests
{
    [Fact]
    public void RealHiddenHwndCreationMotionInputRollbackAndRelease()
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { CandidateSmoke.Run(_ => { }); } catch (Exception ex) { failure = ex; } });
        thread.IsBackground = true; thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "STA smoke timed out");
        Assert.Null(failure);
    }
    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void SharedGeometryMapsToPixelsAndInputWithSingleIdentity(double scale)
    {
        var input = new SpaceDispatcher(new CandidateSmoke.Clock(), new());
        var scene = CandidateScene.Create(input, new(reducedTransparency: true), scale);
        Assert.Equal(2, scene.Spaces.Length);
        Assert.Equal(2, scene.Spaces.Select(space => space.Id).Distinct().Count());
        foreach (var space in scene.Spaces)
        {
            Assert.Equal(Math.Round(space.Bounds.Left * scale), space.Bounds.Left * scale, 6);
            Assert.Equal(Math.Round(space.Bounds.Right * scale), space.Bounds.Right * scale, 6);
            Assert.Null(scene.Hit(space.Bounds.Left, space.Bounds.Top));
            Assert.Equal(space.Id, scene.Hit(space.Bounds.X + 20, space.Bounds.Y + 20)?.SpaceId);
            Assert.NotEmpty(space.Cells);
            foreach (var cell in space.Cells)
            { Assert.Equal(EngineeringFixture.ItemId.ToString("D"), scene.Hit(cell.X + 20, cell.Y + 20)?.ItemId); }
            Assert.True(space.Bounds.Contains(space.Exit));
        }
        Assert.Null(scene.Hit(0, 0));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AccessibilityPolicyIsFinalAndDoesNotCreateExpensiveMaterials(bool highContrast, bool reducedMotion)
    {
        var palette = new ReadablePalette(new(0, 0, 0), new(255, 255, 255));
        var environment = new AppearanceEnvironment(highContrast, true, reducedMotion, highContrast ? palette : null);
        var result = CandidateScene.Resolve(EngineeringFixture.ClickId, true, environment);
        Assert.Equal(SurfaceMaterial.Solid, result.ActualMaterial); Assert.Equal(1, result.Effective.Opacity);
        Assert.Equal(ShadowSpec.None, result.Effective.Shadow); Assert.Null(result.Effective.Image.Reference);
        Assert.Equal(!highContrast && !reducedMotion, result.AllowMotion);
        if (highContrast) { Assert.Equal(palette, result.ContentPalette); }
        Assert.True(result.ContentPalette.Background.ContrastRatio(result.ContentPalette.Foreground) >= 4.5);
    }
    [Fact]
    public void CandidateUsesExistingControllerAndMotionWithoutKeyboardCapture()
    {
        var clock = new CandidateSmoke.Clock(); var input = new SpaceDispatcher(clock, new());
        input.SetAvailability(DesktopAvailability.DesktopAvailable, "synthetic-shell");
        string b = EngineeringFixture.ClickId.ToString("D");
        input.DispatchPointer(PointerAction.Down, new(b)); input.DispatchPointer(PointerAction.Up, new(b));
        var monitor = new MonitorWorkArea("test", new(0, 0, 1200, 800), new(1));
        input.RenderTargets(monitor, true); clock.NowMilliseconds = 100; input.RenderTargets(monitor, true);
        var middle = CandidateScene.Create(input, new(), 1);
        var focused = middle.Spaces.Single(space => space.Id == b);
        Assert.True(focused.Expanded); Assert.Equal(b, middle.Spaces[^1].Id);
        Assert.True(focused.Bounds.Width > 300);
        Assert.True(middle.Hit(focused.Return.X + 10, focused.Return.Y + 10)?.Close);
        input.HostLost(); Assert.False(input.HasActiveAnimation);
        Assert.Equal(KeyboardMode.NoKeyboardCapture, input.Controller.KeyboardMode);
    }
}
