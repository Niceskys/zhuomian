namespace Zhuomian.Spike.FocusAndInput;

internal sealed class KeyboardSurfaceForm : Form
{
    public KeyboardSurfaceForm(Rectangle bounds)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(44, 68, 103);
        Bounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        KeyPreview = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = "Zhuomian Explicit Keyboard Probe";
        TopMost = false;

        InputBox = new TextBox
        {
            AccessibleName = "Focus probe input",
            Bounds = new Rectangle(40, 80, bounds.Width - 80, 40),
            Font = new Font("Segoe UI", 14),
        };

        Controls.Add(InputBox);
        KeyDown += OnKeyDown;
    }

    public bool EscapeHandled { get; private set; }

    public TextBox InputBox { get; }

    protected override bool ShowWithoutActivation => true;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            KeyDown -= OnKeyDown;
        }

        base.Dispose(disposing);
    }

    private void OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode != Keys.Escape)
        {
            return;
        }

        EscapeHandled = true;
        eventArgs.Handled = true;
        eventArgs.SuppressKeyPress = true;
        Close();
    }
}
