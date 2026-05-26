using ENSP.ZD.Models;
using ENSP.ZD.Models.Configuration;
using ENSP.ZD.Models.Requirements;
using ENSP.ZD.Models.Topology;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ENSP.ZD.Services;

public class AIConfigGenerator
{
    private readonly ApiConfig _apiConfig;
    private readonly HttpClient _http;

    public string LastSystemPrompt { get; private set; } = string.Empty;
    public string LastUserPrompt { get; private set; } = string.Empty;
    public string LastRawResponse { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;

    public AIConfigGenerator(ApiConfig apiConfig)
    {
        _apiConfig = apiConfig;
        _http = new HttpClient();
        _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan; // streaming, no timeout
    }

    /// <summary>
    /// Sends a minimal request to verify API connectivity and key validity.
    /// Returns (isReachable, latency, errorMessage).
    /// </summary>
    public async Task<(bool Reachable, long LatencyMs, string Error)> TestConnectivityAsync()
    {
        if (string.IsNullOrEmpty(_apiConfig.ApiKey))
            return (false, 0, "未配置 API Key — 请在设置中填写");

        var requestBody = new
        {
            model = _apiConfig.ModelName,
            messages = new[]
            {
                new { role = "user", content = "ping" }
            },
            max_tokens = 1,
            temperature = 0
        };

        var request = new HttpRequestMessage(HttpMethod.Post, _apiConfig.ChatCompletionsUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json")
        };
        request.Headers.Add("Authorization", $"Bearer {_apiConfig.ApiKey}");

        var sw = Stopwatch.StartNew();
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var response = await _http.SendAsync(request, cts.Token);
            sw.Stop();

            if (response.IsSuccessStatusCode)
                return (true, sw.ElapsedMilliseconds, string.Empty);

            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return (false, sw.ElapsedMilliseconds, "API Key 无效 (401)");
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return (false, sw.ElapsedMilliseconds, "API Key 无权限 (403)");

            return (false, sw.ElapsedMilliseconds, $"API 返回 {response.StatusCode}: {Truncate(body)}");
        }
        catch (TaskCanceledException)
        {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds, "连接超时 (15s) — 请检查网络和 API 地址");
        }
        catch (HttpRequestException ex)
        {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds, $"网络错误: {ex.Message}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds, $"连接失败: {ex.Message}");
        }
    }

    private static string Truncate(string s, int maxLen = 120)
        => s.Length <= maxLen ? s : s[..maxLen] + "...";

    public async Task<List<DeviceConfig>?> GenerateAsync(Topology topology, List<TaskRequirement> requirements, string rawRequirementText)
    {
        if (string.IsNullOrEmpty(_apiConfig.ApiKey))
            return null;

        try
        {
            var systemPrompt = BuildSystemPrompt(topology.Devices.Count);
            var userPrompt = BuildUserPrompt(topology, requirements, rawRequirementText);

            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;

            SaveDebugPrompt(systemPrompt, userPrompt);

            var requestBody = new
            {
                model = _apiConfig.ModelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.3,
                max_tokens = 65536,
                stream = true
            };

            var request = new HttpRequestMessage(HttpMethod.Post, _apiConfig.ChatCompletionsUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_apiConfig.ApiKey}");

            var httpResponse = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            httpResponse.EnsureSuccessStatusCode();

            var content = await ReadSseStreamAsync(httpResponse);

            if (string.IsNullOrWhiteSpace(content))
            {
                LastError = "AI 返回了空内容，请检查 API 额度或简化需求后重试";
                return null;
            }

            LastRawResponse = content;
            SaveDebugResponse(content);

            return ParseAiResponse(content);
        }
        catch (TaskCanceledException)
        {
            LastError = "请求超时 — AI 服务响应时间过长";
            return null;
        }
        catch (HttpRequestException ex)
        {
            LastError = $"网络错误: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"AI 生成失败: {ex.Message}";
            return null;
        }
    }

    public async Task<string?> VerifyAsync(
        Topology topology,
        List<TaskRequirement> requirements,
        string rawRequirementText,
        string renderedConfigs)
    {
        if (string.IsNullOrEmpty(_apiConfig.ApiKey))
            return null;

        try
        {
            var systemPrompt = BuildVerificationSystemPrompt();
            var userPrompt = BuildVerificationUserPrompt(topology, requirements, rawRequirementText, renderedConfigs);

            var requestBody = new
            {
                model = _apiConfig.ModelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1
            };

            var request = new HttpRequestMessage(HttpMethod.Post, _apiConfig.ChatCompletionsUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_apiConfig.ApiKey}");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var content = json.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (TaskCanceledException)
        {
            LastError = "验证请求超时";
            return null;
        }
        catch (HttpRequestException ex)
        {
            LastError = $"验证网络错误: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"验证失败: {ex.Message}";
            return null;
        }
    }

    public async Task<List<DeviceConfig>?> FixConfigsAsync(
        Topology topology,
        List<TaskRequirement> requirements,
        string rawRequirementText,
        string renderedConfigs,
        string verificationResult)
    {
        if (string.IsNullOrEmpty(_apiConfig.ApiKey))
            return null;

        try
        {
            var systemPrompt = """
                你是华为 eNSP 模拟器中的网络配置修正专家。你会收到有问题的配置和验证报告，根据报告中指出的问题逐条修正配置。

                修正规则：
                1. 只修正验证报告中指出的问题，不要改动验证通过的配置
                2. 修正后输出完整的配置（含正确部分），格式与原始配置完全一致
                3. 保持 # ===== DEVICE ===== 格式，每段第一条命令必须是 system-view
                4. 严格使用华为 VRP 语法，严禁 Cisco/Juniper 命令
                5. 接口命名: 千兆用 GigabitEthernet0/0/0 或简写 g 0/0/0，百兆用 Ethernet0/0/0 或简写 e 0/0/0
                   严禁 GE0/0/0 / GE 0/0/0 / ge0/0/0 / eth0/0/0 / Fa0/0/0 等格式！

                输出格式（严格按照此格式，一台设备一个段）:

                # ===== R1 =====
                system-view
                sysname R1
                ...

                # ===== R2 =====
                system-view
                sysname R2
                ...
                """;

            var userPrompt = $"""
                验证报告指出了以下问题，请修正配置：

                === 验证报告 ===
                {verificationResult}

                === 当前配置（需要修正） ===
                {renderedConfigs}

                请输出修正后的完整配置。
                """;

            var requestBody = new
            {
                model = _apiConfig.ModelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = 0.1
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _apiConfig.ChatCompletionsUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_apiConfig.ApiKey}");

            var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var content = json.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            LastRawResponse = content ?? string.Empty;
            return ParseAiResponse(content ?? string.Empty);
        }
        catch (TaskCanceledException)
        {
            LastError = "修正请求超时";
            return null;
        }
        catch (HttpRequestException ex)
        {
            LastError = $"修正网络错误: {ex.Message}";
            return null;
        }
        catch (Exception ex)
        {
            LastError = $"修正失败: {ex.Message}";
            return null;
        }
    }

    private static string BuildVerificationSystemPrompt()
    {
        return """
        你是华为 eNSP 模拟器中的网络配置审核专家。你的唯一任务是：逐条对比原始需求和已生成的CLI配置，检查配置是否完全满足需求。

        审核规则：
        1. 逐条检查每个任务需求（接口IP、VLAN、OSPF、静态路由、ACL等）是否在配置中有对应的正确命令。
        2. 验证所有参数是否精确匹配：IP地址、子网掩码、VLAN ID、接口名称、进程ID、Router ID、Area号、下一跳地址等。
        3. 检查是否有遗漏的需求（需求中提到但配置中缺失）。
        4. 检查是否有错误配置（参数不匹配、错误命令等）。
        5. 检查华为VRP命令语法是否正确。
        6. 如全部正确，在总体结论中写明"✓ 验证通过"。
        7. 如有问题，在总体结论中写明"✗ 存在问题"并列出所有具体问题。

        请严格按照以下格式回复（使用中文）：

        验证通过的项：
        - （列出每项正确实现的需求，如全部正确则写"全部需求均已正确实现"）

        存在的问题：
        - （逐条列出问题，描述问题所在设备和具体差异；如无问题则写"无"）

        总体结论：
        （一句话总结，以"✓ 验证通过"或"✗ 存在问题"开头）
        """;
    }

    private static string BuildVerificationUserPrompt(
        Topology topology,
        List<TaskRequirement> requirements,
        string rawRequirementText,
        string renderedConfigs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("请验证以下生成的配置是否完全满足原始需求。");
        sb.AppendLine();
        sb.AppendLine("=== 原始需求 ===");
        sb.AppendLine();
        sb.Append(BuildUserPrompt(topology, requirements, rawRequirementText));
        sb.AppendLine("=== 生成的配置 ===");
        sb.AppendLine(renderedConfigs);
        return sb.ToString();
    }

    private static string? _cachedSystemPrompt;
    private static readonly object _promptLock = new();

    private static string BuildSystemPrompt(int deviceCount)
    {
        if (_cachedSystemPrompt == null)
        {
            lock (_promptLock)
            {
                if (_cachedSystemPrompt == null)
                {
                    var assembly = typeof(AIConfigGenerator).Assembly;
                    using var stream = assembly.GetManifestResourceStream("ENSP.ZD.Prompts.ConfigGenerator.md");
                    if (stream == null)
                        throw new InvalidOperationException("Embedded resource 'Prompts/ConfigGenerator.md' not found. Ensure it is marked as EmbeddedResource in .csproj.");
                    using var reader = new StreamReader(stream);
                    _cachedSystemPrompt = reader.ReadToEnd();
                }
            }
        }

        return _cachedSystemPrompt.Replace("{deviceCount}", deviceCount.ToString());
    }

    private static string BuildUserPrompt(Topology topology, List<TaskRequirement> requirements, string rawRequirementText)
    {
        var sb = new StringBuilder();

        // List target devices (exclude terminal devices like PC/Server)
        var targetDevices = topology.Devices
            .Where(d => !IsTerminalDevice(d))
            .Select(d => d.Name)
            .ToList();

        sb.AppendLine($"=== 需要配置的设备（共 {targetDevices.Count} 台，每台都必须生成，一台都不能漏） ===");
        for (int i = 0; i < targetDevices.Count; i++)
            sb.AppendLine($"  {i + 1}. {targetDevices[i]}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(rawRequirementText))
        {
            sb.AppendLine("=== 用户需求 ===");
            sb.AppendLine(rawRequirementText);
            sb.AppendLine();
        }

        sb.AppendLine("=== 拓扑设备列表 ===");
        foreach (var dev in topology.Devices)
        {
            var modelStr = string.IsNullOrEmpty(dev.Model) ? "" : $" 型号:{dev.Model}";
            sb.AppendLine($"  {dev.Name} 类型:{TranslateDeviceType(dev.Type)}{modelStr} COM端口:{dev.ConsolePort}");
            if (dev.Interfaces.Count > 0)
            {
                foreach (var iface in dev.Interfaces)
                {
                    var ipStr = string.IsNullOrEmpty(iface.IpAddress) ? "" : $" IP:{iface.IpAddress}/{iface.SubnetMask}";
                    var connStr = string.IsNullOrEmpty(iface.ConnectedToDevice) ? "" : $" → {iface.ConnectedToDevice}:{iface.ConnectedToInterface}";
                    sb.AppendLine($"    {iface.Name}{ipStr}{connStr}");
                }
            }
        }
        sb.AppendLine();

        if (topology.Links.Count > 0)
        {
            sb.AppendLine("=== 拓扑连接关系 ===");
            foreach (var link in topology.Links)
                sb.AppendLine($"  {link.DeviceA}:{link.InterfaceA} ←→ {link.DeviceB}:{link.InterfaceB}");
            sb.AppendLine();
        }

        if (requirements.Count > 0)
        {
            sb.AppendLine("=== 任务需求 ===");
            var reqsByDevice = requirements.GroupBy(r => r.DeviceName);
            foreach (var group in reqsByDevice)
            {
                sb.AppendLine($"-- {group.Key} --");
                foreach (var req in group)
                {
                    switch (req)
                    {
                        case InterfaceIpRequirement ip:
                            sb.AppendLine($"  接口IP: {ip.InterfaceName} = {ip.IpAddress}/{ip.SubnetMask}");
                            break;
                        case VlanRequirement vlan:
                            sb.AppendLine($"  VLAN {vlan.VlanId} (名称: {vlan.VlanName})");
                            if (vlan.AccessPorts.Count > 0)
                                sb.AppendLine($"    Access端口: {string.Join(", ", vlan.AccessPorts)}");
                            if (vlan.TrunkPorts.Count > 0)
                                sb.AppendLine($"    Trunk端口: {string.Join(", ", vlan.TrunkPorts)}");
                            break;
                        case OspfRequirement ospf:
                            sb.AppendLine($"  OSPF 进程:{ospf.ProcessId} RouterID:{ospf.RouterId}");
                            foreach (var area in ospf.Areas)
                                sb.AppendLine($"    Area {area.AreaId}: {string.Join(", ", area.Networks)}");
                            break;
                        case StaticRouteRequirement route:
                            sb.AppendLine($"  静态路由: {route.DestinationNetwork}/{route.SubnetMask} → {route.NextHop}");
                            break;
                        case AclRequirement acl:
                            sb.AppendLine($"  ACL {acl.AclNumber}: {acl.Rules.Count} 条规则");
                            break;
                    }
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static bool IsTerminalDevice(Device d)
    {
        if (string.IsNullOrWhiteSpace(d.Model)) return false;
        string m = d.Model.ToLowerInvariant();
        return m.Contains("pc") || m.Contains("client") || m.Contains("server")
            || m.Contains("mcs") || m.Contains("cellphone") || m.Contains("sta")
            || m.Contains("laptop") || m.Contains("phone") || m.Contains("ap")
            || m.Contains("cloud");
    }

    private static string TranslateDeviceType(DeviceType type) => type switch
    {
        DeviceType.Router => "路由器",
        DeviceType.Switch => "交换机",
        DeviceType.Firewall => "防火墙",
        DeviceType.PC => "PC",
        DeviceType.Server => "服务器",
        _ => "未知"
    };

    private async Task<string> ReadSseStreamAsync(HttpResponseMessage httpResponse)
    {
        using var stream = await httpResponse.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var contentBuilder = new StringBuilder();
        var reasoningBuilder = new StringBuilder();

        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null) break;

            if (line.Length == 0) continue;
            if (!line.StartsWith("data: ")) continue;

            string data = line[6..];
            if (data == "[DONE]") break;

            try
            {
                using var doc = JsonDocument.Parse(data);
                var choices = doc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() == 0) continue;
                var delta = choices[0].GetProperty("delta");

                if (delta.TryGetProperty("content", out var c) && c.GetString() is string s)
                    contentBuilder.Append(s);

                if (delta.TryGetProperty("reasoning_content", out var r) && r.GetString() is string rs)
                    reasoningBuilder.Append(rs);
            }
            catch (JsonException) { /* skip malformed chunks */ }
        }

        if (reasoningBuilder.Length > 0)
            Debug.WriteLine($"[AIGen] Reasoning tokens: ~{reasoningBuilder.Length} chars");

        return contentBuilder.ToString();
    }

    private static void SaveDebugPrompt(string systemPrompt, string userPrompt)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ENSP.ZD");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "ai_response_debug.txt");
            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine($"=== Prompt @ {DateTime.Now:HH:mm:ss} ===");
            sb.AppendLine("========================================");
            sb.AppendLine("--- SYSTEM ---");
            sb.AppendLine(systemPrompt);
            sb.AppendLine("--- USER ---");
            sb.AppendLine(userPrompt);
            sb.AppendLine();
            File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch { /* best-effort */ }
    }

    private static void SaveDebugResponse(string response)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ENSP.ZD");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "ai_response_debug.txt");
            var sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine($"=== Response @ {DateTime.Now:HH:mm:ss} ===");
            sb.AppendLine("========================================");
            sb.AppendLine(response);
            sb.AppendLine();
            File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch { /* best-effort */ }
    }

    private static List<DeviceConfig>? ParseAiResponse(string content)
    {
        // Strip markdown code blocks if present
        content = Regex.Replace(content, @"^```[a-z]*\s*$", "", RegexOptions.Multiline);

        // Normalize interface names: "GE 0/0/0" → "GigabitEthernet0/0/0", etc.
        content = NormalizeInterfaceNames(content);

        var configs = new List<DeviceConfig>();

        // Split by device headers: # ===== NAME =====
        var matches = Regex.Matches(content, @"^# ===== (.+?) =====\s*$", RegexOptions.Multiline);

        for (int i = 0; i < matches.Count; i++)
        {
            var deviceName = matches[i].Groups[1].Value.Trim();
            var startIdx = matches[i].Index + matches[i].Length;
            var endIdx = i + 1 < matches.Count ? matches[i + 1].Index : content.Length;
            var deviceBlock = content[startIdx..endIdx].Trim();

            var config = new DeviceConfig { DeviceName = deviceName };
            var lines = deviceBlock.Split('\n')
                .Select(l => l.TrimEnd('\r', '\n'))
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            // Split lines into sections by VRP block type
            var sections = SplitIntoSections(lines);
            foreach (var (title, sectionLines) in sections)
            {
                var section = new ConfigSection { Title = title };
                foreach (var line in sectionLines)
                {
                    var leadingSpaces = line.Length - line.TrimStart().Length;
                    section.Commands.Add(new ConfigCommand
                    {
                        Command = line.Trim(),
                        IndentLevel = leadingSpaces / 2
                    });
                }
                config.Sections.Add(section);
            }

            configs.Add(config);
        }

        return configs.Count > 0 ? configs : null;
    }

    private static List<(string Title, List<string> Lines)> SplitIntoSections(List<string> lines)
    {
        var sections = new List<(string Title, List<string> Lines)>();
        if (lines.Count == 0) return sections;

        string? currentTitle = null;
        var currentLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            var detectedTitle = DetectSectionTitle(trimmed);

            if (detectedTitle != null && currentTitle != detectedTitle && currentLines.Count > 0)
            {
                // Save current section (use fallback title if still unknown)
                sections.Add((currentTitle ?? "系统配置", currentLines.ToList()));
                currentTitle = detectedTitle;
                currentLines.Clear();
            }
            else if (detectedTitle != null && currentTitle == null)
            {
                currentTitle = detectedTitle;
            }

            currentLines.Add(line);
        }

        // Save final section
        if (currentLines.Count > 0)
            sections.Add((currentTitle ?? "系统配置", currentLines.ToList()));

        return sections;
    }

    private static string? DetectSectionTitle(string line)
    {
        if (line.StartsWith("system-view", StringComparison.OrdinalIgnoreCase)) return null; // keep in current section
        if (line.StartsWith("sysname ", StringComparison.OrdinalIgnoreCase)) return "系统配置";
        if (line.StartsWith("interface ", StringComparison.OrdinalIgnoreCase)) return "接口配置";
        if (line.StartsWith("vlan ", StringComparison.OrdinalIgnoreCase) && !line.Contains("if", StringComparison.OrdinalIgnoreCase)) return "VLAN";
        if (line.StartsWith("stp ", StringComparison.OrdinalIgnoreCase)) return "STP";
        if (Regex.IsMatch(line, @"^ospf\s+\d+", RegexOptions.IgnoreCase)) return "OSPF";
        if (Regex.IsMatch(line, @"^bgp\s+\d+", RegexOptions.IgnoreCase)) return "BGP";
        if (Regex.IsMatch(line, @"^isis\s+\d+", RegexOptions.IgnoreCase)) return "IS-IS";
        if (Regex.IsMatch(line, @"^rip\s+\d+", RegexOptions.IgnoreCase)) return "RIP";
        if (line.StartsWith("ip route-static ", StringComparison.OrdinalIgnoreCase)) return "静态路由";
        if (Regex.IsMatch(line, @"^acl\s+number\s+\d+", RegexOptions.IgnoreCase)) return "ACL";
        if (line.StartsWith("user-interface vty", StringComparison.OrdinalIgnoreCase)) return "VTY";
        if (line.StartsWith("route-policy ", StringComparison.OrdinalIgnoreCase)) return "路由策略";
        if (line.StartsWith("traffic ", StringComparison.OrdinalIgnoreCase)) return "流量策略";
        if (line.StartsWith("ip pool ", StringComparison.OrdinalIgnoreCase)) return "DHCP";
        if (line.StartsWith("dhcp ", StringComparison.OrdinalIgnoreCase)) return "DHCP";
        if (line.StartsWith("#")) return null; // separator, skip
        if (line.StartsWith("!")) return null; // comment, skip
        return null; // keep in current section
    }

    /// <summary>
    /// Normalize interface names in AI-generated config text.
    /// Handles "GE 0/0/0", "g0/0/0", "GE0/0/0", etc. → "GigabitEthernet0/0/0"
    /// </summary>
    private static string NormalizeInterfaceNames(string content)
    {
        var lines = content.Split('\n');
        var result = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r', '\n');
            var trimmed = line.TrimStart();
            var leadingWs = line.Length - trimmed.Length;

            if (trimmed.StartsWith("interface ", StringComparison.OrdinalIgnoreCase))
            {
                var ifName = trimmed["interface ".Length..].Trim();
                var normalized = ConfigurationGenerator.NormalizeIfName(ifName);
                result.AppendLine($"{new string(' ', leadingWs)}interface {normalized}");
            }
            else
            {
                result.AppendLine(line);
            }
        }
        return result.ToString().TrimEnd('\r', '\n');
    }
}
