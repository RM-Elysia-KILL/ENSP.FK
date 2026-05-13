namespace ENSP.FK.Models.Requirements;

public class VlanRequirement : TaskRequirement
{
    public override string RequirementType => "VLAN";

    public int VlanId { get; set; }
    public string VlanName { get; set; } = string.Empty;
    public List<string> AccessPorts { get; set; } = new();
    public List<string> TrunkPorts { get; set; } = new();
}
