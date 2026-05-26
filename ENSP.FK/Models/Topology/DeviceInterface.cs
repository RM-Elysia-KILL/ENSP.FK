namespace ENSP.ZD.Models.Topology;

public class DeviceInterface
{
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string SubnetMask { get; set; } = string.Empty;
    public string ConnectedToDevice { get; set; } = string.Empty;
    public string ConnectedToInterface { get; set; } = string.Empty;
    public int SlotIndex { get; set; }
}
