using System.Windows;
using System.Windows.Controls;
using DataGateWin.Controllers;
using DataGateWin.Models.Ipc;

namespace DataGateWin.Pages;

public partial class HomePage : Page
{
    private HomeController? _controller;

    public HomePage()
    {
        InitializeComponent();

        _controller = new HomeController(
            statusTextSetter: s => DispatchUi(() => StatusText.Text = s),
            uiStateApplier: (state, status) => DispatchUi(() => ApplyUiState(state, status)),
            logAppender: line => DispatchUi(() => AppendLog(line))
        );
    }

    private async void HomePage_OnLoaded(object sender, RoutedEventArgs e)
        => await _controller!.OnLoadedAsync();

    private void HomePage_OnUnloaded(object sender, RoutedEventArgs e)
        => _controller!.OnUnloaded();

    private async void ConnectButton_OnClick(object sender, RoutedEventArgs e)
        => await _controller!.ConnectAsync();

    private async void DisconnectButton_OnClick(object sender, RoutedEventArgs e)
        => await _controller!.DisconnectAsync();

    private void ApplyUiState(UiState state, string statusText)
    {
        StatusText.Text = statusText;

        var isBusy = state is UiState.Connecting or UiState.Disconnecting;

        ConnectButton.IsEnabled = !isBusy && state == UiState.Idle;
        DisconnectButton.IsEnabled = !isBusy && state is UiState.Connected or UiState.Connecting;
    }

    private void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        var ts = DateTime.Now.ToString("HH:mm:ss");
        LogTextBox.AppendText($"[{ts}] {line}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void DispatchUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }
}