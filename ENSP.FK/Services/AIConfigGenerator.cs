using ENSP.FK.Models;
using ENSP.FK.Models.Configuration;
using ENSP.FK.Models.Requirements;
using ENSP.FK.Models.Topology;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ENSP.FK.Services;

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
        _http.Timeout = TimeSpan.FromMinutes(10);
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
            var systemPrompt = BuildSystemPrompt();
            var userPrompt = BuildUserPrompt(topology, requirements, rawRequirementText);

            LastSystemPrompt = systemPrompt;
            LastUserPrompt = userPrompt;

            var requestBody = new
            {
                model = _apiConfig.ModelName,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                response_format = new { type = "json_object" },
                temperature = 0.3
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

            // Parse: choices[0].message.content → JSON string
            var content = json.GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrEmpty(content))
                return null;

            LastRawResponse = content;

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

    private static string BuildVerificationSystemPrompt()
    {
        return """
        你是华为网络配置审核专家。你的唯一任务是：逐条对比原始需求和已生成的CLI配置，检查配置是否完全满足需求。

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

    private static string BuildSystemPrompt()
    {
        return """
        你是华为网络工程师，精通华为路由器、交换机、防火墙的 CLI 配置命令。
        你需要根据用户提供的网络拓扑和任务需求，为每台设备生成完整的 CLI 配置脚本。

        规则：
        1. 输出必须是严格的 JSON 格式。
        2. 配置命令必须遵循华为 VRP 系统语法。
        3. 每条命令包含 command（命令文本）和 indentLevel（缩进层级，0为顶格）。
        4. 配置段包含 title（段标题）和 commands（命令列表）。
        5. 每台设备按 System → Interface → VLAN → OSPF → Static Route → ACL → Return 的顺序组织。
        6. 使用 system-view 开头，return 结尾。
        7. 接口命名使用完整格式，如 GigabitEthernet0/0/1。

        JSON 结构示例：
        {
          "devices": [
            {
              "deviceName": "R1",
              "sections": [
                {
                  "title": "System",
                  "commands": [
                    {"command": "system-view", "indentLevel": 0},
                    {"command": "sysname R1", "indentLevel": 0}
                  ]
                },
                {
                  "title": "Return",
                  "commands": [
                    {"command": "return", "indentLevel": 0}
                  ]
                }
              ]
            }
          ]
        }
        """;
    }

    private static string BuildUserPrompt(Topology topology, List<TaskRequirement> requirements, string rawRequirementText)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(rawRequirementText))
        {
            sb.AppendLine("=== 用户需求描述 ===");
            sb.AppendLine(rawRequirementText);
            sb.AppendLine();
        }

        sb.AppendLine("=== 网络拓扑 ===");
        sb.AppendLine();

        foreach (var dev in topology.Devices)
        {
            sb.AppendLine($"设备: {dev.Name} (类型: {TranslateDeviceType(dev.Type)})");
            if (dev.Interfaces.Count > 0)
            {
                sb.AppendLine("  接口:");
                foreach (var iface in dev.Interfaces)
                {
                    sb.Append($"    - {iface.Name}");
                    if (!string.IsNullOrEmpty(iface.IpAddress))
                        sb.Append($"  IP: {iface.IpAddress}/{iface.SubnetMask}");
                    if (!string.IsNullOrEmpty(iface.ConnectedToDevice))
                        sb.Append($"  连接: {iface.ConnectedToDevice}:{iface.ConnectedToInterface}");
                    sb.AppendLine();
                }
            }
            sb.AppendLine();
        }

        if (topology.Links.Count > 0)
        {
            sb.AppendLine("链路:");
            foreach (var link in topology.Links)
                sb.AppendLine($"  {link.DeviceA}:{link.InterfaceA} ↔ {link.DeviceB}:{link.InterfaceB}");
            sb.AppendLine();
        }

        sb.AppendLine("=== 任务需求 ===");
        sb.AppendLine();

        var reqsByDevice = requirements.GroupBy(r => r.DeviceName);
        foreach (var group in reqsByDevice)
        {
            sb.AppendLine($"-- {group.Key} --");
            foreach (var req in group)
            {
                sb.AppendLine($"  类型: {req.RequirementType}");
                switch (req)
                {
                    case InterfaceIpRequirement ip:
                        sb.AppendLine($"  接口: {ip.InterfaceName}, IP: {ip.IpAddress}, 掩码: {ip.SubnetMask}");
                        break;
                    case VlanRequirement vlan:
                        sb.AppendLine($"  VLAN {vlan.VlanId} (名称: {vlan.VlanName})");
                        if (vlan.AccessPorts.Count > 0)
                            sb.AppendLine($"  Access端口: {string.Join(", ", vlan.AccessPorts)}");
                        if (vlan.TrunkPorts.Count > 0)
                            sb.AppendLine($"  Trunk端口: {string.Join(", ", vlan.TrunkPorts)}");
                        break;
                    case OspfRequirement ospf:
                        sb.AppendLine($"  进程ID: {ospf.ProcessId}, RouterID: {ospf.RouterId}");
                        foreach (var area in ospf.Areas)
                            sb.AppendLine($"  Area {area.AreaId}: {string.Join(", ", area.Networks)}");
                        break;
                    case StaticRouteRequirement route:
                        sb.AppendLine($"  目标: {route.DestinationNetwork}/{route.SubnetMask}, 下一跳: {route.NextHop}, 出接口: {route.OutInterface}");
                        break;
                    case AclRequirement acl:
                        sb.AppendLine($"  ACL {acl.AclNumber}: {acl.Rules.Count} 条规则");
                        foreach (var rule in acl.Rules)
                            sb.AppendLine($"    {rule.Action} {rule.Protocol} from {rule.SourceIp} to {rule.DestIp}");
                        break;
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string TranslateDeviceType(DeviceType type) => type switch
    {
        DeviceType.Router => "路由器",
        DeviceType.Switch => "交换机",
        DeviceType.Firewall => "防火墙",
        _ => "未知"
    };

    private static List<DeviceConfig>? ParseAiResponse(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        // Support both { "devices": [...] } and direct array
        JsonElement devicesArray;
        if (root.TryGetProperty("devices", out var prop))
            devicesArray = prop;
        else
            return null;

        var configs = new List<DeviceConfig>();

        foreach (var devEl in devicesArray.EnumerateArray())
        {
            var config = new DeviceConfig
            {
                DeviceName = devEl.GetProperty("deviceName").GetString() ?? "Unknown"
            };

            if (devEl.TryGetProperty("sections", out var sectionsEl))
            {
                foreach (var secEl in sectionsEl.EnumerateArray())
                {
                    var section = new ConfigSection
                    {
                        Title = secEl.GetProperty("title").GetString() ?? string.Empty
                    };

                    if (secEl.TryGetProperty("commands", out var cmdsEl))
                    {
                        foreach (var cmdEl in cmdsEl.EnumerateArray())
                        {
                            section.Commands.Add(new ConfigCommand
                            {
                                Command = cmdEl.GetProperty("command").GetString() ?? string.Empty,
                                IndentLevel = cmdEl.TryGetProperty("indentLevel", out var indent)
                                    ? indent.GetInt32() : 0
                            });
                        }
                    }

                    config.Sections.Add(section);
                }
            }

            configs.Add(config);
        }

        return configs.Count > 0 ? configs : null;
    }
}
