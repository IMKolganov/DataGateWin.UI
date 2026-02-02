using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace DataGateWin.Services.Auth;

public sealed class AuthenticatedHttpHandler(AuthSession session, HttpMessageHandler innerHandler)
    : DelegatingHandler(innerHandler)
{
    private readonly AuthSession _session = session ?? throw new ArgumentNullException(nameof(session));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var cloned = await CloneHttpRequestMessageAsync(request, ct).ConfigureAwait(false);

        var token = await _session.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var response = await base.SendAsync(request, ct).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        var refreshed = await _session.RefreshAsync(ct).ConfigureAwait(false);
        if (!refreshed)
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);

        var newToken = await _session.GetValidAccessTokenAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(newToken))
        {
            cloned.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newToken);
        }

        return await base.SendAsync(cloned, ct).ConfigureAwait(false);
    }

    private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content != null)
        {
            var ms = new MemoryStream();
            await request.Content.CopyToAsync(ms, ct).ConfigureAwait(false);
            ms.Position = 0;

            var newContent = new StreamContent(ms);

            foreach (var header in request.Content.Headers)
                newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);

            clone.Content = newContent;
        }

        clone.Version = request.Version;
        return clone;
    }
}