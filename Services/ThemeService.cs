using Wpf.Ui.Appearance;

namespace DataGateWin.Services;

public sealed class ThemeService
{
    public void SetDark() => ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    public void SetLight() => ApplicationThemeManager.Apply(ApplicationTheme.Light);
}