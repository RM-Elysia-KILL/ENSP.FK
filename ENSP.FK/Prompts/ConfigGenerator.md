## 总则：最小配置原则

除非用户需求中明确要求，否则：
- 不添加题目未提及的路由协议邻居（含 IBGP）
- 不用 network 宣告题目未提及的网段
- 不加题目未要求的静态路由、ACL、安全加固
- 不引入直连到 IGP（接入交换机除外）
- 不把非根交换机配成 root primary

每条配置都要能在用户需求中找到依据。不确定时宁缺毋滥。

=== 华为 VRP CLI 速查（严禁使用 Cisco/Juniper 语法） ===

基本操作:
  进入系统视图: system-view  (简写 sys，不要写 configure terminal)
  回到用户视图: return  (不要写 end / exit)
  查看运行配置: display current-configuration  (不要写 show running-config)
  保存配置: save  (不要写 write memory / copy running-config startup-config)
  查看接口: display interface  (不要写 show interface)

接口管理:
  接口描述: description Link to R1
  管理状态: shutdown (关闭接口) / undo shutdown (开启接口)
  Hybrid模式: port link-type hybrid → port hybrid tagged vlan 10 20 / port hybrid untagged vlan 1

接口类型命名:
  千兆以太网: GigabitEthernet0/0/0  (严禁 GE0/0/0 / gi0/0/0 / Gig0/0/0)
  百兆以太网: Ethernet0/0/0  (严禁 Fa0/0/0 / fe0/0/0 / FastEthernet0/0/0)
  串口: Serial0/0/0  (严禁 Se0/0/0)
  环回: LoopBack0
  VLAN 接口: Vlanif10  (严禁 interface vlan 10 / SVI)

VLAN / 交换机:
  创建 VLAN: vlan 10 → quit
  接入接口: port link-type access → port default vlan 10  (严禁 switchport access vlan 10)
  干线接口: port link-type trunk → port trunk allow-pass vlan 10 20  (严禁 switchport trunk allowed vlan)
  生成树: stp enable  (严禁 spanning-tree vlan 10)

OSPF:
  ospf 1 router-id 1.1.1.1 → area 0 → network 10.0.0.0 0.0.0.255  (network 写 area 内！)
  物理接口用网段掩码: network 10.0.13.0 0.0.0.255
  Loopback 接口用主机掩码: network 10.0.3.3 0.0.0.0  (精确宣告！)
  引入路由必须带过滤: import-route direct route-policy XXX  (严禁无条件 import-route direct！)
  缺省路由: default-route-advertise match default  (用 match default 优于 always！)
  严禁: router ospf 1 / network 10.0.0.0 0.0.0.3 area 0 (这是 Cisco!!!)

RIP:
  rip 1 → version 2 → network 10.0.0.0  (严禁 router rip)

BGP:
  bgp 100 → router-id 10.0.1.1 → peer 10.0.0.2 as-number 200  (严禁 router bgp 100)
  IBGP必须用Loopback建邻居: peer 10.0.2.2 connect-interface LoopBack0 (IBGP不需要next-hop-local)
  EBGP必须用直连物理IP建邻居: peer 10.0.13.3 as-number 200 (不需要connect-interface)
  宣告网络: network 10.0.0.0 255.255.255.0  (注意是掩码不是反掩码)
  方向速记: LP用import, MED/Community/AS-Path用export, 路由过滤用import
  使用Community属性时必须加: peer x.x.x.x advertise-community  (否则Community发不出去！)

  **BGP 路由过滤方向（按需求意图选择，不要方向错）:**
  阻止对方访问本端网段 → 在本端 export 方向过滤
  阻止本端学习对方路由 → 在本端 import 方向过滤

  **IBGP full-mesh 决策规则:**
  缺省情况下，不要把同一 BGP AS 内的路由器自动配成 IBGP full-mesh。
  只有当题目明确要求"总部路由器之间运行IBGP"或指定设备通过Loopback建立IBGP时才配置。
  没有从IBGP向其他路由器传递路由的需求 → 不需要配IBGP。

  **路由反射器 (RR) 触发规则:**
  在 BGP AS 内，当有 3 台及以上 IBGP 邻居，或不希望配置 IBGP full-mesh，或题目隐含"简化配置/优化"时 → 必须配 RR。
  选其中一台作为 RR，对其他 IBGP 邻居做 reflect-client。
  如果 IBGP 邻居数 ≥ 2，考虑将其中一台设为 RR。

  **import-route 的 MED 规则:**
  在 BGP 中引入 IGP 路由时（import-route isis/ospf），默认 MED 推荐为 0，保证路由优先级稳定:
    import-route isis 1 med 0
    import-route ospf 1 med 0

  **BGP network 最小授权原则:**
  BGP network 只宣告:
  - Loopback 地址（/32 精确）
  - 题目明确要求发布的业务网段
  严禁盲目宣告聚合网段（如 network 70.1.0.0 255.255.0.0），除非题目明确要求。

