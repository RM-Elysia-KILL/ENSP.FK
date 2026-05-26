using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.ZD.Services;
using System.Collections.ObjectModel;
using System.IO;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.ViewModels.Pages;

public partial class DiagnosticsViewModel : ObservableObject, INavigationAware
{
    private readonly SystemDiagnosticsService _diag;
    private readonly ImageRecognitionService _imageRec;
    private readonly DeviceIconService _iconService;
    private readonly LogService _log;

    [ObservableProperty]
    private ObservableCollection<AppStatus> _results = new();

    [ObservableProperty]
    private string _lastCheckTime = string.Empty;

    // Toolbar button template capture
    [ObservableProperty]
    private string _toolbarCaptureStatus = string.Empty;

    [ObservableProperty]
    private int _toolbarCaptureCountdown;

    [ObservableProperty]
    private bool _isCapturingToolbar;

    // Log viewer
    [ObservableProperty]
    private string _logContent = string.Empty;

    [ObservableProperty]
    private string _logPathText = string.Empty;

    public DiagnosticsViewModel(SystemDiagnosticsService diag,
        ImageRecognitionService imageRec, DeviceIconService iconService, LogService log)
    {
        _diag = diag;
        _imageRec = imageRec;
        _iconService = iconService;
        _log = log;
    }

    public Task OnNavigatedToAsync()
    {
        RunCheck();
        RefreshLog();
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

    [RelayCommand]
    private async Task CaptureToolbarButton()
    {
        IsCapturingToolbar = true;
        var mainWindow = System.Windows.Application.Current.MainWindow;

        try
        {
            if (mainWindow != null)
                mainWindow.WindowState = System.Windows.WindowState.Minimized;

            for (int i = 5; i >= 1; i--)
            {
                ToolbarCaptureCountdown = i;
                ToolbarCaptureStatus = $"⏳ {i} 秒后截取 — 请将鼠标移到 eNSP 工具栏「一键启动全部设备」按钮上...";
                await Task.Delay(1000);
            }

            using var bmp = _imageRec.CaptureCursorRegion(48);

            if (mainWindow != null)
                mainWindow.WindowState = System.Windows.WindowState.Normal;

            var path = Path.Combine(_iconService.TemplatesDir, "start_all.png");
            ImageRecognitionService.SaveTemplate(bmp, path);

            _imageRec.ClearCache();

            ToolbarCaptureStatus = $"✓ 已保存: {path}";
        }
        catch (Exception ex)
        {
            ToolbarCaptureStatus = $"✗ 截图失败: {ex.Message}";
            if (mainWindow != null && mainWindow.WindowState == System.Windows.WindowState.Minimized)
                mainWindow.WindowState = System.Windows.WindowState.Normal;
        }
        finally
        {
            IsCapturingToolbar = false;
            ToolbarCaptureCountdown = 0;
        }
    }

    [RelayCommand]
    private void RefreshLog()
    {
        var lines = _log.ReadRecentLines(200);
        LogContent = string.Join(Environment.NewLine, lines);
        LogPathText = _log.CurrentLogPath ?? "(尚未记录)";
    }
}
