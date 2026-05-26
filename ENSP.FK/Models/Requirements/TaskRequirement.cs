namespace ENSP.ZD.Models.Requirements;

public abstract class TaskRequirement
{
    public string DeviceName { get; set; } = string.Empty;
    public abstract string RequirementType { get; }
}
