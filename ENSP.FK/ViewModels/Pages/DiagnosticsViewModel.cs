using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.FK.Services;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.FK.ViewModels.Pages;

public partial class DiagnosticsViewModel : ObservableObject, INavigationAware
{
    private readonly SystemDiagnosticsService _diag;

    [ObservableProperty]
    private ObservableCollection<AppStatus> _results = new();

    [ObservableProperty]
    private string _lastCheckTime = string.Empty;

    public DiagnosticsViewModel(SystemDiagnosticsService diag)
    {
        _diag = diag;
    }

    public Task OnNavigatedToAsync()
    {
        RunCheck();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    [RelayCommand]
    private void RunCheck()
    {
        var items = _diag.CheckAll();
        Results = new ObservableCollection<AppStatus>(items);
        LastCheckTime = $"检查时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
    }
}
