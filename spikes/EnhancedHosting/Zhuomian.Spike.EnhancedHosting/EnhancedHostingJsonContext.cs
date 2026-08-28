using System.Text.Json.Serialization;

namespace Zhuomian.Spike.EnhancedHosting;

[JsonSerializable(typeof(EnhancedHostingEvidence))]
internal sealed partial class EnhancedHostingJsonContext : JsonSerializerContext;
