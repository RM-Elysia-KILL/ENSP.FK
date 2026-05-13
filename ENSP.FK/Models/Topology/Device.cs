namespace ENSP.FK.Models.Topology;

public class Device
{
    public string Name { get; set; } = string.Empty;
    public DeviceType Type { get; set; }
    public int ConsolePort { get; set; }
    public List<DeviceInterface> Interfaces { get; set; } = new();
}
