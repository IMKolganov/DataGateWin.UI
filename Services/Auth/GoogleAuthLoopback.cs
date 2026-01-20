using System.Diagnostics;
using System.Net;
using System.Text;

namespace DataGateWin.Services;

public sealed class GoogleAuthLoopback
{
    public async Task<string> GetAuthorizationCodeAsync(string authorizationUrl, int port, CancellationToken ct)
    {
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        Process.Start(new ProcessStartInfo
        {
            FileName = authorizationUrl,
            UseShellExecute = true
        });

        using var reg = ct.Register(() =>
        {
            try { listener.Stop(); } catch { }
        });

        var context = await listener.GetContextAsync();
        var code = context.Request.QueryString["code"];

        var responseHtml = "<html><body>You can close this window now.</body></html>";
        var buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length, ct);
        context.Response.Close();

        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Authorization code was not returned.");

        return code;
    }
}
