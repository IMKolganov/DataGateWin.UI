using System.Windows.Controls;
using DataGateWin.Services.VpnServers;
using DataGateWin.ViewModels;

namespace DataGateWin.Pages;

public partial class Access : Page
{
    public Access()
    {
        InitializeComponent();

        var serversApi = new OpenVpnServersApiClient(App.AuthedApiHttp);
        DataContext = new AccessViewModel(serversApi);
    }
}