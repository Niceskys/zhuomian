namespace Zhuomian.Spike.FocusAndInput;

internal sealed class VisualSurfaceForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    public VisualSurfaceForm(Rectangle bounds)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(32, 50, 78);
        Bounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Zhuomian Visual NoActivate Probe";
        TopMost = false;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmMouseActivate)
        {
            message.Result = (nint)MaNoActivate;
            return;
        }

        base.WndProc(ref message);
    }
}
