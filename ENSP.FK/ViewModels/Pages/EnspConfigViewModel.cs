using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.FK.Models;
using ENSP.FK.Services;
using System.Diagnostics;
using System.IO;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.FK.ViewModels.Pages;

public partial class EnspConfigViewModel : ObservableObject, INavigationAware
{
    private readonly ProjectSession _session;
    private readonly ApiConfig _apiConfig;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _topoLoaded;

    [ObservableProperty]
    private string _topoFileName = string.Empty;

    [ObservableProperty]
    private string _enspPath = string.Empty;

    [ObservableProperty]
    private bool _enspFound;

    public EnspConfigViewModel(ProjectSession session, ApiConfig apiConfig)
    {
        _session = session;
        _apiConfig = apiConfig;
    }

    public Task OnNavigatedToAsync()
    {
        RefreshState();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void RefreshState()
    {
        TopoLoaded = _session.Topology != null && !string.IsNullOrEmpty(_session.TopologyFilePath);
        TopoFileName = TopoLoaded ? Path.GetFileName(_session.TopologyFilePath!) : string.Empty;

        var enspExe = SystemDiagnosticsService.FindEnspExePath(_apiConfig.EnspPath);
        EnspFound = enspExe != null;
        EnspPath = enspExe ?? string.Empty;

        if (!EnspFound)
            Status = "未找到 eNSP 安装路径";
        else if (!TopoLoaded)
            Status = $"eNSP 路径: {EnspPath}\n请先在「拓扑导入」中导入 .topo 文件";
        else
            Status = $"eNSP: {EnspPath}\n拓扑: {TopoFileName} ({_session.Topology!.Devices.Count} 台设备)";
    }

    [RelayCommand]
    private void LaunchEnsp()
    {
        if (!TopoLoaded)
        {
            Status = "未加载拓扑文件";
            return;
        }

        if (!EnspFound || string.IsNullOrEmpty(EnspPath))
        {
            Status = "未找到 eNSP 安装路径";
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = EnspPath,
                Arguments = $"\"{_session.TopologyFilePath}\"",
                UseShellExecute = true
            };
            Process.Start(psi);
            Status = $"已启动 eNSP，打开: {TopoFileName}";
        }
        catch (Exception ex)
        {
            Status = $"启动 eNSP 失败: {ex.Message}";
        }
    }
}
