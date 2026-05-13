using ENSP.FK.Services;
using System.Collections.ObjectModel;
using System.IO;
using Wpf.Ui.Controls;

namespace ENSP.FK.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = "ENSP.FK — eNSP 自动配置工具";

    public MainWindowViewModel(ProjectSession session)
    {
        session.TopologyChanged += () =>
        {
            var file = session.TopologyFilePath;
            if (!string.IsNullOrEmpty(file))
                ApplicationTitle = $"ENSP.FK — {Path.GetFileNameWithoutExtension(file)} — eNSP 自动配置工具";
            else
                ApplicationTitle = "ENSP.FK — eNSP 自动配置工具";
        };
    }

    [ObservableProperty]
    private ObservableCollection<object> _menuItems = new()
    {
        new NavigationViewItem()
        {
            Content = "首页",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Home24 },
            TargetPageType = typeof(Views.Pages.DashboardPage)
        },
        new NavigationViewItem()
        {
            Content = "拓扑导入",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Organization24 },
            TargetPageType = typeof(Views.Pages.TopologyImportPage)
        },
        new NavigationViewItem()
        {
            Content = "任务需求",
            Icon = new SymbolIcon { Symbol = SymbolRegular.TaskListSquareLtr24 },
            TargetPageType = typeof(Views.Pages.RequirementsPage)
        },
        new NavigationViewItem()
        {
            Content = "配置输出",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Code24 },
            TargetPageType = typeof(Views.Pages.ConfigOutputPage)
        },
        new NavigationViewItem()
        {
            Content = "ENSP RUN",
            Icon = new SymbolIcon { Symbol = SymbolRegular.PlayCircle24 },
            TargetPageType = typeof(Views.Pages.EnspConfigPage)
        },
        new NavigationViewItem()
        {
            Content = "ENSP 扫描",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Globe24 },
            TargetPageType = typeof(Views.Pages.EnspScanPage)
        }
    };

    [ObservableProperty]
    private ObservableCollection<object> _footerMenuItems = new()
    {
        new NavigationViewItem()
        {
            Content = "调试",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Wrench24 },
            TargetPageType = typeof(Views.Pages.DiagnosticsPage)
        },
        new NavigationViewItem()
        {
            Content = "设置",
            Icon = new SymbolIcon { Symbol = SymbolRegular.Settings24 },
            TargetPageType = typeof(Views.Pages.SettingsPage)
        }
    };

    [ObservableProperty]
    private ObservableCollection<MenuItem> _trayMenuItems = new()
    {
        new MenuItem { Header = "主页", Tag = "tray_home" }
    };
}
