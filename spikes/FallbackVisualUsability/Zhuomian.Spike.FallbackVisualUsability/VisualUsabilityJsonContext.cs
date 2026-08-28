using System.Text.Json.Serialization;

namespace Zhuomian.Spike.FallbackVisualUsability;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VisualUsabilityEvidence))]
internal sealed partial class VisualUsabilityJsonContext : JsonSerializerContext;
