namespace ENSP.ZD.Models.Topology;

public class TopologyLink
{
    public string DeviceA { get; set; } = string.Empty;
    public string InterfaceA { get; set; } = string.Empty;
    public string DeviceB { get; set; } = string.Empty;
    public string InterfaceB { get; set; } = string.Empty;
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
}
