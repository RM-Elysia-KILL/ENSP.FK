using ENSP.FK.Models.Configuration;
using ENSP.FK.Models.Requirements;
using ENSP.FK.Models.Topology;

namespace ENSP.FK.Services;

public class ConfigurationGenerator
{
    public List<DeviceConfig> Generate(Topology topology, List<TaskRequirement> requirements)
    {
        var configs = new List<DeviceConfig>();

        foreach (var device in topology.Devices)
        {
            var deviceReqs = requirements.Where(r => r.DeviceName == device.Name).ToList();
            var config = GenerateDeviceConfig(device, deviceReqs);
            configs.Add(config);
        }

        return configs;
    }

    private static DeviceConfig GenerateDeviceConfig(Device device, List<TaskRequirement> requirements)
    {
        var config = new DeviceConfig { DeviceName = device.Name };

        // 1. System section
        AddSystemSection(config, device);

        // 2. Interface IPs — from requirements
        var ipReqs = requirements.OfType<InterfaceIpRequirement>().ToList();
        if (ipReqs.Count > 0)
            AddInterfaceSections(config, ipReqs);

        // 3. VLAN
        var vlanReqs = requirements.OfType<VlanRequirement>().ToList();
        foreach (var v in vlanReqs)
            AddVlanSection(config, v);

        // 4. OSPF
        var ospfReqs = requirements.OfType<OspfRequirement>().ToList();
        foreach (var o in ospfReqs)
            AddOspfSection(config, o);

        // 5. Static Routes
        var routeReqs = requirements.OfType<StaticRouteRequirement>().ToList();
        if (routeReqs.Count > 0)
            AddStaticRoutesSection(config, routeReqs);

        // 6. ACL
        var aclReqs = requirements.OfType<AclRequirement>().ToList();
        foreach (var a in aclReqs)
            AddAclSection(config, a);

        // 7. Return
        config.Sections.Add(new ConfigSection
        {
            Title = "Return",
            Commands = { new ConfigCommand { Command = "return" } }
        });

        return config;
    }

    private static void AddSystemSection(DeviceConfig config, Device device)
    {
        var section = new ConfigSection { Title = "System" };
        section.Commands.Add(new ConfigCommand { Command = "system-view" });
        section.Commands.Add(new ConfigCommand { Command = $"sysname {device.Name}" });
        config.Sections.Add(section);
    }

    private static void AddInterfaceSections(DeviceConfig config, List<InterfaceIpRequirement> ipReqs)
    {
        var section = new ConfigSection { Title = "Interface IP Configuration" };

        foreach (var req in ipReqs)
        {
            section.Commands.Add(new ConfigCommand { Command = $"interface {FormatInterfaceName(req.InterfaceName)}" });
            section.Commands.Add(new ConfigCommand { Command = $"ip address {req.IpAddress} {req.SubnetMask}", IndentLevel = 1 });
        }

        config.Sections.Add(section);
    }

    private static void AddVlanSection(DeviceConfig config, VlanRequirement vlan)
    {
        var section = new ConfigSection { Title = $"VLAN {vlan.VlanId}" };

        // Create VLAN
        section.Commands.Add(new ConfigCommand { Command = $"vlan {vlan.VlanId}" });
        if (!string.IsNullOrEmpty(vlan.VlanName))
            section.Commands.Add(new ConfigCommand { Command = $"name {vlan.VlanName}", IndentLevel = 1 });

        // Access ports
        foreach (var port in vlan.AccessPorts)
        {
            section.Commands.Add(new ConfigCommand { Command = $"interface {FormatInterfaceName(port)}" });
            section.Commands.Add(new ConfigCommand { Command = "port link-type access", IndentLevel = 1 });
            section.Commands.Add(new ConfigCommand { Command = $"port default vlan {vlan.VlanId}", IndentLevel = 1 });
        }

        // Trunk ports
        foreach (var port in vlan.TrunkPorts)
        {
            section.Commands.Add(new ConfigCommand { Command = $"interface {FormatInterfaceName(port)}" });
            section.Commands.Add(new ConfigCommand { Command = "port link-type trunk", IndentLevel = 1 });
            section.Commands.Add(new ConfigCommand { Command = $"port trunk allow-pass vlan {vlan.VlanId}", IndentLevel = 1 });
        }

        config.Sections.Add(section);
    }