IS-IS:
  配置顺序: isis 1 → network-entity 49.0001.xxxx.xxxx.xxxx.00 (System ID必须全网唯一！) → is-level level-2 → quit
  **NET 格式硬约束**: System ID 共 6 字节，分 3 段，每段必须 4 位十六进制数（2 字节）。
  正确: `49.0001.0100.0003.0003.00` ← 每段都是 4 位
  错误: `49.0001.0100.0030.03.00` ← 段长不齐，不是标准 6 字节
  然后才是接口下: isis enable 1 → isis circuit-type p2p  (P2P链路必须用p2p减少LSP！)
  缺省路由: default-route-advertise match default level-2  (用条件下发，防止环路)
  认证: area-authentication-mode md5 cipher xxx  (两台设备密码必须一致！)
  引入路由必须带过滤: import-route direct route-policy XXX  (严禁无条件引入！)
  重要: isis 1 进程必须写在所有 interface 之前！每个需要IS-IS的接口都要有 isis enable 1！

  **import-route direct 按角色区分:**
  接入层交换机角色 → 必须 import-route direct（把用户网段带进IS-IS）。
  纯骨干路由器角色 → 不应当 import-route direct（减少LSP，降低开销）。
  不要一刀切地给所有IS-IS设备都加 import-route direct。

ACL:
  acl number 3000 → rule 5 permit ip source 10.0.0.0 0.0.0.255 destination 10.0.1.0 0.0.0.255
  严禁: access-list 100 permit ip 10.0.0.0 0.0.0.255 10.0.1.0 0.0.0.255 (这是 Cisco!!!)

NAT:
  Easy IP: acl 2000 → rule 5 permit source 内部网段 反掩码 (先配ACL!)
  然后 WAN 口下: nat outbound 2000  (严禁 ip nat inside source list)
  静态映射: nat server protocol tcp global 1.2.3.4 80 inside 10.0.0.1 80  (写在接口下)

DHCP:
  全局地址池: ip pool vlan10 → network 10.0.0.0 mask 255.255.255.0 → gateway-list 10.0.0.1
  接口使能: dhcp select global  (严禁 ip dhcp pool / ip dhcp server)

终端设备 (PC/Server):
  注意: PC/Server 不需要配路由协议！只配 IP 和网关即可
  DHCP获取: ip address dhcp-alloc  (接口下)
  静态IP: ip address 10.0.0.100 255.255.255.0  (接口下)
  默认网关: ip route-static 0.0.0.0 0.0.0.0 10.0.0.1  (用户视图下)
  DNS服务器: dns server 8.8.8.8  (用户视图下)
  IPv6静态: ipv6 address 2001::1/64  (接口下)
  IPv6自动: ipv6 address auto  (接口下)
  IPv6网关: ipv6 route-static :: 0 2001::254  (用户视图下)
  严禁: PC/Server 上配置路由协议 (OSPF/BGP/IS-IS/RIP)！

静态路由:
  ip route-static 0.0.0.0 0.0.0.0 10.0.0.1  (注意是 route-static 不是 route！)
  严禁: ip route 0.0.0.0 0.0.0.0 10.0.0.1 (这是 Cisco!!!)

Telnet / 远程管理:
  user-interface vty 0 4 → authentication-mode password → set authentication password cipher xxx
  严禁: line vty 0 4 / password xxx / login (这是 Cisco!!!)

VLAN 聚合 (Super-VLAN):
  需求关键词: "VLAN聚合" / "不同VLAN互通" / "同一网段跨VLAN"
  vlan 4 → aggregate-vlan → access-vlan 2 3 → quit
  interface Vlanif4 → ip address x.x.x.x 24 → arp-proxy enable
  注意: Sub-VLAN 不能配 Vlanif 接口！只有 Super-VLAN 配 Vlanif！

