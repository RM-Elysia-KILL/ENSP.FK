using ENSP.ZD.Models.Configuration;
using System.Text.RegularExpressions;

namespace ENSP.ZD.Services;

public static partial class DeviceConfigParser
{
    [GeneratedRegex(@"\s*-+\s*More\s*-+\s*", RegexOptions.Compiled)]
    internal static partial Regex MoreStripRegex();

    [GeneratedRegex(@"\x1B\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled)]
    internal static partial Regex AnsiStripRegex();

    /// <summary>Parse raw "display current-configuration" output into structured data.</summary>
    public static ParsedDeviceConfig Parse(string rawConfig)
    {
        var result = new ParsedDeviceConfig();
        if (string.IsNullOrWhiteSpace(rawConfig)) return result;

        string clean = AnsiStripRegex().Replace(MoreStripRegex().Replace(rawConfig, ""), "");

        // Extract sections
        result.GlobalConfig = ExtractSection(clean, IsGlobalBlock);
        result.StaticRouteConfig = ExtractSection(clean, IsStaticRouteBlock);
        result.RipConfig = ExtractSection(clean, IsRipBlock);
        result.OspfConfig = ExtractSection(clean, IsOspfBlock);
        result.IsisConfig = ExtractSection(clean, IsIsisBlock);
        result.BgpConfig = ExtractSection(clean, IsBgpBlock);
        result.VlanConfig = ExtractSection(clean, IsVlanBlock);

        // Interface names
        result.InterfaceNames = Regex.Matches(clean, @"^interface\s+(\S+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        // Hostname
        var m = Regex.Match(clean, @"^sysname\s+(.+)$", RegexOptions.Multiline);
        if (m.Success) result.Hostname = m.Groups[1].Value.Trim();

        // Static Route
        if (!string.IsNullOrEmpty(result.StaticRouteConfig))
        {
            foreach (Match sm in Regex.Matches(result.StaticRouteConfig, @"ip\s+route-static\s+(\S+)\s+(\S+)\s+(\S+)", RegexOptions.Multiline))
            {
                result.StaticRoutes.Add(new StaticRouteEntry
                {
                    Dest = sm.Groups[1].Value,
                    Mask = sm.Groups[2].Value,
                    NextHop = sm.Groups[3].Value
                });
            }
        }

        // RIP
        if (!string.IsNullOrEmpty(result.RipConfig))
        {
            m = Regex.Match(result.RipConfig, @"version\s+(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) result.RipVersion = m.Groups[1].Value;

            foreach (Match nm in Regex.Matches(result.RipConfig, @"^\s*network\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                result.RipNetworkEntries.Add(new RipNetworkEntry { Network = nm.Groups[1].Value });
        }

        // OSPF
        if (!string.IsNullOrEmpty(result.OspfConfig))
        {
            m = Regex.Match(result.OspfConfig, @"^ospf\s+(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (m.Success) result.OspfProcessId = m.Groups[1].Value;

            m = Regex.Match(result.OspfConfig, @"router-id\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success) result.OspfRouterId = m.Groups[1].Value;

            m = Regex.Match(result.OspfConfig, @"area\s+(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) result.OspfArea = m.Groups[1].Value;

            foreach (Match nm in Regex.Matches(result.OspfConfig, @"^\s*network\s+(\S+\s+\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                result.OspfNetworkEntries.Add(new OspfNetworkEntry { Network = nm.Groups[1].Value.Trim(), Area = result.OspfArea });
        }

        // IS-IS
        if (!string.IsNullOrEmpty(result.IsisConfig))
        {
            m = Regex.Match(result.IsisConfig, @"is-level\s+(level-\S+)", RegexOptions.IgnoreCase);
            if (m.Success) result.IsisLevel = m.Groups[1].Value;

            foreach (Match nm in Regex.Matches(result.IsisConfig, @"network-entity\s+(\S+)", RegexOptions.IgnoreCase))
                result.IsisNetworkEntries.Add(new IsisNetworkEntry { Network = nm.Groups[1].Value.Trim() });

            if (result.IsisNetworkEntries.Count > 0)
                result.IsisSystemId = result.IsisNetworkEntries[0].Network;
        }

        // BGP
        if (!string.IsNullOrEmpty(result.BgpConfig))
        {
            m = Regex.Match(result.BgpConfig, @"bgp\s+(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) result.BgpAsNumber = m.Groups[1].Value;
            m = Regex.Match(result.BgpConfig, @"router-id\s+(\S+)", RegexOptions.IgnoreCase);
            if (m.Success) result.BgpRouterId = m.Groups[1].Value;
            foreach (Match pm in Regex.Matches(result.BgpConfig, @"peer\s+(\S+)\s+as-number\s+(\d+)", RegexOptions.IgnoreCase))
                result.BgpPeerEntries.Add(new BgpPeerEntry { PeerIp = pm.Groups[1].Value });
            foreach (Match nm in Regex.Matches(result.BgpConfig, @"^\s*network\s+(\S+\s+\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                result.BgpNetworkEntries.Add(new BgpNetworkEntry { Network = nm.Groups[1].Value.Trim() });
            result.BgpImportDirect = Regex.IsMatch(result.BgpConfig, @"import-route\s+direct", RegexOptions.IgnoreCase);
            result.BgpImportStatic = Regex.IsMatch(result.BgpConfig, @"import-route\s+static", RegexOptions.IgnoreCase);
            result.BgpImportRip = Regex.IsMatch(result.BgpConfig, @"import-route\s+rip", RegexOptions.IgnoreCase);
            result.BgpImportOspf = Regex.IsMatch(result.BgpConfig, @"import-route\s+ospf", RegexOptions.IgnoreCase);
            result.BgpImportIsis = Regex.IsMatch(result.BgpConfig, @"import-route\s+isis", RegexOptions.IgnoreCase);
        }

        // VLAN
        if (!string.IsNullOrEmpty(result.VlanConfig))
        {
            foreach (Match vm in Regex.Matches(result.VlanConfig, @"^vlan\s+(\d+)\s*\r?\n(?:\s+name\s+(.+))?", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                result.Vlans.Add(new VlanEntry
                {
                    VlanId = vm.Groups[1].Value,
                    Name = vm.Groups[2].Success ? vm.Groups[2].Value.Trim() : string.Empty
                });
            }
        }

        // Terminal fields
        ParseTerminalFields(clean, result);

        // Firewall fields
        ParseFirewallFields(clean, result);

        return result;
    }

    private static void ParseTerminalFields(string config, ParsedDeviceConfig result)
    {
        string iface = result.InterfaceNames.FirstOrDefault() ?? "GigabitEthernet0/0/0";
        result.TerminalIfaceName = iface;

        var ifacePattern = $@"^interface\s+{Regex.Escape(iface)}\s*\r?\n(?:\s+.+\r?\n)*";
        var ifaceMatch = Regex.Match(config, ifacePattern, RegexOptions.Multiline);
        string ifaceBlock = ifaceMatch.Success ? ifaceMatch.Value : string.Empty;

        if (!string.IsNullOrEmpty(ifaceBlock))
        {
            var ipMatch = Regex.Match(ifaceBlock, @"ip\s+address\s+dhcp-alloc", RegexOptions.IgnoreCase);
            if (ipMatch.Success)
            {
                result.Ipv4Mode = "DHCP";
            }
            else
            {
                ipMatch = Regex.Match(ifaceBlock, @"ip\s+address\s+(\S+)\s+(\S+)", RegexOptions.IgnoreCase);
                if (ipMatch.Success)
                {
                    result.Ipv4Mode = "Static";
                    result.Ipv4Address = ipMatch.Groups[1].Value;
                    result.Ipv4Mask = ipMatch.Groups[2].Value;
                }
            }
        }

        var gwMatch = Regex.Match(config, @"ip\s+route-static\s+0\.0\.0\.0\s+0\.0\.0\.0\s+(\S+)", RegexOptions.IgnoreCase);
        if (gwMatch.Success) result.Ipv4Gateway = gwMatch.Groups[1].Value;

        if (!string.IsNullOrEmpty(ifaceBlock))
        {
            var ipv6Match = Regex.Match(ifaceBlock, @"ipv6\s+address\s+(\S+?)/(\d+)", RegexOptions.IgnoreCase);
            if (ipv6Match.Success)
            {
                result.Ipv6Enabled = true;
                result.Ipv6Mode = "Static";
                result.Ipv6Address = ipv6Match.Groups[1].Value;
                result.Ipv6Prefix = ipv6Match.Groups[2].Value;
            }
            else if (Regex.IsMatch(ifaceBlock, @"ipv6\s+enable", RegexOptions.IgnoreCase))
            {
                result.Ipv6Enabled = true;
            }
        }

        var dnsMatch = Regex.Match(config, @"dns\s+server\s+(\S+)", RegexOptions.IgnoreCase);
        if (dnsMatch.Success) result.DnsServer = dnsMatch.Groups[1].Value;
    }

    private static void ParseFirewallFields(string config, ParsedDeviceConfig result)
    {
        var zoneMatch = Regex.Match(config, @"firewall\s+zone\s+(\S+)\s*\r?\n(?:\s+set\s+priority\s+(\d+))?(?:\s*\r?\n\s*add\s+interface\s+(\S+(?:\s+\S+)*))?",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (zoneMatch.Success)
        {
            result.FwZoneName = zoneMatch.Groups[1].Value;
            if (zoneMatch.Groups[2].Success) result.FwZonePriority = zoneMatch.Groups[2].Value;
            if (zoneMatch.Groups[3].Success) result.FwZoneInterfaces = zoneMatch.Groups[3].Value.Trim();
        }

        if (!string.IsNullOrEmpty(result.FwZoneName))
        {
            var zoneBlockPattern = $@"firewall\s+zone\s+{Regex.Escape(result.FwZoneName)}\s*\r?\n((?:\s+[^\r\n]+\r?\n)*)";
            var zbMatch = Regex.Match(config, zoneBlockPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (zbMatch.Success)
            {
                var ifaces = Regex.Matches(zbMatch.Groups[1].Value, @"add\s+interface\s+(\S+)", RegexOptions.IgnoreCase)
                    .Select(mm => mm.Groups[1].Value);
                if (ifaces.Any()) result.FwZoneInterfaces = string.Join(", ", ifaces);
            }
        }

        var policyMatch = Regex.Match(config,
            @"security-policy\s*\r?\n\s+rule\s+name\s+(\S+)\s*\r?\n(?:\s+[^\r\n]*\r?\n)*",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (policyMatch.Success)
        {
            var policyBlock = policyMatch.Value;
            var pm = Regex.Match(policyBlock, @"rule\s+name\s+(\S+)", RegexOptions.IgnoreCase);
            if (pm.Success) result.FwPolicyName = pm.Groups[1].Value;

            pm = Regex.Match(policyBlock, @"source-zone\s+(\S+)", RegexOptions.IgnoreCase);
            if (pm.Success) result.FwPolicySrcZone = pm.Groups[1].Value;

            pm = Regex.Match(policyBlock, @"destination-zone\s+(\S+)", RegexOptions.IgnoreCase);
            if (pm.Success) result.FwPolicyDstZone = pm.Groups[1].Value;

            pm = Regex.Match(policyBlock, @"source-address\s+(.+)", RegexOptions.IgnoreCase);
            if (pm.Success) result.FwPolicySrcAddr = pm.Groups[1].Value.Trim();

            pm = Regex.Match(policyBlock, @"destination-address\s+(.+)", RegexOptions.IgnoreCase);
            if (pm.Success) result.FwPolicyDstAddr = pm.Groups[1].Value.Trim();

            pm = Regex.Match(policyBlock, @"action\s+(\S+)", RegexOptions.IgnoreCase);
            if (pm.Success) result.FwPolicyAction = pm.Groups[1].Value;
        }

        var natMatch = Regex.Match(config,
            @"nat-policy\s*\r?\n\s+rule\s+name\s+(\S+)\s*\r?\n(?:\s+[^\r\n]*\r?\n)*",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (natMatch.Success)
        {
            var natBlock = natMatch.Value;
            var nm = Regex.Match(natBlock, @"rule\s+name\s+(\S+)", RegexOptions.IgnoreCase);
            if (nm.Success) result.FwNatName = nm.Groups[1].Value;

            nm = Regex.Match(natBlock, @"source-zone\s+(\S+)", RegexOptions.IgnoreCase);
            if (nm.Success) result.FwNatSrcZone = nm.Groups[1].Value;

            nm = Regex.Match(natBlock, @"destination-zone\s+(\S+)", RegexOptions.IgnoreCase);
            if (nm.Success) result.FwNatDstZone = nm.Groups[1].Value;

            nm = Regex.Match(natBlock, @"nat-type\s+(\S+(?:-\S+)*)", RegexOptions.IgnoreCase);
            if (nm.Success) result.FwNatType = nm.Groups[1].Value;

            nm = Regex.Match(natBlock, @"(?:translated-address|apply\s+nat)\s+(.+)", RegexOptions.IgnoreCase);
            if (nm.Success) result.FwNatTranslatedAddr = nm.Groups[1].Value.Trim();
        }
    }

    // ── Section extraction ────────────────────────

    public static string ExtractSection(string fullConfig, Func<string, bool> blockPredicate)
    {
        var blocks = SplitHuaweiBlocks(fullConfig);
        var matching = blocks.Where(blockPredicate);
        return string.Join("\n#\n", matching).Trim();
    }

    public static List<string> SplitHuaweiBlocks(string config)
    {
        if (string.IsNullOrWhiteSpace(config)) return new List<string>();
        var parts = Regex.Split(config, @"\r?\n#\r?\n");
        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }

    // ── Block classifiers ─────────────────────────

    public static bool IsGlobalBlock(string block)
    {
        var trimmed = block.TrimStart();
        if (trimmed.StartsWith("sysname ", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("enable ", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("super ", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("header ", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("banner ", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("clock ", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.StartsWith("undo ", StringComparison.OrdinalIgnoreCase)) return true;
        if (Regex.IsMatch(trimmed, @"^(aaa|authentication|authorization|accounting|domain|user-group|local-user)\b", RegexOptions.IgnoreCase)) return true;
        return false;
    }

    public static bool IsStaticRouteBlock(string block) =>
        block.Contains("ip route-static", StringComparison.OrdinalIgnoreCase);

    public static bool IsRipBlock(string block)
    {
        var trimmed = block.TrimStart();
        if (Regex.IsMatch(trimmed, @"^rip\s", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(trimmed, @"^interface\s", RegexOptions.IgnoreCase) && block.Contains("rip ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsOspfBlock(string block)
    {
        var trimmed = block.TrimStart();
        if (Regex.IsMatch(trimmed, @"^ospf\s", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(trimmed, @"^interface\s", RegexOptions.IgnoreCase) && block.Contains("ospf ", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsIsisBlock(string block)
    {
        var trimmed = block.TrimStart();
        if (Regex.IsMatch(trimmed, @"^isis\s", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(trimmed, @"^interface\s", RegexOptions.IgnoreCase) && block.Contains("isis enable", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static bool IsBgpBlock(string block)
    {
        var trimmed = block.TrimStart();
        return Regex.IsMatch(trimmed, @"^bgp\s+\d+", RegexOptions.IgnoreCase);
    }

    public static bool IsVlanBlock(string block)
    {
        var trimmed = block.TrimStart();
        if (Regex.IsMatch(trimmed, @"^vlan\b", RegexOptions.IgnoreCase)) return true;
        if (Regex.IsMatch(trimmed, @"^interface\s+Vlanif", RegexOptions.IgnoreCase)) return true;
        return false;
    }
}