    private static void AddOspfSection(DeviceConfig config, OspfRequirement ospf)
    {
        var section = new ConfigSection { Title = $"OSPF {ospf.ProcessId}" };

        section.Commands.Add(new ConfigCommand
        {
            Command = !string.IsNullOrEmpty(ospf.RouterId)
                ? $"ospf {ospf.ProcessId} router-id {ospf.RouterId}"
                : $"ospf {ospf.ProcessId}"
        });

        foreach (var area in ospf.Areas)
        {
            section.Commands.Add(new ConfigCommand { Command = $"area {FormatArea(area.AreaId)}", IndentLevel = 1 });

            foreach (var network in area.Networks)
            {
                var (ip, wildcard) = ParseNetwork(network);
                section.Commands.Add(new ConfigCommand
                {
                    Command = $"network {ip} {wildcard}",
                    IndentLevel = 2
                });
            }
        }

        config.Sections.Add(section);
    }

    private static void AddStaticRoutesSection(DeviceConfig config, List<StaticRouteRequirement> routes)
    {
        var section = new ConfigSection { Title = "Static Routes" };

        foreach (var route in routes)
        {
            var cmd = $"ip route-static {route.DestinationNetwork} {route.SubnetMask}";
            if (!string.IsNullOrEmpty(route.NextHop))
                cmd += $" {route.NextHop}";
            if (!string.IsNullOrEmpty(route.OutInterface))
                cmd += $" {FormatInterfaceName(route.OutInterface)}";

            section.Commands.Add(new ConfigCommand { Command = cmd });
        }

        config.Sections.Add(section);
    }

    private static void AddAclSection(DeviceConfig config, AclRequirement acl)
    {
        var section = new ConfigSection { Title = $"ACL {acl.AclNumber}" };

        section.Commands.Add(new ConfigCommand { Command = $"acl number {acl.AclNumber}" });

        int seq = 5;
        foreach (var rule in acl.Rules)
        {
            var cmd = $"rule {seq} {rule.Action} {rule.Protocol}";

            if (rule.SourceIp != "any")
            {
                cmd += $" source {rule.SourceIp}";
                if (!string.IsNullOrEmpty(rule.SourceWildcard))
                    cmd += $" {rule.SourceWildcard}";
            }

            if (rule.DestIp != "any")
            {
                cmd += $" destination {rule.DestIp}";
                if (!string.IsNullOrEmpty(rule.DestWildcard))
                    cmd += $" {rule.DestWildcard}";
            }

            if (!string.IsNullOrEmpty(rule.PortOperator) && !string.IsNullOrEmpty(rule.Port))
                cmd += $" destination-port {rule.PortOperator} {rule.Port}";

            section.Commands.Add(new ConfigCommand { Command = cmd, IndentLevel = 1 });
            seq += 5;
        }

        config.Sections.Add(section);
    }

    // Utility: format interface name (e.g., "GigabitEthernet0/0/1" or "GE0/0/1")
    private static string FormatInterfaceName(string name)
    {
        if (name.StartsWith("GE", StringComparison.OrdinalIgnoreCase))
            return "GigabitEthernet" + name[2..];
        if (name.StartsWith("Eth", StringComparison.OrdinalIgnoreCase))
            return "Ethernet" + name[3..];
        return name;
    }

    // Utility: format OSPF area (0 → 0.0.0.0, 1 → 0.0.0.1)
    private static string FormatArea(string areaId)
    {
        if (areaId.Contains('.'))
            return areaId;
        if (int.TryParse(areaId, out int areaNum))
            return $"0.0.0.{areaNum}";
        return areaId;
    }

    // Parse "10.0.0.0/24" → ("10.0.0.0", "0.0.0.255")
    private static (string ip, string wildcard) ParseNetwork(string network)
    {
        var parts = network.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[1], out int cidr))
            return (parts[0], CidrToWildcard(cidr));
        return (network, "0.0.0.0");
    }

    private static string CidrToWildcard(int cidr)
    {
        uint mask = cidr == 0 ? 0 : ~((1u << (32 - cidr)) - 1);
        uint wildcard = ~mask;
        return $"{(wildcard >> 24) & 0xFF}.{(wildcard >> 16) & 0xFF}.{(wildcard >> 8) & 0xFF}.{wildcard & 0xFF}";
    }
}
