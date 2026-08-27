namespace Zhuomian.Core.Interaction;

[Flags]
public enum InteractionActivity
{
    None = 0,
    Searching = 1 << 0,
    Scrolling = 1 << 1,
    ContextMenuOpen = 1 << 2,
    Dragging = 1 << 3,
    PointerCaptured = 1 << 4,
}
