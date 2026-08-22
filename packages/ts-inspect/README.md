# SatmLanIp

通过 **局域网或 VPN IP** 与朋友联机玩 *Shift at Midnight*，不经过 Photon 云服。

游戏内打开 **局域网联机**：房主开房或输入 IP 加入，全员准备后进入正常合作对局。

**版本：** 1.0.3 · **许可证：** [MIT](LICENSE) · **更新说明：** [CHANGELOG](CHANGELOG.md)

> **仅限可信对端。** 房间 UDP 无鉴权，请勿把监听端口暴露到公网；请使用局域网或 VPN。

## 功能

- 在游玩菜单（单人模式上方）注入 **局域网联机**
- 房主监听；加入方填写房主 IP（改过端口时用 `IP:端口`）
- 大厅用 UDP 同步准备状态；对局走 Fusion 直连

## 安装

1. 安装 [BepInExPack IL2CPP](https://thunderstore.io/c/shift-at-midnight/p/BepInEx/BepInExPack_IL2CPP/) `6.0.755`（或兼容版本）。
2. 用 Thunderstore / r2modman 安装本模组，或将 `SatmLanIp.dll` 放入 `BepInEx/plugins/`。
3. **所有联机玩家**都需安装本模组。

## 使用方法

1. 游玩菜单 → **局域网联机**。
2. **房主：** 创建房间 → 选档 / 模式 → 把大厅里显示的本机 IP 发给同伴。
3. **加入方：** 加入 → 输入房主 IP → 连接。
4. 全员准备 → 按大厅提示开始。

**版本要求：** 局域网联机需双方 **同一 Steam 游戏 build**（大厅会显示 `build …`）。模组在加入时比对 buildid；读不到 buildid 时仅警告、不拦截。请双方都安装 **SatmLanIp 1.0.3**。

## 异地组网

官方 Photon 云服卡顿或延迟高时，可改用本模组直连。

双方不在同一局域网时，先用 **VPN / 虚拟局域网** 连到同一虚拟网段，再用 **组网后的虚拟 IP** 开房、加入。

- 加入方必须填 **组网后的虚拟 IP**，不要填公网 IP。
- 延迟与稳定性取决于组网质量；断线时先确认 VPN 是否仍在线。

**推荐工具：** 以下为第三方异地组网方案，任选其一即可；与本模组相互独立、无隶属或官方背书，安装与使用风险自负。

- [Tailscale](https://github.com/tailscale/tailscale) — 基于 WireGuard，跨平台、配置简单；使用 Tailscale 分配给房主的 IP。
- [connecttool-qt](https://github.com/moeleak/connecttool-qt) — 图形化异地组网，支持 TUN；使用工具显示的虚拟 IP。

## 兼容性

| 依赖 | 版本 |
|------|------|
| 游戏 | *Shift at Midnight* |
| BepInEx | [BepInExPack IL2CPP](https://thunderstore.io/c/shift-at-midnight/p/BepInEx/BepInExPack_IL2CPP/) `6.0.755` |

## 相关链接

| | |
|---|---|
| 源码 | [github.com/679215/SatmLanIp](https://github.com/679215/SatmLanIp) |
| Thunderstore | [SatmLanIp](https://thunderstore.io/c/shift-at-midnight/p/679215/SatmLanIp/) |
| 模组社区 | [Shift at Midnight](https://thunderstore.io/c/shift-at-midnight/) |
| 参与贡献 | [CONTRIBUTING](https://github.com/679215/SatmLanIp/blob/main/docs/CONTRIBUTING.md) |

## 免责声明

非官方社区模组，与 Kwalee、Photon 及游戏发行方无关。使用风险自负，请遵守游戏用户协议。
