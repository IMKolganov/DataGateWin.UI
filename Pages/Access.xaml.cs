using System.Windows.Controls;
using DataGateWin.Services.Access;
using DataGateWin.Services.VpnServers;
using DataGateWin.ViewModels;

namespace DataGateWin.Pages;

public partial class Access : Page
{
    public Access()
    {
        InitializeComponent();

        var http = App.AuthedApiHttp;
        var serversApi = new OpenVpnServersApiClient(http);
        var quotaApi = new UserVpnAccessClient(http);
        DataContext = new AccessViewModel(serversApi, quotaApi, App.Session);
    }
}