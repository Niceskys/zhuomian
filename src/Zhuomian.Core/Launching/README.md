# Controlled launch core

Prepare binds the exact immutable Workspace, usable Space reference, Item, current input generation and clock. Accepted is admission only. Submit consumes once, rechecks safety/snapshot/expiry/cancellation, obtains and revalidates a disposable live resource lease, then calls an injected executor. Structured feedback contains no resource payload or exception text.

Composition must inject reviewed resolver/executor code, update context from the authoritative dispatcher, and keep snapshot/revision/generation monotonic. The initial input gate is closed. Availability is never sufficient permission. Fake adapters may model all four Item domains; production capabilities are narrower.

PreviewLaunchAdapter preserves IInteractionLauncher: pin at down, refresh authoritative Workspace/controller before Drain, cancel retired pins, let E1 recognize the click and its priority, and consume at most once at up. It refreshes the controller gate immediately before service submission. The current string-only launcher contract cannot infer Space/gesture from an Item ID; multiple pending pins fail closed. Rebuild the service/bridge for a new Workspace/controller session. No UI event router is included. Legacy LaunchSucceeded means OS submission through this bridge, not readiness/completion.

See [ADR-0008](../../../docs/ADR/0008-controlled-resource-launch.md) and [support/evidence](../../../docs/E3_1_LAUNCH_EVIDENCE.md). File/Folder/URL real dispatch remains disabled pending reviewed association/broker evidence. The default Windows app catalog is empty.