MSTP:
  先改模式: stp mode mstp  (必须先切换！)
  stp region-configuration → region-name xxx → instance 1 vlan 10 → active region-configuration
  全网每个实例只有一台 root primary！需求指定的根设备配 primary，其他设备配 secondary 或不配！
  严禁: 同一设备同时配 root primary 和 root secondary！
  严禁: 多台设备同时配同一个 instance 的 root primary (会导致生成树混乱！)
  stp instance 0 root primary   (CIST 总根——只能是需求指定的一台！)
  stp instance 1 root primary   (实例根——只能是需求指定的一台！)
  stp instance 2 root secondary (备份根)
  边缘端口+BPDU保护: stp edged-port enable + stp bpdu-protection
  严禁: spanning-tree vlan 10 root primary (这是 Cisco!!!)

  **根保护配置位置:**
  `stp root-protection` 只能配置在连接非根交换机的接口上（设计端口），不能在所有接口批量配置。

  **根优先级选择规则:**
  若题目只要求某实例"不是根、但优先级较高" → 使用 stp instance X priority 4096 或 priority 8192。
  不要滥用 root secondary——root secondary 指定的是备份根，与"高优先级但非根"语义不同。

PBR 策略路由（需求明确写"策略路由"/"强制下一跳"/"指定出口"时才使用）:
  **重要：BGP 场景下，如果需求仅描述流量走向（如"X通过Y去访问Z"）而未写"策略路由"四个字，优先用 BGP route-policy + local-preference，不要用 PBR。**
  每个目的网段需要独立的 ACL！不要一个 ACL 匹配多个网段！
  acl number 3000 → rule 5 permit ip destination 70.1.0.0 0.0.255.255  (只匹配70段)
  acl number 3001 → rule 5 permit ip destination 80.1.0.0 0.0.255.255  (只匹配80段)
  traffic classifier c70 operator or → if-match acl 3000
  traffic classifier c80 operator or → if-match acl 3001
  traffic behavior bR3 → redirect ip-nexthop 10.0.35.3
  traffic behavior bR4 → redirect ip-nexthop 10.0.45.4
  traffic policy pbr → classifier c70 behavior bR3 → classifier c80 behavior bR4
  在流量入口物理接口的 inbound 方向绑定: interface GigabitEthernet0/0/0 → traffic-policy pbr inbound
  触发词: "策略路由" / "强制下一跳" / "指定出口"
  严禁: ip policy route-map / set ip next-hop (这是 Cisco!!!)
  严禁: traffic-policy PBR global (华为VRP不存在此命令！必须逐接口绑定 inbound！)

route-policy 多条件规则（重要！）:
  前提: ip community-filter 1 permit 100 / ip ip-prefix head-office permit 70.1.0.0 16 (先定义引用对象!)
  单个 node 内多个 if-match 是 AND 关系（必须同时满足）
  如果要 OR 关系（满足任一就匹配），必须拆成多个 node！
  例——拒绝所有总部路由（Community=100 OR IP前缀匹配head-office）:
  route-policy deny-head-office deny node 10
   if-match community-filter 1
  route-policy deny-head-office deny node 20
   if-match ip-prefix head-office
  然后 BGP 下: peer 10.0.0.2 route-policy deny-head-office import
  错误写法: node 10 里同时写 if-match community-filter 1 + if-match ip-prefix (变成AND！)

BGP 路由策略备选方案（需求要求"至少2种方案"时逐一使用）:
  方向速记: LP→import, MED→export, Community→export, AS-Path→export, 路由过滤→import
  方案A: Community过滤 (route-policy + if-match community-filter + peer x.x.x.x route-policy xxx export)
  方案B: Local-Preference (route-policy + apply local-preference 200 + peer x.x.x.x route-policy xxx import)
  方案B用于按目的网段选路的负载分担需求（如"X通过Y去访问Z网段"），BGP 运行时优先用此方案替代 PBR。
  方案C: AS-Path过滤 (ip as-path-filter + peer x.x.x.x as-path-filter export)
  方案D: MED (route-policy + apply cost + peer x.x.x.x route-policy xxx export)
  ACL绑定: 创建ACL后必须在接口下调用！traffic-filter inbound acl XXXX 或 traffic-policy pbr inbound

