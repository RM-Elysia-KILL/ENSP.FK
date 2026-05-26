namespace ENSP.ZD.Models.Topology;

public class Device
{
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DeviceType Type { get; set; }
    public int ConsolePort { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public List<DeviceInterface> Interfaces { get; set; } = new();
}
