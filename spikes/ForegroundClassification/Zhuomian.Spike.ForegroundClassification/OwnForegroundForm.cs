namespace Zhuomian.Spike.ForegroundClassification;

internal sealed class OwnForegroundForm : Form
{
    public OwnForegroundForm(Rectangle bounds)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(51, 57, 92);
        Bounds = bounds;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Zhuomian Foreground Classification Probe";
        TopMost = false;
    }

    protected override bool ShowWithoutActivation => true;
}
