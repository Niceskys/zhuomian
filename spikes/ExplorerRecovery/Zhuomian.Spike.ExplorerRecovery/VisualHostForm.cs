namespace Zhuomian.Spike.ExplorerRecovery;

internal sealed class VisualHostForm : Form
{
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    public VisualHostForm(Rectangle workArea, int generation)
    {
        var width = Math.Min(480, workArea.Width);
        var height = Math.Min(240, workArea.Height);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(29, 58, 51);
        Bounds = new Rectangle(workArea.Left + 40, workArea.Top + 40, width, height);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = $"Zhuomian Explorer Recovery Host {generation}";
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
