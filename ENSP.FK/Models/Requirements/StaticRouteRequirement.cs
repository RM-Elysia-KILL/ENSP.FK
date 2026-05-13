namespace ENSP.FK.Models.Requirements;

public class StaticRouteRequirement : TaskRequirement
{
    public override string RequirementType => "静态路由";

    public string DestinationNetwork { get; set; } = string.Empty;
    public string SubnetMask { get; set; } = string.Empty;
    public string NextHop { get; set; } = string.Empty;
    public string OutInterface { get; set; } = string.Empty;
}
