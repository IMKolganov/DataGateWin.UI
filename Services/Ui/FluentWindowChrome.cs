using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace DataGateWin.Services.Ui;

/// <summary>
/// Applies DWM immersive dark mode to WPF-UI <see cref="Wpf.Ui.Controls.FluentWindow"/> instances so the
/// non-client / composition path does not flash light gray or white during move or live resize (same idea as MainWindow).
/// </summary>
public static class FluentWindowChrome
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    private static int _themeSubscriptionCount;
    private static ThemeChangedEvent? _themeChanged;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Attach(Window window)
    {
        window.SourceInitialized += OnWindowSourceInitialized;
        window.Closed += OnWindowClosed;

        if (Interlocked.Increment(ref _themeSubscriptionCount) == 1)
        {
            _themeChanged = OnApplicationThemeChanged;
            ApplicationThemeManager.Changed += _themeChanged;
        }
    }

    private static void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window w)
            return;

        w.SourceInitialized -= OnWindowSourceInitialized;
        TryApplyToWindow(w);
    }

    private static void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not Window w)
            return;

        w.SourceInitialized -= OnWindowSourceInitialized;
        w.Closed -= OnWindowClosed;

        if (Interlocked.Decrement(ref _themeSubscriptionCount) == 0 && _themeChanged is not null)
        {
            ApplicationThemeManager.Changed -= _themeChanged;
            _themeChanged = null;
        }
    }

    private static void OnApplicationThemeChanged(ApplicationTheme applicationTheme, Color systemAccent)
        => RefreshAllWindows();

    private static void RefreshAllWindows()
    {
        try
        {
            foreach (Window w in Application.Current.Windows)
                TryApplyToWindow(w);
        }
        catch
        {
            // ignore during shutdown
        }
    }

    private static void TryApplyToWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var useDark = ShouldUseDarkChrome() ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref useDark, Marshal.SizeOf<int>());
    }

    private static bool ShouldUseDarkChrome()
    {
        try
        {
            var theme = App.Settings?.Theme;
            if (!string.IsNullOrWhiteSpace(theme))
                return !string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // App.Settings not ready
        }

        return ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
    }
}
