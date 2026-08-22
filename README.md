# SatmLanIp

通过 **局域网或 VPN IP** 与朋友联机 *Shift at Midnight*，不经过 Photon 云服。

> **仅限可信对端。** 房间 UDP 无鉴权，请勿把监听端口暴露到公网；请使用局域网或 VPN。

**联机双方均需安装本模组。**

## 联机

1. 游玩菜单（单人模式上方）→ **局域网联机**。
2. **房主：** 创建房间 → 选档 / 模式 → 把大厅显示的本机 IP 发给同伴。
3. **加入方：** 加入 → 输入房主 IP（改过端口时用 `IP:端口`）→ 连接 → 准备。
4. 全员准备 → 按大厅提示开始。

## 版本要求

联机双方请使用 **同一 Steam 游戏版本**。大厅会显示本机 `build …`；版本不一致时无法加入。读不到版本号时仅警告，不阻止联机。

## 异地组网

不在同一局域网时，先用 VPN / 虚拟局域网连到同一网段，再用 **虚拟 IP** 开房、加入（不要填公网 IP）。

- [Tailscale](https://github.com/tailscale/tailscale) — WireGuard，跨平台；使用分配给房主的虚拟 IP
- [connecttool-qt](https://github.com/moeleak/connecttool-qt) — 图形化组网；使用工具显示的虚拟 IP

以上为第三方工具，与本模组无关；延迟与稳定性取决于组网质量。

## 链接

- 源码：[github.com/679215/SatmLanIp](https://github.com/679215/SatmLanIp)
- Thunderstore：[SatmLanIp](https://thunderstore.io/c/shift-at-midnight/p/679215/SatmLanIp/)

非官方社区模组，与 Kwalee、Photon 及游戏发行方无关。使用风险自负，请遵守游戏用户协议。
