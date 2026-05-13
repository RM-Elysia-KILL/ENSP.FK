namespace ENSP.FK.Models.Requirements;

public class OspfRequirement : TaskRequirement
{
    public override string RequirementType => "OSPF";

    public int ProcessId { get; set; } = 1;
    public string RouterId { get; set; } = string.Empty;
    public List<OspfArea> Areas { get; set; } = new();
}

public class OspfArea
{
    public string AreaId { get; set; } = "0";
    public List<string> Networks { get; set; } = new();
}
