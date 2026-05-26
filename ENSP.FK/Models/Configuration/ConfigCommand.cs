namespace ENSP.ZD.Models.Configuration;

public class ConfigCommand
{
    public string Command { get; set; } = string.Empty;
    public int IndentLevel { get; set; } = 0;

    public string Rendered => new string(' ', IndentLevel * 2) + Command;
}
