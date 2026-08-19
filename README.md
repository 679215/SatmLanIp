# SatmLanIp

通过 **局域网或 VPN IP** 与朋友联机玩 **Shift at Midnight**，此路径不走 Photon 云服务。

在游戏内打开 **局域网联机**，房主开房或输入 IP 加入，准备就绪后进入正常合作对局。

**版本：** 1.0.0 · **许可证：** [MIT](LICENSE)

> **仅限可信对端。** 房间 UDP 无鉴权，请勿将监听端口暴露到公网；请使用局域网或 VPN 组网。

## 相关链接

| | |
|---|---|
| 源码 | [github.com/679215/SatmLanIp](https://github.com/679215/SatmLanIp) |
| Thunderstore | [SatmLanIp](https://thunderstore.io/c/shift-at-midnight/p/679215/SatmLanIp/) |
| 模组社区 | [Shift at Midnight](https://thunderstore.io/c/shift-at-midnight/) |

## 功能说明

- 游玩菜单（单人模式上方）注入 **局域网联机**
- 房主 UDP 监听；加入方输入房主 IPv4 即可连接（默认端口无需填写）
- UDP 房间层同步准备状态，对局走 **Fusion Direct**

## 使用方法

1. 双方都安装本模组。
2. 游玩菜单 → **局域网联机**。
3. **房主：** 创建房间 → 选档/模式 → 告知局域网或 VPN IPv4（若修改过配置中的端口，一并告知）。
4. **加入方：** 加入 → 输入房主 IP → 连接（默认只填 IP；仅当房主改过端口时填写 `IP:端口`）。
5. 全员准备 → 按大厅提示开始。

## 异地组网

与朋友使用**官方服务器**（Photon 云服）联机时若卡顿、延迟高，可尝试改用本模组，并通过下方异地组网方式建立直连。

不在同一局域网时，先用 **VPN / 虚拟局域网** 把双方连到同一虚拟网段，再按上面步骤用 **VPN 分配的 IPv4** 开房、加入。

- 加入方填写的必须是 **组网后的虚拟 IP**。
- 延迟取决于组网质量；丢包或断线时先检查 VPN 是否仍在线。

**推荐工具（任选其一，与本模组无隶属关系）：**

- [Tailscale](https://github.com/tailscale/tailscale) — 基于 WireGuard 的虚拟组网，跨平台、配置简单；联机时使用 Tailscale 分配给房主的 IPv4。
- [connecttool-qt](https://github.com/moeleak/connecttool-qt) — 图形化异地组网工具，支持 TUN 虚拟网卡模式；联机时使用组网后显示的虚拟 IPv4。

## 兼容性
- **BepInEx：** [BepInExPack IL2CPP](https://thunderstore.io/c/shift-at-midnight/p/BepInEx/BepInExPack_IL2CPP/) `6.0.755`

## 免责声明

非官方社区模组，与 Kwalee、Photon 及游戏发行方无关。使用风险自负，请遵守游戏用户协议。