防火墙 (USG/NGFW) —— 需求含"防火墙"/"安全策略"/"NAT策略"时使用:
  安全区域:
    firewall zone trust → set priority 85 → add interface GigabitEthernet0/0/0 → quit
    firewall zone untrust → set priority 5 → add interface GigabitEthernet0/0/1 → quit
    firewall zone dmz → set priority 50 → add interface GigabitEthernet0/0/2 → quit
  安全策略:
    security-policy → rule name Allow_Trust_Untrust
    → source-zone trust → destination-zone untrust
    → source-address 10.0.0.0 24 → destination-address any
    → action permit
  严禁: security-policy 的 rule 忘记加 action！
  NAT策略:
    nat-policy → rule name SourceNAT → source-zone trust → destination-zone untrust
    → nat-type source-nat → source-address 10.0.0.0 24
    nat-policy → rule name DestNAT → source-zone untrust → destination-zone dmz
    → nat-type destination-nat → destination-address 1.2.3.4 32
  严禁: nat-policy 缺了 nat-type 会导致策略不生效！

配置顺序规范（严格按此顺序，否则配置不生效！）:
  1. stp mode mstp  (MSTP 必须先开模式)
  2. stp region-configuration  (再配区域)
  3. isis 1 进程配置  (先创建进程)
  4. interface 下的 isis enable 1  (后使能接口)
  5. acl 定义  (PBR/防火墙 必须先定义 ACL)
  6. firewall zone 定义  (防火墙安全区域)
  7. security-policy / nat-policy  (防火墙策略)
  8. traffic classifier/behavior/policy  (再配策略路由)
  9. interface 下的 traffic-policy 绑定  (最后绑定)

常见错误快速索引:
  IS-IS邻居起不来 → 检查接口下是否有 isis enable 1 / 认证密码是否一致 / NET System ID 是否唯一
  BGP邻居起不来 → IBGP改用Loopback+connect-interface / EBGP改用直连物理IP
  Community 不生效 → 检查是否缺少 peer x.x.x.x advertise-community
  路由策略不生效 → 检查 route-policy 是否被 peer 调用 (peer x.x.x.x route-policy xxx export/import)
  PBR不生效 → 检查5个环节是否完整 (acl→classifier→behavior→policy→interface绑定) / 检查是否误用traffic-policy global (此命令不存在！)
  MSTP 角色混乱 → 检查是否多台设备配了同一个 instance 的 root primary (全网每个实例只能一台primary！)
  OSPF邻居起不来 → 物理接口用网段掩码 network 10.0.13.0 0.0.0.255，不要用主机掩码 0.0.0.0
  防火墙策略不生效 → 检查 rule 是否缺了 action / nat-policy 是否缺了 nat-type
  终端设备被配了路由协议 → 检查 PC/Server 是否被误加了 OSPF/BGP/IS-IS

规则:
- 对端设备的对应接口 IP 必须在同一子网
- 协议参数（OSPF area、BGP AS 等）必须与对端设备匹配
- 每条命令一行，不要注释，不要 markdown 代码块
- 接口配置按 interface 分段，每个接口写完整 ip address
- sysname 必须用设备名，禁止保留默认的 Huawei

生成后自检（逐项核对，发现遗漏立即补充）:
  1. 遍历每台设备的每个物理接口——有IP地址的接口是否都宣告进了IGP (OSPF network/RIP network/IS-IS isis enable)？IS-IS 的 isis 1 进程是否写在所有 interface 之前？需要 IS-IS 的接口是否每个都有 isis enable 1？
  2. 检查所有 acl 是否已通过 traffic-filter/traffic-policy 绑定到接口？LoopBack 安全 ACL 用 traffic-filter inbound，PBR 用 traffic-policy inbound。没绑定的 ACL 等于没配！PBR 每个目的网段必须有独立的 ACL 和独立的 classifier！
  3. 检查所有 route-policy 是否已通过 peer x.x.x.x route-policy xxx export/import 调用？
  4. MSTP 角色检查：每个 instance 是否只有一台 root primary？需求指定的根设备是否正确？其他设备是否误配了 root primary？
  5. 需要 BGP 邻居的设备——peer as-number 是否正确？EBGP 用不同AS号，IBGP 用相同 AS？是否缺省避免了不必要的 IBGP full-mesh？
  6. 接口 Trunk 口是否放行了所有需要的 VLAN（含 Super-VLAN/聚合 VLAN）？
  7. 需求中提到"至少2种方案"时，是否真的实现了2种？每种方案的配置是否完整？
  8. 需求中的关键词(如"策略路由"→PBR, "VLAN聚合"→Super-VLAN, "员工访客隔离"→ACL)——是否已全部响应？
  9. 被引用的对象是否在调用前定义？(ip-prefix/community-filter/route-policy 先于 peer 调用, acl 先于 traffic-policy/classifier)
  10. IBGP 配置检查：是否存在不必要的 IBGP full-mesh？IBGP 邻居数 ≥ 2 时是否配置了 RR？
  11. BGP network 检查：是否只宣告了 Loopback /32 + 题目明确要求的网段？有无盲目宣告聚合网段？
  12. IS-IS import-route 检查：接入交换机是否引入了直连？骨干路由器是否错误地引入了直连？
  13. MSTP priority 检查：非根桥设备是否误用了 root secondary？需要高优先级但非根时是否用了 priority 数值？
  14. 防火墙检查：安全策略的 rule 是否都有 action？NAT 策略是否都指定了 nat-type？zone 是否都添加了接口？
  15. 终端设备检查：PC/Server 是否只配了 IP 和网关？是否有误配路由协议的？

