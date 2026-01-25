using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using Wpf.Ui.Tray.Controls;

namespace DataGateWin.Services.Tray;

public sealed class TrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private Window? _mainWindow;
    private bool _isRegistered;

    public void AttachMainWindow(Window mainWindow)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
    }

    public void Register()
    {
        if (_mainWindow == null)
            throw new InvalidOperationException("Main window is not attached.");

        if (_isRegistered)
            return;

        var icon = Application.Current.Resources["TrayIcon"] as NotifyIcon
                   ?? throw new InvalidOperationException("TrayIcon resource not found.");

        _notifyIcon = icon;

        _mainWindow.Closing += OnMainWindowClosing;
        
        var window = _mainWindow;

        void RegisterCore()
        {
            if (_isRegistered)
                return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            icon.ParentHandle = hwnd;
            icon.Register();
            _isRegistered = true;
        }

        // If handle already exists (e.g., you called after Show()), register immediately.
        RegisterCore();

        // Otherwise, wait for the window to create its handle.
        if (!_isRegistered)
        {
            window.SourceInitialized += OnSourceInitialized;

            void OnSourceInitialized(object? sender, EventArgs e)
            {
                window.SourceInitialized -= OnSourceInitialized;
                RegisterCore();
            }
        }
    }

    public void Unregister()
    {
        if (_mainWindow != null)
            _mainWindow.Closing -= OnMainWindowClosing;

        if (_isRegistered)
        {
            _notifyIcon?.Unregister();
            _isRegistered = false;
        }

        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }

    private void OnMainWindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        _mainWindow?.Hide();
    }

    public void Dispose()
    {
        Unregister();
    }
}
