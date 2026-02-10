using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using DataGateWin.Services.Statistics;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServerClients.Requests;
using OpenVPNGateMonitor.SharedModels.DataGateMonitorBackend.OpenVpnServerClients.Responses;
using OpenVPNGateMonitor.SharedModels.Enums;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Wpf;
using Wpf.Ui.Appearance;

namespace DataGateWin.ViewModels;

public sealed class StatisticsViewModel : INotifyPropertyChanged
{
    private readonly StatisticsApiClient _api;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<OverviewGrouping> GroupingItems { get; } = new[]
    {
        OverviewGrouping.Auto,
        OverviewGrouping.Hours,
        OverviewGrouping.Days,
        OverviewGrouping.Months,
        OverviewGrouping.Years
    };

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set { _isLoading = value; OnPropertyChanged(); }
    }

    private string? _errorText;
    public string? ErrorText
    {
        get => _errorText;
        private set { _errorText = value; OnPropertyChanged(); }
    }

    private string _totalUploadedText = "—";
    public string TotalUploadedText
    {
        get => _totalUploadedText;
        private set { _totalUploadedText = value; OnPropertyChanged(); }
    }

    private string _periodText = "—";
    public string PeriodText
    {
        get => _periodText;
        private set { _periodText = value; OnPropertyChanged(); }
    }

    private PlotModel _plotModel = new();
    public PlotModel PlotModel
    {
        get => _plotModel;
        private set { _plotModel = value; OnPropertyChanged(); }
    }

    private DateTime? _fromLocalDate;
    public DateTime? FromLocalDate
    {
        get => _fromLocalDate;
        set { _fromLocalDate = value; OnPropertyChanged(); UpdatePeriodTextPreview(); }
    }

    private DateTime? _toLocalDate;
    public DateTime? ToLocalDate
    {
        get => _toLocalDate;
        set { _toLocalDate = value; OnPropertyChanged(); UpdatePeriodTextPreview(); }
    }

    private OverviewGrouping _grouping = OverviewGrouping.Auto;
    public OverviewGrouping Grouping
    {
        get => _grouping;
        set { _grouping = value; OnPropertyChanged(); }
    }

    public ICommand ApplyFiltersCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand SetLastDaysCommand { get; }

    // Theme colors used by PlotModel builders
    private OxyColor _chartBg = OxyColors.Transparent;
    private OxyColor _chartFg = OxyColors.White;
    private OxyColor _chartGrid = OxyColor.FromAColor(60, OxyColors.White);

    private OverviewSeriesResponse? _lastData;

    public StatisticsViewModel(StatisticsApiClient api)
    {
        _api = api ?? throw new ArgumentNullException(nameof(api));

        ApplyFiltersCommand = new AsyncRelayCommand(ApplyAsync);
        ResetFiltersCommand = new RelayCommand(ResetFilters);
        SetLastDaysCommand = new RelayCommand<string>(SetLastDays);

        ResetFilters();
        PlotModel = BuildEmptyModel();
    }

    public Task LoadAsync(CancellationToken ct) => ApplyAsync(ct);

    private async Task ApplyAsync(CancellationToken ct)
    {
        ErrorText = null;

        var from = GetFromDateUtc();
        var to = GetToDateUtc();

        if (to <= from)
        {
            ErrorText = "Invalid period: To must be greater than From.";
            return;
        }

        IsLoading = true;
        try
        {
            var req = new GetOverviewSeriesRequest
            {
                From = from,
                To = to,
                Grouping = Grouping
            };

            var data = await _api.GetOverviewSeriesAsync(req, ct);
            _lastData = data;

            TotalUploadedText = FormatBytes(data.Summary.TotalTrafficOutBytes);
            PeriodText = $"{from:yyyy-MM-dd} — {to:yyyy-MM-dd}";

            PlotModel = BuildUploadSeriesModel(data);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ResetFilters()
    {
        Grouping = OverviewGrouping.Auto;

        var nowLocal = DateTime.Now.Date;
        FromLocalDate = nowLocal.AddDays(-7);
        ToLocalDate = nowLocal;

        UpdatePeriodTextPreview();
    }

    private void SetLastDays(string? daysText)
    {
        if (!int.TryParse(daysText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days <= 0)
            return;

        var nowLocal = DateTime.Now.Date;
        FromLocalDate = nowLocal.AddDays(-days);
        ToLocalDate = nowLocal;
    }

    private DateTimeOffset GetFromDateUtc()
    {
        var local = (FromLocalDate ?? DateTime.Now.Date).Date;
        var localStart = new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Local);
        return new DateTimeOffset(localStart).ToUniversalTime();
    }

    private DateTimeOffset GetToDateUtc()
    {
        var local = (ToLocalDate ?? DateTime.Now.Date).Date;
        var localEnd = new DateTime(local.Year, local.Month, local.Day, 23, 59, 59, 999, DateTimeKind.Local);
        return new DateTimeOffset(localEnd).ToUniversalTime();
    }

    private void UpdatePeriodTextPreview()
    {
        var from = FromLocalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
        var to = ToLocalDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—";
        PeriodText = $"{from} — {to}";
    }

    public void SetChartTheme(PlotView plotView, ApplicationTheme theme)
    {
        var bgBrush = plotView.TryFindResource("CardBackgroundFillColorDefaultBrush") as SolidColorBrush;
        var textPrimary = plotView.TryFindResource("TextFillColorPrimaryBrush") as SolidColorBrush;
        var textSecondary = plotView.TryFindResource("TextFillColorSecondaryBrush") as SolidColorBrush;

        // IMPORTANT: set PlotView background (WPF layer)
        plotView.Background = bgBrush ?? Brushes.Transparent;

        var bg = bgBrush?.Color ?? (theme == ApplicationTheme.Dark ? Colors.Black : Colors.White);
        var fg = textPrimary?.Color ?? (theme == ApplicationTheme.Dark ? Colors.White : Colors.Black);
        var gridBase = textSecondary?.Color ?? fg;

        _chartBg = OxyColor.FromArgb(bg.A, bg.R, bg.G, bg.B);
        _chartFg = OxyColor.FromArgb(fg.A, fg.R, fg.G, fg.B);
        _chartGrid = OxyColor.FromArgb(60, gridBase.R, gridBase.G, gridBase.B);

        RefreshChart();

        // Force redraw (important!)
        plotView.InvalidatePlot(true);
    }

    public void RefreshChart()
    {
        PlotModel = _lastData is null
            ? BuildEmptyModel()
            : BuildUploadSeriesModel(_lastData);
    }

    private PlotModel BuildUploadSeriesModel(OverviewSeriesResponse data)
    {
        var model = BuildEmptyModel();

        var series = new AreaSeries
        {
            Title = "Upload",
            StrokeThickness = 2,
            ConstantY2 = 0
        };

        foreach (var row in data.OverviewSeriesRows)
        {
            var x = DateTimeAxis.ToDouble(row.Ts.UtcDateTime);
            series.Points.Add(new DataPoint(x, row.TrafficOutBytes));
            series.Points2.Add(new DataPoint(x, 0));
        }

        model.Series.Add(series);
        return model;
    }

    private PlotModel BuildEmptyModel()
    {
        var model = new PlotModel
        {
            Background = _chartBg,
            TextColor = _chartFg,
            PlotAreaBorderThickness = new OxyThickness(0)
        };

        model.Axes.Add(new DateTimeAxis
        {
            Position = AxisPosition.Bottom,
            StringFormat = "dd MMM",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.None,
            MajorGridlineColor = _chartGrid,
            TicklineColor = _chartGrid,
            AxislineColor = _chartGrid,
            TextColor = _chartFg
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Traffic",
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.None,
            MajorGridlineColor = _chartGrid,
            TicklineColor = _chartGrid,
            AxislineColor = _chartGrid,
            TextColor = _chartFg,
            TitleColor = _chartFg,
            LabelFormatter = v => FormatBytes((long)v)
        });

        return model;
    }

    private static string FormatBytes(long bytes)
    {
        const double k = 1024.0;
        if (bytes < k) return $"{bytes.ToString(CultureInfo.InvariantCulture)} B";

        var kb = bytes / k;
        if (kb < k) return $"{kb:F1} KB";

        var mb = kb / k;
        if (mb < k) return $"{mb:F1} MB";

        var gb = mb / k;
        return $"{gb:F2} GB";
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
