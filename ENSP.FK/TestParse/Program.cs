using ENSP.ZD.Services;

try
{
    var path = @"C:\Users\STC\Downloads\实验13 IS-IS路由渗透(仅提交其中的word文档)\实验4.10 IS-IS路由渗透(仅提交其中的word文档)\实验4.10 IS-IS路由渗透.topo";
    Console.WriteLine($"读取: {path}");
    Console.WriteLine($"存在: {File.Exists(path)}");

    var parser = new TopologyParser();
    var topo = parser.Parse(path);

    Console.WriteLine($"\n设备数: {topo.Devices.Count}");
    foreach (var d in topo.Devices)
    {
        Console.WriteLine($"  {d.Name} ({d.Type}) com_port={d.ConsolePort} — {d.Interfaces.Count} 接口");
        foreach (var i in d.Interfaces.Take(6))
            Console.WriteLine($"    {i.Name}");
        if (d.Interfaces.Count > 6)
            Console.WriteLine($"    ... 共 {d.Interfaces.Count} 个");
    }

    Console.WriteLine($"\n链路数: {topo.Links.Count}");
    foreach (var l in topo.Links)
        Console.WriteLine($"  {l.DeviceA}:{l.InterfaceA} ↔ {l.DeviceB}:{l.InterfaceB}");
}
catch (Exception ex)
{
    Console.WriteLine($"错误: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
