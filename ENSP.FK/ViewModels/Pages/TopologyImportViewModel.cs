using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.FK.Models.Topology;
using ENSP.FK.Services;
using System.Collections.ObjectModel;
using System.IO;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.FK.ViewModels.Pages;

public partial class TopologyImportViewModel : ObservableObject, INavigationAware
{
    private readonly TopologyParser _parser;
    private readonly ProjectSession _session;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "选择一个 .topo 文件开始";

    [ObservableProperty]
    private ObservableCollection<Device> _devices = new();

    [ObservableProperty]
    private ObservableCollection<TopologyLink> _links = new();

    [ObservableProperty]
    private bool _isLoaded;

    public TopologyImportViewModel(TopologyParser parser, ProjectSession session)
    {
        _parser = parser;
        _session = session;
    }

    public Task OnNavigatedToAsync()
    {
        if (_session.Topology != null)
        {
            Devices = new ObservableCollection<Device>(_session.Topology.Devices);
            Links = new ObservableCollection<TopologyLink>(_session.Topology.Links);
            IsLoaded = true;
            StatusMessage = $"已加载: {_session.Topology.Devices.Count} 台设备, {_session.Topology.Links.Count} 条链路";
        }
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    [RelayCommand]
    private void BrowseFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "eNSP 拓扑文件 (*.topo)|*.topo|所有文件 (*.*)|*.*",
            Title = "选择 eNSP 拓扑文件"
        };

        if (dlg.ShowDialog() == true)
        {
            FilePath = dlg.FileName;
            ParseTopology();
        }
    }

    [RelayCommand]
    private void ParseTopology()
    {
        if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
        {
            StatusMessage = "文件不存在";
            return;
        }

        try
        {
            _session.Topology = _parser.Parse(FilePath);
            _session.TopologyFilePath = FilePath;
            _session.Requirements.Clear();
            _session.RawRequirementText = string.Empty;
            _session.Configs.Clear();
            Devices = new ObservableCollection<Device>(_session.Topology.Devices);
            Links = new ObservableCollection<TopologyLink>(_session.Topology.Links);
            IsLoaded = true;
            StatusMessage =
                $"已加载: {_session.Topology.Devices.Count} 台设备, {_session.Topology.Links.Count} 条链路";
            _session.NotifyTopologyChanged();
        }
        catch (Exception ex)
        {
            StatusMessage = $"解析失败: {ex.Message}";
            IsLoaded = false;
        }
    }
}
