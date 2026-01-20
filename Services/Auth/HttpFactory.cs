using System.Net.Http;
using DataGateWin.Services.Auth;

namespace DataGateWin.Services;

public static class HttpFactory
{
    public static (AuthApiClient authApi, HttpClient api) CreateClients(string baseUrl, AuthSession session)
    {
        var authHttp = new HttpClient
        {
            BaseAddress = new Uri(baseUrl)
        };

        var authApi = new AuthApiClient(authHttp);

        var inner = new HttpClientHandler();

        var apiHttp = new HttpClient(new AuthenticatedHttpHandler(session, inner))
        {
            BaseAddress = new Uri(baseUrl)
        };

        return (authApi, apiHttp);
    }
}