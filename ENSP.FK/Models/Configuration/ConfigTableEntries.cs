using CommunityToolkit.Mvvm.ComponentModel;
using System.Text;

namespace ENSP.ZD.Models.Configuration;

public partial class StaticRouteEntry : ObservableObject
{
    [ObservableProperty] private string _dest = string.Empty;
    [ObservableProperty] private string _mask = string.Empty;
    [ObservableProperty] private string _nextHop = string.Empty;

    public string CliLine =>
        string.IsNullOrWhiteSpace(Dest) ? string.Empty
        : $"ip route-static {Dest} {Mask} {NextHop}".TrimEnd();
}

public partial class RipNetworkEntry : ObservableObject
{
    [ObservableProperty] private string _network = string.Empty;
    public string CliLine =>
        string.IsNullOrWhiteSpace(Network) ? string.Empty : $"network {Network}";
}

public partial class OspfNetworkEntry : ObservableObject
{
    [ObservableProperty] private string _network = string.Empty;
    [ObservableProperty] private string _area = "0";
    public string CliLine =>
        string.IsNullOrWhiteSpace(Network) ? string.Empty : $"network {Network} {Area}";
}

public partial class IsisNetworkEntry : ObservableObject
{
    [ObservableProperty] private string _network = string.Empty;
    public string CliLine =>
        string.IsNullOrWhiteSpace(Network) ? string.Empty : $"network-entity {Network}";
}

public partial class BgpNetworkEntry : ObservableObject
{
    [ObservableProperty] private string _network = string.Empty;
    public string CliLine =>
        string.IsNullOrWhiteSpace(Network) ? string.Empty : $"network {Network}";
}

public partial class BgpPeerEntry : ObservableObject
{
    [ObservableProperty] private string _peerIp = string.Empty;
    public string CliLine =>
        string.IsNullOrWhiteSpace(PeerIp) ? string.Empty : $"peer {PeerIp} as-number {{0}}";
}

public partial class VlanEntry : ObservableObject
{
    [ObservableProperty] private string _vlanId = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    public string CliLine
    {
        get
        {
            if (string.IsNullOrWhiteSpace(VlanId)) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine($"vlan {VlanId}");
            if (!string.IsNullOrWhiteSpace(Name)) sb.AppendLine($" name {Name}");
            return sb.ToString().TrimEnd();
        }
    }
}
