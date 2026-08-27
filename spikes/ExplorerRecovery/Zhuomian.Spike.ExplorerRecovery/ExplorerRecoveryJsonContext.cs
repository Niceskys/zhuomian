using System.Text.Json.Serialization;

namespace Zhuomian.Spike.ExplorerRecovery;

[JsonSerializable(typeof(ExplorerRecoveryEvidence))]
[JsonSerializable(typeof(ReadySignal))]
internal sealed partial class ExplorerRecoveryJsonContext : JsonSerializerContext;

internal sealed record ReadySignal(bool Ready, uint ShellProcessId);
