namespace ENSP.ZD.Models;

public class DeviceStartupProgress
{
    public DeviceRuntimeState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public string Phase { get; init; } = string.Empty;
    public int ElapsedSeconds { get; init; }
    public double ProgressPercent { get; init; }
}
