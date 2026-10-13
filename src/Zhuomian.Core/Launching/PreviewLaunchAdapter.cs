using Zhuomian.Core.Interaction;
using Zhuomian.Core.WorkspaceModel;

namespace Zhuomian.Core.Launching;

/// <summary>
/// Future single-dispatcher E1 bridge. Pin exactly once at pointer-down; TryLaunch never prepares a new request.
/// The caller retains physical gesture recognition/priority ordering in SpaceInteractionController.
/// Never pin multiple pending gestures before Drain. Snapshot replacement invalidates the previous request.
/// </summary>
public sealed class PreviewLaunchAdapter : IInteractionLauncher
{
    private readonly ItemLaunchService _service;
    private LaunchRequest? _request;
    private Workspace? _workspace;
    private SpaceInteractionController? _controller;
    public LaunchResult LastResult { get; private set; } = new(LaunchStatus.Denied, LaunchReason.InvalidReference);

    public PreviewLaunchAdapter(ItemLaunchService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
    }

    public void Pin(LaunchAdmission admission)
    {
        // A second unconsumed pin is ambiguous with the string-only E1 boundary: fail closed.
        if (_request is not null)
        {
            _request = null;
            LastResult = new(LaunchStatus.Denied, LaunchReason.StaleContext);
            return;
        }
        _request = admission.Request;
        LastResult = admission.Result;
    }

    public void Cancel()
    {
        _request = null;
        LastResult = new(LaunchStatus.Cancelled, LaunchReason.RequestCancelled);
    }

    /// <summary>Refresh before Drain AND immediately before launch, from the authoritative controller state.</summary>
    public void Refresh(Workspace workspace, SpaceInteractionController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _workspace = workspace;
        _controller = controller;
        bool allowed;
        try
        {
            allowed = controller.HostAvailable && controller.Availability != DesktopAvailability.Suspended &&
            !controller.DragActive && !controller.ImeActive && !controller.ModalOpen &&
            workspace.Spaces.Keys.All(id => !controller.GetState(id.ToString("D")).Activities.HasFlag(InteractionActivity.Dragging) &&
                (controller.GetState(id.ToString("D")).Activities & (InteractionActivity.ContextMenuOpen | InteractionActivity.PointerCaptured)) == 0);
        }
        catch (KeyNotFoundException) { allowed = false; }
        _service.UpdateContext(workspace, controller.InputGeneration, allowed);
    }

    public bool TryLaunch(string itemId)
    {
        var request = _request;
        _request = null;
        if (request is null || !Guid.TryParseExact(itemId, "D", out var id) || id != request.Item.Id)
        {
            LastResult = new(LaunchStatus.Denied, LaunchReason.InvalidReference);
            return false;
        }
        if (_workspace is null || _controller is null)
        {
            LastResult = new(LaunchStatus.Denied, LaunchReason.StaleContext);
            return false;
        }
        Refresh(_workspace, _controller);
        LastResult = _service.Submit(request);
        // Legacy LaunchSucceeded means OS submission here, never app-ready/file-open completion.
        return LastResult.Status == LaunchStatus.SubmittedToOperatingSystem;
    }
}
