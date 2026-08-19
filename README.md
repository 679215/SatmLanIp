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
- 房主 UDP 监听；加入方输入房主 IPv4（及端口）连接
- UDP 房间层同步准备状态，对局走 **Fusion Direct**

## 使用方法

1. 双方都安装本模组。
2. 游玩菜单 → **局域网联机**。
3. **房主：** 创建房间 → 选档/模式 → 告知局域网或 VPN IPv4 与端口。
4. **加入方：** 加入 → 输入房主 IP（非默认端口一并填写）→ 连接。
5. 全员准备 → 按大厅提示开始。

## 创作者

- 679215 承担核心开发工作与大量测试任务
- Littps 在前期方向制定提供思路,在后期参与一定量测试任务

## 兼容性
- **BepInEx：** [BepInExPack IL2CPP](https://thunderstore.io/c/shift-at-midnight/p/BepInEx/BepInExPack_IL2CPP/) `6.0.755`

## 免责声明

非官方社区模组，与 Kwalee、Photon 及游戏发行方无关。使用风险自负，请遵守游戏用户协议。
