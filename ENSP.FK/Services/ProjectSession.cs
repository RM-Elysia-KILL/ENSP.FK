using ENSP.ZD.Models.Configuration;
using ENSP.ZD.Models.Requirements;
using ENSP.ZD.Models.Topology;

namespace ENSP.ZD.Services;

public class ProjectSession
{
    public Topology? Topology { get; set; }
    public string? TopologyFilePath { get; set; }
    public List<TaskRequirement> Requirements { get; set; } = new();
    public string RawRequirementText { get; set; } = string.Empty;
    public List<DeviceConfig> Configs { get; set; } = new();

    public event Action? TopologyChanged;

    public void NotifyTopologyChanged() => TopologyChanged?.Invoke();
}
