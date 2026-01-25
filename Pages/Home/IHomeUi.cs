using DataGateWin.Models.Ipc;

namespace DataGateWin.Pages.Home;

public interface IHomeUi
{
    void ApplyUiState(UiState state, string statusText);
    void AppendLog(string line);
}