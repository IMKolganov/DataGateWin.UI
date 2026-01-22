using Newtonsoft.Json;

namespace DataGateWin.Models.Ipc;

public sealed class IpcError
{
    [JsonProperty("code")]
    public string? Code { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }
}