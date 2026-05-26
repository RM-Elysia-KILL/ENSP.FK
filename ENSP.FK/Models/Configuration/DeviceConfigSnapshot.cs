namespace ENSP.ZD.Models.Configuration;

public class DeviceConfigSnapshot
{
    public string DeviceName { get; set; } = string.Empty;
    public int ConsolePort { get; set; }
    public DateTime LastFetchTime { get; set; }
    public string RawConfig { get; set; } = string.Empty;
    public ParsedDeviceConfig? ParsedConfig { get; set; }
}