## 高频错误强制修正（实验总结，以下每条都会导致严重问题，必须遵守）

### 设备角色边界
- 普通交换机（非BGP场景）不配 BGP 协议，不要自作主张给交换机加 BGP 配置
- 防火墙 (USG/NGFW) 不配 OSPF/BGP/IS-IS/RIP 等路由协议，只配安全策略
- PC/Server 终端设备只配 IP 和网关，不配任何路由协议
- 骨干路由器不引入直连到 IS-IS（减少 LSP），接入交换机必须引入直连到 IS-IS（发布用户网段）
- IGP 已覆盖的网段不要再加静态路由，避免冗余配置

### VLAN 聚合精确语法
- Super-VLAN 的 Vlanif 接口下必须用: `arp-proxy inter-sub-vlan-proxy enable`
- 普通 `arp-proxy enable` 只是单向代理，inter-sub 才能实现子 VLAN 间互访

### BGP 邻居硬约束
- IBGP 邻居: 必须用 Loopback 地址建邻居 + `connect-interface LoopBack0`
- EBGP 邻居: 必须用直连物理接口 IP 建邻居，不要加 connect-interface
- 使用 `apply community` 的 BGP peer 必须同时配 `peer x.x.x.x advertise-community`，否则 Community 发不出去

### OSPF 掩码铁律
- 物理接口 network 用网段掩码: `network 10.0.13.0 0.0.0.255`
- Loopback 接口 network 用主机掩码: `network 10.0.3.3 0.0.0.0`
- 物理接口写成主机掩码会导致 OSPF 邻居起不来

### PBR 五步完整性
- ACL → classifier → behavior → policy → 接口 inbound 绑定，缺一不可
- `traffic-policy PBR global` 是无效命令（华为 VRP 不存在），必须逐接口 `traffic-policy pbr inbound`
- 每个目的网段必须独立 ACL + 独立 classifier

### 负载分担对称性（两端都要配）
- 需求同时描述两个方向的选路优化时（如"总部→分部"和"分部→总部"），两端都必须有对应的 route-policy + LP 配置，不能只做一端
- 自检：对照需求中每一个"从X去Y走Z"的语句，确认 X 设备上是否有对应的 import 方向 LP 策略

### route-policy 逻辑陷阱
- 一个 node 内多个 if-match = AND（必须同时满足）
- OR 关系（满足任一）必须拆成多个 node
- 先定义 ip-prefix / community-filter，再在 route-policy 中引用

### MUX VLAN 禁止规则
- **严禁使用 MUX VLAN 实现跨 VLAN 的访问控制！** MUX VLAN 只能在同一个 VLAN 内做二层隔离，无法解决不同 VLAN 间的访问隔离
- 跨 VLAN 访问控制必须在网关设备的 Vlanif 接口下用 ACL / traffic-filter 实现
- 需求含"员工访客隔离"、"客户不能互访"等关键词时 → 用 ACL，不要用 mux-vlan

### VLAN IP 地址检查
- 每个 Vlanif 接口的 IP 地址必须与其所属网段一致，严禁张冠李戴（如 VLAN30 配 70.1.31.1 是错的，应该是 70.1.30.1）

输出格式（严格按照此格式，一台设备一个段）:

# ===== R1 =====
sysname R1
...

# ===== R2 =====
sysname R2
...

重要提醒：上面列出的 {deviceCount} 台设备，每台都必须有对应的 # ===== 配置段！现在生成:
