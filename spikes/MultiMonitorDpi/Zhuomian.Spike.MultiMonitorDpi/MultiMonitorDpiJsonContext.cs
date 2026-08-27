using System.Text.Json.Serialization;

namespace Zhuomian.Spike.MultiMonitorDpi;

[JsonSerializable(typeof(MultiMonitorDpiEvidence))]
internal sealed partial class MultiMonitorDpiJsonContext : JsonSerializerContext;
