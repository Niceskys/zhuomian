# Windows launch adapter

This module has no host/UI or discovery dependency. The resolver uses a code-composed exact identity/provenance/deployment digest catalog, default empty. WindowsResourceLease opens fixed local filesystem ancestors and leaf without following reparse points, validates final DOS paths and identity, rejects linked leaves and pins against write/delete until submission. The private lease is required by WindowsLaunchExecutor. ProcessStartInfo has an absolute executable, no arguments and UseShellExecute=false.

No user-app approvals ship. Windows tests compose an approval only for the built no-I/O fixture, and test native failure/ownership paths using private temporary resources. File/Folder/URL return safe Unsupported feedback for unreviewed associations; no ShellExecute fallback exists. The adapter never downloads, deletes, moves or writes a source, never elevates, and does not kill a submitted application. Process wrapper and filesystem handles are disposed separately.

See [ADR-0005](../../docs/ADR/0005-controlled-resource-launch.md), [support/evidence](../../docs/E3_1_LAUNCH_EVIDENCE.md), and [platform tests](../../tests/Zhuomian.Platform.Windows.Tests/WindowsLaunchTests.cs). This does not accept deployment, Windows desktop focus/session/DPI support, or all four real launch domains.
