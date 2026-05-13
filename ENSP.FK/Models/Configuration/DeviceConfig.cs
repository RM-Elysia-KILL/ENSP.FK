namespace ENSP.FK.Models.Configuration;

public class DeviceConfig
{
    public string DeviceName { get; set; } = string.Empty;
    public List<ConfigSection> Sections { get; set; } = new();

    public string RenderAll()
    {
        var lines = new List<string>();
        foreach (var section in Sections)
        {
            lines.Add($"# {section.Title}");
            lines.AddRange(section.Commands.Select(c => c.Rendered));
            lines.Add("");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
