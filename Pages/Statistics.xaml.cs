using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using DataGateWin.Services.Statistics;
using DataGateWin.ViewModels;
using Wpf.Ui.Appearance;

namespace DataGateWin.Pages;

public partial class Statistics : Page
{
    private readonly StatisticsViewModel _vm;

    public Statistics(HttpClient authedApiHttp)
    {
        InitializeComponent();
        _vm = new StatisticsViewModel(new StatisticsApiClient(authedApiHttp));
        DataContext = _vm;

        ApplicationThemeManager.Changed += OnThemeChanged;
    }

    private void OnThemeChanged(ApplicationTheme currentApplicationTheme, System.Windows.Media.Color systemAccent)
    {
        _vm.SetChartTheme(Chart, currentApplicationTheme);
        _vm.RefreshChart();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.SetChartTheme(Chart, ApplicationThemeManager.GetAppTheme());
        await _vm.LoadAsync(CancellationToken.None);
    }
}