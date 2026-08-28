using System.Text.Json.Serialization;

namespace Zhuomian.Spike.PlatformLifecycle;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PlatformLifecycleEvidence))]
internal sealed partial class PlatformLifecycleJsonContext : JsonSerializerContext;
