using Newtonsoft.Json.Linq;

namespace DataGateWin.Models.Ipc;

public sealed class IpcCommand
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public JToken? Payload { get; set; }
}