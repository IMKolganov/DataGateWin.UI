using System.IO;

namespace DataGateWin.Services.Ipc;

public sealed class EnginePathResolver
{
    public string ResolveEngineExePath()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(baseDir, "engine", "engine.exe");
    }
}
