using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Tray;

namespace DataGateWin.Services.Tray;

public sealed class TrayService
{
    private readonly AppNotifyIcon _notify;
    private Window? _mainWindow;

    private bool _exitRequested;

    public TrayService()
    {
        _notify = new AppNotifyIcon();

        _notify.TooltipText = "DataGate OpenVPN 3";
        _notify.ContextMenu = BuildContextMenu();
    }

    public void AttachMainWindow(Window mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));

        _notify.SetParentWindow(mainWindow);

        mainWindow.StateChanged += MainWindowOnStateChanged;
        mainWindow.Closing += MainWindowOnClosing;
    }

    public void Register()
    {
        _notify.Register();
    }

    public void Unregister()
    {
        _notify.Unregister();
    }

    public void ShowMainWindow()
    {
        if (_mainWindow == null)
            return;

        if (!_mainWindow.IsVisible)
            _mainWindow.Show();

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;

        _mainWindow.Activate();
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();
    }

    public void HideMainWindow()
    {
        _mainWindow?.Hide();
    }

    public void RequestExit()
    {
        _exitRequested = true;

        try { Unregister(); } catch { }

        try
        {
            if (_mainWindow != null)
            {
                _mainWindow.StateChanged -= MainWindowOnStateChanged;
                _mainWindow.Closing -= MainWindowOnClosing;
            }
        }
        catch { }

        Application.Current.Shutdown();
    }

    private void MainWindowOnStateChanged(object? sender, EventArgs e)
    {
        if (_mainWindow == null)
            return;

        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.Hide();
    }

    private void MainWindowOnClosing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
            return;

        e.Cancel = true;
        _mainWindow?.Hide();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var show = new MenuItem { Header = "Show" };
        show.Click += (_, _) => ShowMainWindow();

        var hide = new MenuItem { Header = "Hide" };
        hide.Click += (_, _) => HideMainWindow();

        var sep = new Separator();

        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => RequestExit();

        menu.Items.Add(show);
        menu.Items.Add(hide);
        menu.Items.Add(sep);
        menu.Items.Add(exit);

        return menu;
    }

    private sealed class AppNotifyIcon : NotifyIconService
    {
        public Action? LeftClickAction { get; set; }
        public Action? LeftDoubleClickAction { get; set; }

        protected override void OnLeftClick()
        {
            LeftClickAction?.Invoke();
        }

        protected override void OnLeftDoubleClick()
        {
            LeftDoubleClickAction?.Invoke();
        }
    }

    public void EnableDefaultClickBehavior()
    {
        _notify.LeftClickAction = () =>
        {
            if (_mainWindow == null)
                return;

            if (_mainWindow.IsVisible)
                HideMainWindow();
            else
                ShowMainWindow();
        };

        _notify.LeftDoubleClickAction = ShowMainWindow;
    }
}
