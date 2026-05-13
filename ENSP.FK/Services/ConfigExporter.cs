using ENSP.FK.Models.Configuration;
using System.IO;
using System.Text;

namespace ENSP.FK.Services;

public class ConfigExporter
{
    public void ExportAll(List<DeviceConfig> configs, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        foreach (var config in configs)
        {
            var fileName = GetUniqueFileName(outputDir, SanitizeFileName(config.DeviceName));
            var filePath = Path.Combine(outputDir, fileName);
            File.WriteAllText(filePath, config.RenderAll(), Encoding.UTF8);
        }
    }

    public string ExportSingle(DeviceConfig config, string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        var fileName = GetUniqueFileName(outputDir, SanitizeFileName(config.DeviceName));
        var filePath = Path.Combine(outputDir, fileName);
        File.WriteAllText(filePath, config.RenderAll(), Encoding.UTF8);
        return filePath;
    }

    private static string GetUniqueFileName(string dir, string baseName)
    {
        var fileName = $"{baseName}.cfg";
        var filePath = Path.Combine(dir, fileName);
        if (!File.Exists(filePath))
            return fileName;

        for (int i = 2; i < 100; i++)
        {
            fileName = $"{baseName}_{i}.cfg";
            filePath = Path.Combine(dir, fileName);
            if (!File.Exists(filePath))
                return fileName;
        }
        return $"{baseName}_{Guid.NewGuid():N}.cfg";
    }

    public string RenderAllConfigs(List<DeviceConfig> configs)
    {
        var sb = new StringBuilder();
        foreach (var config in configs)
        {
            sb.AppendLine($"{"=".PadRight(60, '=')}");
            sb.AppendLine($"  Device: {config.DeviceName}");
            sb.AppendLine($"{"=".PadRight(60, '=')}");
            sb.AppendLine();
            sb.AppendLine(config.RenderAll());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
