using System.IO;
using System.Text.Json;

namespace ENSP.FK.Models;

public class ApiConfig
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ENSP.FK");

    public static readonly string ConfigPath = Path.Combine(ConfigDir, "apiconfig.json");

    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = "deepseek-v4-pro";
    public string EnspPath { get; set; } = @"C:\Program Files\Huawei\eNSP";

    public string ChatCompletionsUrl => $"{BaseUrl.TrimEnd('/')}/v1/chat/completions";

    public ApiConfig()
    {
        Load();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return;

            var json = File.ReadAllText(ConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("BaseUrl", out var v) && v.ValueKind == JsonValueKind.String)
                BaseUrl = v.GetString()!;
            if (root.TryGetProperty("ApiKey", out v) && v.ValueKind == JsonValueKind.String)
                ApiKey = v.GetString()!;
            if (root.TryGetProperty("ModelName", out v) && v.ValueKind == JsonValueKind.String)
                ModelName = v.GetString()!;
            if (root.TryGetProperty("EnspPath", out v) && v.ValueKind == JsonValueKind.String)
                EnspPath = v.GetString()!;
        }
        catch
        {
            // Corrupt config — ignore and use defaults
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // Best-effort save — don't crash
        }
    }
}
