using DataGateWin.Models.Ipc;

namespace DataGateWin.Services.Ipc;

public static class EngineEventMapper
{
    public static EngineEvent? Map(DataGateWin.Models.Ipc.IpcEvent ev)
    {
        if (string.IsNullOrWhiteSpace(ev.Type))
            return null;

        if (ev.Type.Equals("Log", StringComparison.OrdinalIgnoreCase))
        {
            var line = ev.Payload?["line"]?.ToString();
            if (string.IsNullOrWhiteSpace(line))
                return null;

            return new EngineEvent { Kind = EngineEventKind.Log, Message = line };
        }

        if (ev.Type.Equals("StateChanged", StringComparison.OrdinalIgnoreCase))
        {
            return new EngineEvent
            {
                Kind = EngineEventKind.StateChanged,
                State = ev.Payload?["state"]?.ToString()
            };
        }

        if (ev.Type.Equals("Connected", StringComparison.OrdinalIgnoreCase))
        {
            return new EngineEvent
            {
                Kind = EngineEventKind.Connected,
                Ip = ev.Payload?["vpnIpv4"]?.ToString()
            };
        }

        if (ev.Type.Equals("Disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return new EngineEvent
            {
                Kind = EngineEventKind.Disconnected,
                Reason = ev.Payload?["reason"]?.ToString()
            };
        }

        if (ev.Type.Equals("Error", StringComparison.OrdinalIgnoreCase))
        {
            return new EngineEvent
            {
                Kind = EngineEventKind.Error,
                Code = ev.Payload?["code"]?.ToString(),
                Message = ev.Payload?["message"]?.ToString()
            };
        }

        return new EngineEvent { Kind = EngineEventKind.Other, RawType = ev.Type };
    }
}