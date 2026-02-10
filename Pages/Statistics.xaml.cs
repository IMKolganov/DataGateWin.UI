using System;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DataGateWin.Services.Auth;
using DataGateWin.Services.Statistics;
using DataGateWin.ViewModels;
using Wpf.Ui.Appearance;

namespace DataGateWin.Pages;

public partial class Statistics : Page
{
    private readonly StatisticsViewModel _vm;

    private readonly DispatcherTimer _resizeDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(80)
    };

    public Statistics(HttpClient authedApiHttp, AuthSession session)
    {
        InitializeComponent();

        _vm = new StatisticsViewModel(
            new StatisticsApiClient(authedApiHttp),
            session);
        DataContext = _vm;

        ApplicationThemeManager.Changed += OnThemeChanged;

        _resizeDebounce.Tick += (_, _) =>
        {
            _resizeDebounce.Stop();
            Chart.InvalidatePlot(true);
        };

        Chart.SizeChanged += (_, _) =>
        {
            _resizeDebounce.Stop();
            _resizeDebounce.Start();
        };
    }

    private void OnThemeChanged(ApplicationTheme currentApplicationTheme, System.Windows.Media.Color systemAccent)
    {
        _vm.SetChartTheme(Chart, currentApplicationTheme);
        _vm.RefreshChart();
        Chart.InvalidatePlot(true);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.SetChartTheme(Chart, ApplicationThemeManager.GetAppTheme());
        await _vm.LoadAsync(CancellationToken.None);
        Chart.InvalidatePlot(true);
    }
}