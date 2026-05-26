using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ENSP.ZD.Models.Requirements;
using ENSP.ZD.Models.Topology;
using ENSP.ZD.Services;
using System.Collections.ObjectModel;
using Wpf.Ui.Abstractions.Controls;

namespace ENSP.ZD.ViewModels.Pages;

public partial class RequirementsViewModel : ObservableObject, INavigationAware
{
    private readonly ProjectSession _session;

    [ObservableProperty]
    private ObservableCollection<Device> _devices = new();

    [ObservableProperty]
    private Device? _selectedDevice;

    [ObservableProperty]
    private string _selectedRequirementType = "接口 IP";

    [ObservableProperty]
    private ObservableCollection<string> _requirementTypes = new()
    {
        "接口 IP", "VLAN", "OSPF", "静态路由", "ACL"
    };

    // Interface IP fields
    [ObservableProperty] private string _ifName = string.Empty;
    [ObservableProperty] private string _ifIp = string.Empty;
    [ObservableProperty] private string _ifMask = string.Empty;

    // VLAN fields
    [ObservableProperty] private string _vlanId = string.Empty;
    [ObservableProperty] private string _vlanName = string.Empty;
    [ObservableProperty] private string _vlanAccessPorts = string.Empty;
    [ObservableProperty] private string _vlanTrunkPorts = string.Empty;

    // OSPF fields
    [ObservableProperty] private string _ospfProcessId = "1";
    [ObservableProperty] private string _ospfRouterId = string.Empty;
    [ObservableProperty] private string _ospfAreaId = "0";
    [ObservableProperty] private string _ospfNetworks = string.Empty;

    // Static Route fields
    [ObservableProperty] private string _routeDest = string.Empty;
    [ObservableProperty] private string _routeMask = string.Empty;
    [ObservableProperty] private string _routeNextHop = string.Empty;
    [ObservableProperty] private string _routeOutIf = string.Empty;

    // ACL fields
    [ObservableProperty] private string _aclNumber = string.Empty;
    [ObservableProperty] private string _aclAction = "permit";
    [ObservableProperty] private string _aclProtocol = "ip";
    [ObservableProperty] private string _aclSource = "any";
    [ObservableProperty] private string _aclDest = "any";

    // Output
    [ObservableProperty]
    private ObservableCollection<TaskRequirement> _addedRequirements = new();

    [ObservableProperty]
    private string _reqStatus = string.Empty;

    [ObservableProperty]
    private string _rawRequirementText = string.Empty;

    public RequirementsViewModel(ProjectSession session)
    {
        _session = session;
    }

    public Task OnNavigatedToAsync()
    {
        if (_session.Topology != null)
            Devices = new ObservableCollection<Device>(_session.Topology.Devices);

        if (_session.Requirements.Count > 0)
            AddedRequirements = new ObservableCollection<TaskRequirement>(_session.Requirements);

        RawRequirementText = _session.RawRequirementText;
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync() => Task.CompletedTask;

    partial void OnRawRequirementTextChanged(string value)
    {
        _session.RawRequirementText = value;
    }

    partial void OnSelectedDeviceChanged(Device? value)
    {
        if (value != null && value.Interfaces.Count > 0)
        {
            IfName = value.Interfaces[0].Name;
            IfIp = value.Interfaces[0].IpAddress;
            IfMask = value.Interfaces[0].SubnetMask;
        }
    }

    [RelayCommand]
    private void AddRequirement()
    {
        if (SelectedDevice == null)
        {
            ReqStatus = "请先选择设备";
            return;
        }

        TaskRequirement? req = SelectedRequirementType switch
        {
            "接口 IP" => BuildInterfaceIpReq(),
            "VLAN" => BuildVlanReq(),
            "OSPF" => BuildOspfReq(),
            "静态路由" => BuildStaticRouteReq(),
            "ACL" => BuildAclReq(),
            _ => null
        };

        if (req == null)
        {
            ReqStatus = "需求构建失败";
            return;
        }

        _session.Requirements.Add(req);
        AddedRequirements.Add(req);
        ReqStatus = $"已为 {SelectedDevice.Name} 添加 {req.RequirementType} 需求 (共 {_session.Requirements.Count} 项)";
        ClearFormFields();
    }

    private void ClearFormFields()
    {
        IfName = IfIp = IfMask = string.Empty;
        VlanId = VlanName = VlanAccessPorts = VlanTrunkPorts = string.Empty;
        OspfProcessId = "1"; OspfRouterId = string.Empty; OspfAreaId = "0"; OspfNetworks = string.Empty;
        RouteDest = RouteMask = RouteNextHop = RouteOutIf = string.Empty;
        AclNumber = string.Empty; AclAction = "permit"; AclProtocol = "ip"; AclSource = "any"; AclDest = "any";
    }

    [RelayCommand]
    private void DeleteRequirement(TaskRequirement req)
    {
        _session.Requirements.Remove(req);
        AddedRequirements.Remove(req);
        ReqStatus = $"已删除 {req.DeviceName} 的 {req.RequirementType} 需求 (剩余 {_session.Requirements.Count} 项)";
    }

    private InterfaceIpRequirement BuildInterfaceIpReq()
    {
        return new InterfaceIpRequirement
        {
            DeviceName = SelectedDevice!.Name,
            InterfaceName = IfName,
            IpAddress = IfIp,
            SubnetMask = IfMask
        };
    }

    private VlanRequirement BuildVlanReq()
    {
        return new VlanRequirement
        {
            DeviceName = SelectedDevice!.Name,
            VlanId = int.TryParse(VlanId, out var id) ? id : 1,
            VlanName = VlanName,
            AccessPorts = VlanAccessPorts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList(),
            TrunkPorts = VlanTrunkPorts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList()
        };
    }

    private OspfRequirement BuildOspfReq()
    {
        var networks = OspfNetworks.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.Trim()).ToList();

        return new OspfRequirement
        {
            DeviceName = SelectedDevice!.Name,
            ProcessId = int.TryParse(OspfProcessId, out var pid) ? pid : 1,
            RouterId = OspfRouterId,
            Areas = new List<OspfArea>
            {
                new OspfArea
                {
                    AreaId = OspfAreaId,
                    Networks = networks
                }
            }
        };
    }

    private StaticRouteRequirement BuildStaticRouteReq()
    {
        return new StaticRouteRequirement
        {
            DeviceName = SelectedDevice!.Name,
            DestinationNetwork = RouteDest,
            SubnetMask = RouteMask,
            NextHop = RouteNextHop,
            OutInterface = RouteOutIf
        };
    }

    private AclRequirement BuildAclReq()
    {
        return new AclRequirement
        {
            DeviceName = SelectedDevice!.Name,
            AclNumber = int.TryParse(AclNumber, out var num) ? num : 3000,
            Rules = new List<AclRule>
            {
                new AclRule
                {
                    Action = AclAction,
                    Protocol = AclProtocol,
                    SourceIp = AclSource,
                    DestIp = AclDest
                }
            }
        };
    }
}
