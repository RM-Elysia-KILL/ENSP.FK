using ENSP.FK.Models;
using ENSP.FK.Services;
using Wpf.Ui.Abstractions.Controls;
using Wpf.Ui.Appearance;

namespace ENSP.FK.ViewModels.Pages;

public partial class SettingsViewModel : ObservableObject, INavigationAware
{
    private readonly ApiConfig _apiConfig;
    private readonly AIConfigGenerator _aiGenerator;

    [ObservableProperty]
    private string _appVersion = string.Empty;

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
    private string _apiStatus = string.Empty;

    [ObservableProperty]
    private bool _isTesting;

    public SettingsViewModel(ApiConfig apiConfig, AIConfigGenerator aiGenerator)
    {
        _apiConfig = apiConfig;
        _aiGenerator = aiGenerator;
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
        AppVersion = $"ENSP.FK — {GetAssemblyVersion()}";

        BaseUrl = _apiConfig.BaseUrl;
        ApiKey = _apiConfig.ApiKey;
        ModelName = _apiConfig.ModelName;
        EnspPath = _apiConfig.EnspPath;
    }

    private static string GetAssemblyVersion()
    {
        return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? string.Empty;
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
}
