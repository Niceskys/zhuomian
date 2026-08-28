namespace Zhuomian.Spike.FallbackVisualUsability;

internal sealed class OrdinaryWindowForm : Form
{
    internal static readonly Color SurfaceColor = Color.FromArgb(116, 72, 184);

    internal OrdinaryWindowForm(Rectangle bounds)
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = SurfaceColor;
        Bounds = bounds;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Ordinary application coverage probe";
        TopMost = false;
    }
}
