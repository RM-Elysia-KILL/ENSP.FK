using ENSP.FK.Models.Configuration;
using ENSP.FK.Models.Requirements;
using ENSP.FK.Models.Topology;

namespace ENSP.FK.Services;

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
