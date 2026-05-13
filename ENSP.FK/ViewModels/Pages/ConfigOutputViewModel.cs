using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.FK.Models;
using ENSP.FK.Models.Configuration;
using ENSP.FK.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.FK.ViewModels.Pages;

public partial class ConfigOutputViewModel : ObservableObject, INavigationAware
{
    private readonly AIConfigGenerator _aiGenerator;
    private readonly ConfigurationGenerator _fallbackGenerator;
    private readonly ConfigExporter _exporter;
    private readonly ProjectSession _session;

    [ObservableProperty]
    private ObservableCollection<DeviceConfig> _deviceConfigs = new();

    [ObservableProperty]
    private DeviceConfig? _selectedDeviceConfig;

    [ObservableProperty]
    private string _configText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "添加需求后点击生成配置";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private ObservableCollection<ChatMessage> _chatMessages = new();

    [ObservableProperty]
    private string _elapsedTime = string.Empty;

    private Stopwatch _elapsedSw = new();
    private DispatcherTimer? _elapsedTimer;

    public ConfigOutputViewModel(
        AIConfigGenerator aiGenerator,
        ConfigurationGenerator fallbackGenerator,
        ConfigExporter exporter,
        ProjectSession session)
    {
        _aiGenerator = aiGenerator;
        _fallbackGenerator = fallbackGenerator;
        _exporter = exporter;
        _session = session;
    }

