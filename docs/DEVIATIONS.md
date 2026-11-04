# 实现偏差登记（§2.1 豁免之外的自研部分）

| # | 能力 | AGENTS 要求 | 实际做法 | 理由 | 状态 |
|---|---|---|---|---|---|
| 1 | Forge / NeoForge 安装 | 复用 `ModLoader` / `ModDownloadLib` | 官方安装器 jar + 预置库 | 安装器要跑 processors；PCL 该函数与 16 处窗体耦合 | 待追认 |
| 2 | 整合包导入 | 复用 `ModModpack.vb` | 自写 `.mrpack` 解析 | 需隔离实例 + SHA1 校验 + 并行下载 | 待追认 |
| 3 | 资源搜索 | 复用 `Modules/Resource/*.vb` | 自写 Modrinth 客户端 | PCL 资源模块与 UI 同编译单元 | 待追认 |
| 4 | 崩溃分析 | 复用 `ModCrash.vb` | 自写 14 条特征规则 | 只需结论，不需要完整报告渲染 | 记录 |
| 5 | Mod 管理 | PCL 有同类 | 自写列表/启停/更新 | 与 PCL 界面解耦成本更低 | 记录 |
| 6 | 局域网扫描 / SLP | PCL 有同类 | 自写组播 + SLP | 同上 | 记录 |

## 新增自研（M22 之后）

| 能力 | 说明 |
|---|---|
| `SrvResolver` | 手写 DNS SRV 查询（.NET Framework 无托管 API） |
| `ServerProfilePicker` | 多地址挑优（人数 / 延迟） |
| `AutoModpackSelector` | 按服务端版本钉死 v4/v5，避免协议错配 |
| `ProfileInstanceMatcher` | 版本目录后缀精确匹配（修 1.21.1 命中 1.21.10） |
| `HttpFileDownloader` 分块 | 多连接 + 分块重试 + `.parts` 续传 |

## 处理建议

采用 A 方案：逐项在此登记 + 提交 AGENTS §2.1a 修订；
资源层（#3）保留自研，不设硬性切回时间表。