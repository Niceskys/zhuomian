using System.Text.Json.Serialization;

namespace Zhuomian.Spike.DesktopHosting;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(HostProbeEvidence))]
internal sealed partial class DesktopHostingJsonContext : JsonSerializerContext;