    public Task OnNavigatedToAsync()
    {
        try
        {
            if (_session.Configs.Count > 0)
            {
                DeviceConfigs = new ObservableCollection<DeviceConfig>(_session.Configs);
                ShowAllConfigs();
                StatusMessage = $"已加载 {_session.Configs.Count} 台设备配置";
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ConfigOutput OnNavigatedTo error: {ex}");
        }
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task GenerateConfigs()
    {
        if (_session.Topology == null)
        {
            StatusMessage = "未加载拓扑，请先导入 .topo 文件";
            return;
        }

        if (_session.Requirements.Count == 0 && string.IsNullOrWhiteSpace(_session.RawRequirementText))
        {
            StatusMessage = "未定义需求，请先添加任务需求或粘贴文本描述";
            return;
        }

        try
        {
            IsGenerating = true;
            ChatMessages.Clear();
            StartElapsedTimer();
            StatusMessage = "正在通过 AI 生成配置...";

            AddChatMessage("status", "正在测试 API 连通性...");

            var (reachable, latency, err) = await _aiGenerator.TestConnectivityAsync();

            if (!reachable)
            {
                AddChatMessage("status", $"✗ 连通性测试失败 ({latency}ms): {err}");
                AddChatMessage("status", "⚠ 回退到模板引擎...");
                _session.Configs = _fallbackGenerator.Generate(_session.Topology, _session.Requirements);
                var elapsed = StopElapsedTimer();
                AddChatMessage("status", $"✓ 已用模板引擎为 {_session.Configs.Count} 台设备生成配置（耗时 {elapsed}）");
                OnConfigsReady("模板");
                return;
            }

            AddChatMessage("status", $"✓ 连通性测试通过 ({latency}ms) — 正在发送请求...");

            var aiConfigs = await _aiGenerator.GenerateAsync(_session.Topology, _session.Requirements, _session.RawRequirementText);

            if (aiConfigs != null && aiConfigs.Count > 0)
            {
                _session.Configs = aiConfigs;

                // Full AI conversation trace
                AddChatMessage("status", "━━━ System Prompt ━━━");
                AddChatMessage("ai", _aiGenerator.LastSystemPrompt);

                AddChatMessage("status", "━━━ User Prompt ━━━");
                AddChatMessage("ai", _aiGenerator.LastUserPrompt);

                AddChatMessage("status", "━━━ AI 原始响应 ━━━");
                AddChatMessage("ai", _aiGenerator.LastRawResponse);

                var elapsed = StopElapsedTimer();
                AddChatMessage("status", $"✓ AI 已为 {aiConfigs.Count} 台设备生成配置（耗时 {elapsed}）");
                OnConfigsReady("AI");
            }
            else
            {
                var elapsed = StopElapsedTimer();
                AddChatMessage("status", $"⚠ AI 生成失败 — {_aiGenerator.LastError}，回退到模板引擎...");
                _session.Configs = _fallbackGenerator.Generate(_session.Topology, _session.Requirements);
                AddChatMessage("status", $"✓ 已用模板引擎为 {_session.Configs.Count} 台设备生成配置（耗时 {elapsed}）");
                OnConfigsReady("模板");
            }
        }
        catch (Exception ex)
        {
            AddChatMessage("status", $"✗ 生成过程异常: {ex.Message}");
            StatusMessage = $"生成失败: {ex.Message}";
        }
        finally
        {
            if (_elapsedSw.IsRunning)
                StopElapsedTimer();
        }
    }

    private void AddChatMessage(string role, string content)
    {
        ChatMessages.Add(new ChatMessage { Role = role, Content = content });
    }

    private void StartElapsedTimer()
    {
        _elapsedSw.Restart();
        ElapsedTime = "0:00";

        _elapsedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnElapsedTick, Application.Current.Dispatcher);
        _elapsedTimer.Start();
    }

    private string StopElapsedTimer()
    {
        _elapsedSw.Stop();
        _elapsedTimer?.Stop();
        _elapsedTimer = null;
        IsGenerating = false;
        var result = FormatElapsed(_elapsedSw.Elapsed);
        ElapsedTime = string.Empty;
        return result;
    }

    private void OnElapsedTick(object? sender, EventArgs e)
    {
        ElapsedTime = FormatElapsed(_elapsedSw.Elapsed);
    }

    private static string FormatElapsed(TimeSpan ts)
        => ts.TotalSeconds < 60
            ? $"{ts.Seconds} 秒"
            : $"{(int)ts.TotalMinutes} 分 {ts.Seconds} 秒";

    private void OnConfigsReady(string source)
    {
        DeviceConfigs = new ObservableCollection<DeviceConfig>(_session.Configs);
        // Default to first device's config (per-device view)
        if (DeviceConfigs.Count > 0)
            SelectedDeviceConfig = DeviceConfigs[0];
        StatusMessage = $"({source}) 已为 {_session.Configs.Count} 台设备生成配置";
    }

    partial void OnSelectedDeviceConfigChanged(DeviceConfig? value)
    {
        if (value == null) return;
        ConfigText = value.RenderAll();
    }

    [RelayCommand]
    private void ShowAllConfigs()
    {
        if (_session.Configs.Count == 0) return;

        ConfigText = _exporter.RenderAllConfigs(_session.Configs);
        SelectedDeviceConfig = null;
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (string.IsNullOrEmpty(ConfigText)) return;

        Clipboard.SetText(ConfigText);
        StatusMessage = "配置已复制到剪贴板";
    }

    private static string GetOutputDir()
    {
        // Walk up from bin/Debug/net10.0-windows to project root
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 5 && dir != null; i++)
        {
            if (File.Exists(Path.Combine(dir, "ENSP.FK.csproj")))
                return Path.Combine(dir, "配置输出");
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: next to the exe
        return Path.Combine(AppContext.BaseDirectory, "配置输出");
    }

    [RelayCommand]
    private void ExportToFiles()
    {
        if (_session.Configs.Count == 0) return;

        var outputDir = GetOutputDir();
        _exporter.ExportAll(_session.Configs, outputDir);
        StatusMessage = $"已导出 {_session.Configs.Count} 个配置文件到 {outputDir}";
    }

    [RelayCommand]
    private void ClearCache()
    {
        _session.Configs.Clear();
        DeviceConfigs.Clear();
        ChatMessages.Clear();
        ConfigText = string.Empty;
        SelectedDeviceConfig = null;
        StatusMessage = "已清除所有配置缓存";
    }
}
