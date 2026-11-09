# 联机（M6-B 第一阶段：直连）

## 做了什么

| 能力 | 实现 |
|---|---|
| 本机 IPv4 探测 | `LocalNetworkInfo`：枚举网卡，跳过回环/隧道/虚拟网卡（VPN、Docker、Hyper-V）与 APIPA（169.254.） |
| 地址解析 | `LanAddress.TryParse`：支持 `host:port` 与 `tcp://host:port`，错误信息按用户能看懂的话写 |
| 端口探测 | `ILocalNetworkInfo.IsPortOpenAsync`：1.5 秒超时的 TCP 连接 |
| 直连启动 | `LaunchCoreAsync(serverAddress)` → `LaunchRequest.ServerAddress` → PCL 参数管线加 `--server` |
| 复制地址 | `IClipboard` 端口，App 层用 `System.Windows.Clipboard` 实现 |

## 为什么先做直连而不是扫描

原版是"扫描 + 直连"两半。**扫描依赖组播** `224.0.2.60:4445`，校园网经常：
交换机不开 IGMP、无线开客户端隔离、ACL 拦组播——任何一条中了就扫不到。

直连只要求 TCP 可达。所以第一阶段先做直连，**校园网里必定可用**。

## 关键设计：先探测再启动

`ConnectToLanAsync` 的顺序是 **解析 → 探测 → 启动**。

先探测的价值：地址打错或对方没开房时，**立刻**给出提示，而不是让用户对着游戏的
"连接中…" 干等三十秒再超时。

探测失败时的提示明确区分了三种原因：

> 连不上 10.21.3.9:54321 —— 对方可能没开房、端口填错，或不在同一网段（校园网跨 VLAN 常被拦）

## 边界（写进界面了）

- 同一 WiFi / 同一网段：能连
- 不同楼栋、不同 VLAN：校园网路由通常不放行，**连不上是正常的**
- 跨公网：需要 FRP 等穿透工具，**不自研**（与原版相同的边界）

## 第二阶段（未做）

- 组播监听 + Server List Ping（MC 协议：VarInt + handshake + status）
- 收藏夹
- 二维码已接入（`FIX-86`，房间行「二维码」/顶栏「地址二维码」）
- 多网卡监听与诊断已接入（`FIX-87`；校园网客户端隔离仍需中转/热点/VPN）
- PCL 的小技巧：往 `127.0.0.1:4445` 发一条伪造广播，让游戏的"局域网"列表直接出现房间
