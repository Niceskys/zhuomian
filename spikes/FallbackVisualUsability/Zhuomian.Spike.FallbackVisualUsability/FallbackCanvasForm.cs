namespace Zhuomian.Spike.FallbackVisualUsability;

internal sealed class FallbackCanvasForm : Form
{
    internal static readonly Color TransparencyColor = Color.FromArgb(255, 0, 255);
    internal static readonly Color CardColor = Color.FromArgb(24, 54, 82);
    internal static readonly Color ItemColor = Color.FromArgb(37, 201, 151);

    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;

    private readonly Rectangle _cardOne;
    private readonly Rectangle _cardTwo;
    private readonly Rectangle _previewItem;

    internal FallbackCanvasForm(Rectangle workArea)
    {
        AutoScaleMode = AutoScaleMode.None;
        BackColor = TransparencyColor;
        Bounds = workArea;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Zhuomian Fallback Visual Usability Probe";
        TopMost = false;
        TransparencyKey = TransparencyColor;

        _cardOne = new Rectangle(80, 100, 360, 220);
        _cardTwo = new Rectangle(520, 100, 360, 220);
        _previewItem = new Rectangle(_cardOne.Left + 32, _cardOne.Top + 92, 112, 80);
    }

    internal int PreviewExecutionCount { get; private set; }

    internal Rectangle ExposedSample => RectangleToScreen(new Rectangle(_cardOne.Left + 190, _cardOne.Top + 120, 80, 60));

    internal Rectangle CoveredSample => RectangleToScreen(new Rectangle(_cardTwo.Left + 190, _cardTwo.Top + 120, 80, 60));

    internal Point PreviewClickPoint => PointToScreen(new Point(_previewItem.Left + 30, _previewItem.Top + 30));

    internal Point TransparentPoint => PointToScreen(new Point(470, 380));

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

    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        base.OnPaint(eventArgs);
        eventArgs.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var cardBrush = new SolidBrush(CardColor);
        using var itemBrush = new SolidBrush(ItemColor);
        using var headingFont = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bodyFont = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);

        eventArgs.Graphics.FillRectangle(cardBrush, _cardOne);
        eventArgs.Graphics.FillRectangle(cardBrush, _cardTwo);
        eventArgs.Graphics.FillRectangle(itemBrush, _previewItem);
        eventArgs.Graphics.DrawString("开发空间", headingFont, textBrush, _cardOne.Left + 24, _cardOne.Top + 22);
        eventArgs.Graphics.DrawString("单击预览 Item", bodyFont, textBrush, _previewItem.Left + 8, _previewItem.Top + 28);
        eventArgs.Graphics.DrawString("应被普通窗口遮挡", headingFont, textBrush, _cardTwo.Left + 24, _cardTwo.Top + 22);
    }

    protected override void OnMouseDown(MouseEventArgs eventArgs)
    {
        if (_previewItem.Contains(eventArgs.Location))
        {
            PreviewExecutionCount++;
        }

        base.OnMouseDown(eventArgs);
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
