namespace ENSP.ZD.Models.Topology;

public class Topology
{
    public List<Device> Devices { get; set; } = new();
    public List<TopologyLink> Links { get; set; } = new();
}
