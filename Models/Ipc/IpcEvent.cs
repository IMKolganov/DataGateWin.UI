using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Models.Ipc;

public sealed class IpcEvent
{
    public string? Type { get; set; }
    public JToken? Payload { get; set; }

    [JsonIgnore]
    public string PayloadRaw => Payload == null ? "{}" : Payload.ToString(Formatting.None);

    [JsonIgnore]
    public string? PayloadLine => Payload?["line"]?.Value<string>();

    [JsonIgnore]
    public string? PayloadState => Payload?["state"]?.Value<string>();

    [JsonIgnore]
    public bool? PayloadFatal => Payload?["fatal"]?.Value<bool>();

    [JsonIgnore]
    public string? PayloadReason => Payload?["reason"]?.Value<string>();
}