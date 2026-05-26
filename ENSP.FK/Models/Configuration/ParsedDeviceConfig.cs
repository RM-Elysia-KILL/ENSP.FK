namespace ENSP.ZD.Models.Configuration;

public class ParsedDeviceConfig
{
    // Section configs (pre-extracted blocks)
    public string GlobalConfig { get; set; } = string.Empty;
    public string StaticRouteConfig { get; set; } = string.Empty;
    public string RipConfig { get; set; } = string.Empty;
    public string OspfConfig { get; set; } = string.Empty;
    public string IsisConfig { get; set; } = string.Empty;
    public string BgpConfig { get; set; } = string.Empty;
    public string VlanConfig { get; set; } = string.Empty;

    // Interface names
    public List<string> InterfaceNames { get; set; } = new();

    // Global
    public string Hostname { get; set; } = string.Empty;

    // Tables
    public List<StaticRouteEntry> StaticRoutes { get; set; } = new();
    public List<RipNetworkEntry> RipNetworkEntries { get; set; } = new();
    public List<OspfNetworkEntry> OspfNetworkEntries { get; set; } = new();
    public List<IsisNetworkEntry> IsisNetworkEntries { get; set; } = new();
    public List<BgpNetworkEntry> BgpNetworkEntries { get; set; } = new();
    public List<BgpPeerEntry> BgpPeerEntries { get; set; } = new();
    public List<VlanEntry> Vlans { get; set; } = new();

    // RIP
    public string RipVersion { get; set; } = string.Empty;

    // OSPF
    public string OspfProcessId { get; set; } = string.Empty;
    public string OspfRouterId { get; set; } = string.Empty;
    public string OspfArea { get; set; } = string.Empty;

    // IS-IS
    public string IsisSystemId { get; set; } = string.Empty;
    public string IsisLevel { get; set; } = string.Empty;

    // BGP
    public string BgpAsNumber { get; set; } = string.Empty;
    public string BgpRouterId { get; set; } = string.Empty;
    public bool BgpImportDirect { get; set; }
    public bool BgpImportStatic { get; set; }
    public bool BgpImportRip { get; set; }
    public bool BgpImportOspf { get; set; }
    public bool BgpImportIsis { get; set; }

    // Terminal fields
    public string TerminalIfaceName { get; set; } = string.Empty;
    public string Ipv4Mode { get; set; } = string.Empty;
    public string Ipv4Address { get; set; } = string.Empty;
    public string Ipv4Mask { get; set; } = string.Empty;
    public string Ipv4Gateway { get; set; } = string.Empty;
    public bool Ipv6Enabled { get; set; }
    public string Ipv6Mode { get; set; } = string.Empty;
    public string Ipv6Address { get; set; } = string.Empty;
    public string Ipv6Prefix { get; set; } = string.Empty;
    public string Ipv6Gateway { get; set; } = string.Empty;
    public string DnsServer { get; set; } = string.Empty;

    // Firewall fields
    public string FwZoneName { get; set; } = string.Empty;
    public string FwZonePriority { get; set; } = string.Empty;
    public string FwZoneInterfaces { get; set; } = string.Empty;
    public string FwPolicyName { get; set; } = string.Empty;
    public string FwPolicySrcZone { get; set; } = string.Empty;
    public string FwPolicyDstZone { get; set; } = string.Empty;
    public string FwPolicySrcAddr { get; set; } = string.Empty;
    public string FwPolicyDstAddr { get; set; } = string.Empty;
    public string FwPolicyAction { get; set; } = string.Empty;
    public string FwNatName { get; set; } = string.Empty;
    public string FwNatSrcZone { get; set; } = string.Empty;
    public string FwNatDstZone { get; set; } = string.Empty;
    public string FwNatType { get; set; } = string.Empty;
    public string FwNatTranslatedAddr { get; set; } = string.Empty;
}
