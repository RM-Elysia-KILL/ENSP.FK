namespace ENSP.ZD.Models.Configuration;

public class ConfigSection
{
    public string Title { get; set; } = string.Empty;
    public List<ConfigCommand> Commands { get; set; } = new();
}
