using System.Globalization;
using System.Windows.Data;

namespace DataGateWin.ViewModels.Utils;

public sealed class BytesToHumanReadableConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return "-";

        if (!TryToInt64(value, out var bytes)) return value.ToString() ?? "-";
        if (bytes < 0) return bytes.ToString(culture);

        var unitSystem = (parameter as string)?.Trim().ToUpperInvariant() ?? "IEC"; // IEC or SI

        double size = bytes;
        string[] units;
        double step;

        if (unitSystem == "SI")
        {
            units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
            step = 1000d;
        }
        else
        {
            units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];
            step = 1024d;
        }

        var unitIndex = 0;
        while (size >= step && unitIndex < units.Length - 1)
        {
            size /= step;
            unitIndex++;
        }

        // 0 decimals for bytes, 1 for KB+, tweak if you want
        var decimals = unitIndex == 0 ? 0 : 1;
        return $"{Math.Round(size, decimals).ToString($"F{decimals}", culture)} {units[unitIndex]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static bool TryToInt64(object value, out long result)
    {
        switch (value)
        {
            case long l: result = l; return true;
            case int i: result = i; return true;
            case uint ui: result = ui; return true;
            case ulong ul when ul <= long.MaxValue: result = (long)ul; return true;
            case short s: result = s; return true;
            case ushort us: result = us; return true;
            case byte b: result = b; return true;
            case sbyte sb: result = sb; return true;
            case string str when long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                result = parsed; return true;
            default:
                try
                {
                    result = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
                    return true;
                }
                catch
                {
                    result = 0;
                    return false;
                }
        }
    }
}
