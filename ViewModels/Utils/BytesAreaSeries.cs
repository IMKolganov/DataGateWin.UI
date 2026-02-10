using System.Globalization;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace DataGateWin.ViewModels.Utils;

public sealed class BytesAreaSeries : AreaSeries
{
    public Func<long, string>? BytesFormatter { get; set; }

    public override TrackerHitResult GetNearestPoint(ScreenPoint point, bool interpolate)
    {
        var result = base.GetNearestPoint(point, interpolate);
        if (result is null)
            return result!;

        var xValue = result.DataPoint.X;
        var yValue = result.DataPoint.Y;

        var xAxisTitle = XAxis?.Title ?? XYAxisSeries.DefaultXAxisTitle;
        var yAxisTitle = YAxis?.Title ?? XYAxisSeries.DefaultYAxisTitle;

        var dt = DateTimeAxis.ToDateTime(xValue);
        var bytes = (long)Math.Max(0, yValue);

        var bytesText = BytesFormatter is null
            ? bytes.ToString(CultureInfo.InvariantCulture)
            : BytesFormatter(bytes);

        result.Text =
            $"{Title}\n" +
            $"{dt:yyyy-MM-dd HH:mm}\n" +
            $"Upload: {bytesText}";

        return result;
    }
}