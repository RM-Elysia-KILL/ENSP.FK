namespace ENSP.FK.Models.Requirements;

public class InterfaceIpRequirement : TaskRequirement
{
    public override string RequirementType => "接口 IP";

    public string InterfaceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string SubnetMask { get; set; } = string.Empty;
}
