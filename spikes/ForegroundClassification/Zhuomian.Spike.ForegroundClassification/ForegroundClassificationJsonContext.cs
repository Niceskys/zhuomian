using System.Text.Json.Serialization;

namespace Zhuomian.Spike.ForegroundClassification;

[JsonSerializable(typeof(ForegroundClassificationEvidence))]
internal sealed partial class ForegroundClassificationJsonContext : JsonSerializerContext;
