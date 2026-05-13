namespace ENSP.FK.Models.Requirements;

public class AclRequirement : TaskRequirement
{
    public override string RequirementType => "ACL";

    public int AclNumber { get; set; }
    public List<AclRule> Rules { get; set; } = new();
}

public class AclRule
{
    public string Action { get; set; } = "permit"; // permit or deny
    public string Protocol { get; set; } = "ip";   // ip, tcp, udp, icmp
    public string SourceIp { get; set; } = "any";
    public string SourceWildcard { get; set; } = string.Empty;
    public string DestIp { get; set; } = "any";
    public string DestWildcard { get; set; } = string.Empty;
    public string PortOperator { get; set; } = string.Empty; // eq, gt, lt, range
    public string Port { get; set; } = string.Empty;
}
