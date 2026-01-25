using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataGateWin.Models.Ipc;

public sealed class IpcReply
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("ok")]
    public bool Ok { get; set; }

    [JsonProperty("payload")]
    public JToken? Payload { get; set; }

    [JsonProperty("error")]
    public IpcError? Error { get; set; }

    [JsonIgnore]
    public string? Code => Error?.Code;

    [JsonIgnore]
    public string? Message => Error?.Message;

    [JsonIgnore]
    public string PayloadRaw => Payload == null ? "{}" : Payload.ToString(Formatting.None);
}