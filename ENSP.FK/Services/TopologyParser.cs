using ENSP.ZD.Models.Topology;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ENSP.ZD.Services;

public partial class TopologyParser
{
    public Topology Parse(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        if (ext != ".topo")
            throw new InvalidOperationException($"不支持的文件格式: {ext}，请选择 .topo 文件。");

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        var magic = new byte[4];
        fs.ReadExactly(magic, 0, 4);
        fs.Seek(0, SeekOrigin.Begin);

        try
        {
            if (magic[0] == 0x50 && magic[1] == 0x4B) // PK → ZIP
                return ParseFromZip(fs);

            var doc = LoadTopoXml(fs);
            return ValidateAndParse(doc);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"解析拓扑文件失败: {ex.Message}", ex);
        }
    }

    // eNSP .topo files are typically UTF-8 but often have encoding="UNICODE" in the XML
    // declaration, which confuses .NET XML parser into trying UTF-16. We fix that here.
    // Additionally, eNSP being a Chinese application may encode .topo files in GBK when
    // the topology contains Chinese annotations. We detect garbled decoding and fall back.
    private static XDocument LoadTopoXml(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        string xml;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            xml = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            xml = Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            xml = Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        else
        {
            xml = Encoding.UTF8.GetString(bytes);

            // eNSP may encode .topo in GBK when Chinese text is present.
            // U+FFFD (�) in the result signals UTF-8 decoding failure → retry with GBK.
            if (xml.Contains('�'))
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                xml = Encoding.GetEncoding("GBK").GetString(bytes);
            }
        }

        // Fix non-standard "UNICODE" encoding declaration that .NET doesn't recognize
        xml = EncodingDeclRegex().Replace(xml, "encoding=\"UTF-8\"");

        return XDocument.Parse(xml);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"encoding\s*=\s*""UNICODE""", RegexOptions.IgnoreCase)]
    private static partial Regex EncodingDeclRegex();

    private static Topology ParseFromZip(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var xmlEntry = archive.Entries.FirstOrDefault(e =>
            e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            e.Name.EndsWith(".topo", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("ZIP 内未找到拓扑 XML 文件。");

        using var entryStream = xmlEntry.Open();
        var doc = XDocument.Load(entryStream);
        return ValidateAndParse(doc);
    }

    private static Topology ValidateAndParse(XDocument doc)
    {
        if (doc.Root == null)
            throw new InvalidOperationException("XML 文件为空。");

        return ParseTopoXml(doc.Root);
    }

    private static Topology ParseTopoXml(XElement root)
    {
        var topology = new Topology();
        var guidToDevice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ParseDevices(root, topology, guidToDevice);
        ParseLinks(root, topology, guidToDevice);

        return topology;
    }

    private static void ParseDevices(XElement root, Topology topology, Dictionary<string, string> guidToDevice)
    {
        var devicesEl = FindChild(root, "devices")
            ?? throw new InvalidOperationException($"未找到 <devices> 元素。根: <{root.Name.LocalName}>");

        foreach (var devEl in devicesEl.Elements("dev"))
        {
            var guid = devEl.Attribute("id")?.Value ?? string.Empty;
            var name = devEl.Attribute("name")?.Value ?? "Unknown";
            var model = devEl.Attribute("model")?.Value ?? string.Empty;
            var comPortStr = devEl.Attribute("com_port")?.Value ?? "0";
            if (!int.TryParse(comPortStr, out var comPort)) comPort = 0;

            guidToDevice[guid] = name;

            double x = 0, y = 0;
            var cxStr = devEl.Attribute("cx")?.Value;
            var cyStr = devEl.Attribute("cy")?.Value;
            if (cxStr != null && cyStr != null)
            {
                double.TryParse(cxStr, out x);
                double.TryParse(cyStr, out y);
            }

            var device = new Device
            {
                Name = name,
                Model = model,
                Type = ParseDeviceType(model),
                ConsolePort = comPort,
                X = x,
                Y = y
            };

            // Collect interface blocks with slot assignment
            var ifaceBlocks = new List<(string IfName, int Count, int SlotNumber)>();

            // Parse slot elements to determine expansion slot count
            var slotElements = devEl.Elements("slot").ToList();
            int expansionCount = slotElements.Count(s => s.Attribute("isMainBoard")?.Value != "1");

            // Collect all interface blocks first
            var rawIfaces = new List<(string IfName, int Count)>();
            foreach (var iface in devEl.Elements("slot").Elements("interface"))
            {
                var ifName = iface.Attribute("interfacename")?.Value ?? "Ethernet";
                var countStr = iface.Attribute("count")?.Value ?? "0";
                if (!int.TryParse(countStr, out var count)) count = 0;
                if (count > 0)
                    rawIfaces.Add((ifName, count));
            }

            // Assign slot numbers: last expansionCount blocks go to slots 1..N, rest on slot 0
            int mainBoardBlocks = rawIfaces.Count - expansionCount;
            for (int idx = 0; idx < rawIfaces.Count; idx++)
            {
                int slotNum = idx < mainBoardBlocks ? 0 : idx - mainBoardBlocks + 1;
                ifaceBlocks.Add((rawIfaces[idx].IfName, rawIfaces[idx].Count, slotNum));
            }

            // Generate interface names: switches use 1-based, routers use 0-based port numbering
            bool isSwitch = IsSwitchModel(model);
            int portOffset = isSwitch ? 1 : 0;
            int currentSlot = -1;
            var prevCountByType = new Dictionary<string, int>();

            foreach (var (ifName, count, slotNum) in ifaceBlocks)
            {
                // Reset per-type counters when slot changes
                if (slotNum != currentSlot)
                {
                    currentSlot = slotNum;
                    prevCountByType.Clear();
                }

                int prevSameType = prevCountByType.GetValueOrDefault(ifName, 0);
                for (int i = 0; i < count; i++)
                {
                    int localIdx = prevSameType + i + portOffset;
                    device.Interfaces.Add(new DeviceInterface
                    {
                        Name = $"{ifName}{slotNum}/0/{localIdx}",
                        SlotIndex = slotNum
                    });
                }
                prevCountByType[ifName] = prevSameType + count;
            }

            topology.Devices.Add(device);
        }
    }

    private static void ParseLinks(XElement root, Topology topology, Dictionary<string, string> guidToDevice)
    {
        var linesEl = FindChild(root, "lines");
        if (linesEl == null) return;

        // Build device → ordered interface list
        var deviceIfaces = topology.Devices.ToDictionary(
            d => d.Name,
            d => d.Interfaces.Select(i => i.Name).ToList());

        foreach (var line in linesEl.Elements("line"))
        {
            var srcGuid = line.Attribute("srcDeviceID")?.Value ?? string.Empty;
            var dstGuid = line.Attribute("destDeviceID")?.Value ?? string.Empty;

            if (!guidToDevice.TryGetValue(srcGuid, out var devA)) continue;
            if (!guidToDevice.TryGetValue(dstGuid, out var devB)) continue;

            // A <line> can have multiple <interfacePair> elements (parallel links)
            foreach (var pair in line.Elements("interfacePair"))
            {
                string ifA = GetInterfaceByIndex(deviceIfaces, devA, pair.Attribute("srcIndex")?.Value);
                string ifB = GetInterfaceByIndex(deviceIfaces, devB, pair.Attribute("tarIndex")?.Value);
                double.TryParse(pair.Attribute("srcBoundRect_X")?.Value ?? string.Empty, out double x1);
                double.TryParse(pair.Attribute("srcBoundRect_Y")?.Value ?? string.Empty, out double y1);
                double.TryParse(pair.Attribute("tarBoundRect_X")?.Value ?? string.Empty, out double x2);
                double.TryParse(pair.Attribute("tarBoundRect_Y")?.Value ?? string.Empty, out double y2);

                topology.Links.Add(new TopologyLink
                {
                    DeviceA = devA, InterfaceA = ifA,
                    DeviceB = devB, InterfaceB = ifB,
                    X1 = x1, Y1 = y1,
                    X2 = x2, Y2 = y2
                });
            }
        }
    }

    private static string GetInterfaceByIndex(Dictionary<string, List<string>> map, string device, string? indexStr)
    {
        if (indexStr == null) return string.Empty;
        if (!int.TryParse(indexStr, out var idx)) return string.Empty;
        if (!map.TryGetValue(device, out var list)) return string.Empty;
        return (idx >= 0 && idx < list.Count) ? list[idx] : string.Empty;
    }

    private static XElement? FindChild(XElement parent, string name)
    {
        return parent.Elements().FirstOrDefault(e =>
            string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
    }

    private static DeviceType ParseDeviceType(string model)
    {
        var t = model.ToLower();
        if (t.Contains("router") || t.Contains("ar") || t.Contains("ne") || t.Contains("cx"))
            return DeviceType.Router;
        if (t.Contains("switch") || t.Contains("s57") || t.Contains("s37") || t.Contains("ce"))
            return DeviceType.Switch;
        if (t.Contains("firewall") || t.Contains("usg") || t.Contains("fw"))
            return DeviceType.Firewall;
        if (t.StartsWith("pc") || t.Contains("client") || t.Contains("pc-"))
            return DeviceType.PC;
        if (t.Contains("server"))
            return DeviceType.Server;
        return DeviceType.Router;
    }

    // Huawei VRP port numbering: routers use 0-based, switches use 1-based
    private static bool IsSwitchModel(string model)
    {
        if (string.IsNullOrEmpty(model)) return false;
        return model.StartsWith("S", StringComparison.OrdinalIgnoreCase)
            || model.StartsWith("CE", StringComparison.OrdinalIgnoreCase);
    }
}
