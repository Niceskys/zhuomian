using System.Text.Json.Serialization;

namespace Zhuomian.Spike.FocusAndInput;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(FocusAndInputEvidence))]
internal sealed partial class FocusAndInputJsonContext : JsonSerializerContext;
