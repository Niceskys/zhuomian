namespace Zhuomian.Spike.PlatformLifecycle;

internal sealed class FullscreenExternalForm : Form
{
    internal FullscreenExternalForm(Rectangle bounds)
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(12, 18, 30);
        Bounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Zhuomian external fullscreen lifecycle probe";
        TopMost = false;
    }
}
