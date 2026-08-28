namespace Zhuomian.Spike.ExplorerRecovery;

internal sealed class LifecycleSinkForm : Form
{
    private readonly uint _taskbarCreatedMessage;

    public LifecycleSinkForm()
    {
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessageW("TaskbarCreated");
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = new Rectangle(-32000, -32000, 1, 1);
        Text = "Zhuomian Explorer Lifecycle Sink";
    }

    public bool TaskbarCreatedReceived { get; private set; }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message message)
    {
        if (_taskbarCreatedMessage != 0 && message.Msg == _taskbarCreatedMessage)
        {
            TaskbarCreatedReceived = true;
        }

        base.WndProc(ref message);
    }
}
