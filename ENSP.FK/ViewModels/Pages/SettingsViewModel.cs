using ENSP.ZD.Models;
using ENSP.ZD.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace ENSP.ZD.ViewModels.Pages;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ApiConfig _apiConfig;
    private readonly AIConfigGenerator _aiGenerator;

    [ObservableProperty]
    private string _appVersion = string.Empty;

    [ObservableProperty]
    private string _gitHubUrl = string.Empty;

    [ObservableProperty]
    private ApplicationTheme _currentTheme = ApplicationTheme.Unknown;

    // API settings
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _apiKey = string.Empty;

    [ObservableProperty]
    private string _modelName = string.Empty;

    [ObservableProperty]
    private string _enspPath = string.Empty;

    [ObservableProperty]
    private string _configOutputPath = string.Empty;

    [ObservableProperty]
    private string _apiStatus = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    // Image recognition template capture
    [ObservableProperty]
    private string _captureModel = string.Empty;

    [ObservableProperty]
    private string _captureStatus = string.Empty;

    [ObservableProperty]
    private int _captureCountdown;

    [ObservableProperty]
    private bool _isCapturing;

    private readonly ImageRecognitionService _imageRec;
    private readonly DeviceIconService _iconService;

    public SettingsViewModel(ApiConfig apiConfig, AIConfigGenerator aiGenerator,
        ImageRecognitionService imageRec, DeviceIconService iconService)
    {
        _apiConfig = apiConfig;
        _aiGenerator = aiGenerator;
        _imageRec = imageRec;
        _iconService = iconService;
    }

    public Task OnNavigatedToAsync()
    {
        LoadSettings();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    private void LoadSettings()
    {
        CurrentTheme = ApplicationThemeManager.GetAppTheme();
        AppVersion = $"ENSP.ZD v{GetAssemblyVersion()}";
        GitHubUrl = "https://github.com/RM-Elysia-KILL/ENSP.FK";

        BaseUrl = _apiConfig.BaseUrl;
        ApiKey = _apiConfig.ApiKey;
        ModelName = _apiConfig.ModelName;
        EnspPath = _apiConfig.EnspPath;
        ConfigOutputPath = _apiConfig.ConfigOutputPath;
    }

    private static string GetAssemblyVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? string.Empty;
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        if (!string.IsNullOrWhiteSpace(GitHubUrl))
            Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void ShowChangelog()
    {
        try
        {
            var window = App.Services.GetRequiredService<Views.Windows.ChangelogWindow>();
            window.Owner = Application.Current.MainWindow;
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Changelog] Error opening: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OnChangeTheme(string parameter)
    {
        switch (parameter)
        {
            case "theme_light":
                if (CurrentTheme == ApplicationTheme.Light) break;
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                CurrentTheme = ApplicationTheme.Light;
                break;
            default:
                if (CurrentTheme == ApplicationTheme.Dark) break;
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                CurrentTheme = ApplicationTheme.Dark;
                break;
        }
    }

    [RelayCommand]
    private void SaveApiConfig()
    {
        _apiConfig.BaseUrl = BaseUrl;
        _apiConfig.ApiKey = ApiKey;
        _apiConfig.ModelName = ModelName;
        _apiConfig.EnspPath = EnspPath;
        _apiConfig.ConfigOutputPath = ConfigOutputPath;
        _apiConfig.Save();
        ApiStatus = "配置已保存到 " + ApiConfig.ConfigPath;
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            ApiStatus = "✗ Base URL 不能为空";
            return;
        }

        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            ApiStatus = "✗ API Key 不能为空";
            return;
        }

        // Persist current values so AIConfigGenerator reads the latest
        _apiConfig.BaseUrl = BaseUrl;
        _apiConfig.ApiKey = ApiKey;
        _apiConfig.ModelName = ModelName;
        _apiConfig.Save();

        IsTesting = true;
        ApiStatus = "正在测试连接...";

        try
        {
            var (reachable, latency, err) = await _aiGenerator.TestConnectivityAsync();

            if (reachable)
                ApiStatus = $"✓ 连接成功 — 延迟 {latency}ms — {ModelName}";
            else
                ApiStatus = $"✗ 连接失败: {err}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task StartCapture()
    {
        if (string.IsNullOrWhiteSpace(CaptureModel))
        {
            CaptureStatus = "请先输入设备型号（如 AR1220）";
            return;
        }

        IsCapturing = true;
        var mainWindow = System.Windows.Application.Current.MainWindow;

        try
        {
            // Minimize ENSP.ZD so user can see eNSP and position cursor
            if (mainWindow != null)
                mainWindow.WindowState = System.Windows.WindowState.Minimized;

            for (int i = 5; i >= 1; i--)
            {
                CaptureCountdown = i;
                CaptureStatus = $"⏳ {i} 秒后截取 — 请将鼠标移到 eNSP 设备图标上...";
                await Task.Delay(1000);
            }

            using var bmp = _imageRec.CaptureCursorRegion(48);

            // Restore window before saving
            if (mainWindow != null)
                mainWindow.WindowState = System.Windows.WindowState.Normal;

            var path = Path.Combine(_iconService.TemplatesDir, $"{CaptureModel}.png");
            ImageRecognitionService.SaveTemplate(bmp, path);

            // Clear caches so the new template takes effect immediately
            _imageRec.ClearCache();
            _iconService.InvalidateCache(CaptureModel);

            CaptureStatus = $"✓ 已保存: {path}";
        }
        catch (Exception ex)
        {
            CaptureStatus = $"✗ 截图失败: {ex.Message}";
            if (mainWindow != null && mainWindow.WindowState == System.Windows.WindowState.Minimized)
                mainWindow.WindowState = System.Windows.WindowState.Normal;
        }
        finally
        {
            IsCapturing = false;
            CaptureCountdown = 0;
        }
    }
}
