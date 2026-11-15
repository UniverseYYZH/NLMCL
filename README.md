# NLMCL

Minecraft Java 版启动器（Windows / .NET Framework 4.8 / WPF）。

本仓库是 **NLMCL 的公开代码镜像**，现由 **UniverseYYZH** 维护，主要用于比赛展示与代码评审。

项目最初由 **ZackWatts1145** 创建并交接给 UniverseYYZH；早期 Python + Flet 实现（v1.0.0）已归档至 [UniverseYYZH/NLMCL-Old](https://github.com/UniverseYYZH/NLMCL-Old)。

> **重要说明**
>
> 完整可运行实现（含 PCL 适配层及其 vendor 代码）在作者的私有仓库中；本公开仓库**不包含 PCL 源码，也不能独立构建/运行完整启动器**。
>
> 公开部分可以构建领域层与 ViewModel 层（见 `NLMCL.Public.sln`）。完整程序的运行演示、构建产物由作者另行提供。

旧实现源码仍在本仓库的 [`legacy/`](legacy/) 中保留，仅用于 C#/WPF 重写时的行为对照，不再作为主开发线。

---

## 仓库范围

公开镜像包含：

- WPF 界面与交互
- `NLMCL.Abstractions` 端口契约
- `NLMCL.Core` 领域与编排
- `NLMCL.App.ViewModels` 展示逻辑
- 测试、架构文档、修复记录
- 旧版 Python + Flet 实现（`legacy/`）

公开镜像**不包含**：

- `src/NLMCL.Core.Pcl`（PCL 的 C# 适配）
- `src/NLMCL.Core.Pcl.Vb`（PCL 的 VB 适配与 vendor）
- 完整启动器所需的私有运行时组件

这些组件只在作者的私有构建中提供，不随本仓库分发。

## 当前实现

- **前端**：WPF（XAML）自研，支持浅色/深色主题与界面缩放。
- **后端**：复用 PCL（Plain Craft Launcher）的成熟实现，经适配层接入；适配层属于私有依赖。
- **架构**：分层单体 + 端口/适配器 + MVVM，详见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

接手开发请先读 [docs/HANDOVER.md](docs/HANDOVER.md)：现状、构建方式、剩余待办和已知问题都在里面。

## 功能

以下功能在完整构建中可用；本公开镜像只展示其 UI、领域逻辑和测试。

| 能力 | 状态 | 说明 |
|---|---|---|
| 版本扫描与启动 | ✅ | 接 PCL 的 `McVersion`；父版本库合并；natives 解压 |
| 原版 / Fabric 安装 | ✅ | 下载经 BMCLAPI 镜像；Fabric 自动补装父版本 |
| Forge / NeoForge 安装 | ✅ | 运行官方安装器 jar；先预置库绕开直连 maven |
| Java 运行时下载 | ✅ | 按版本声明的 component 拉 Mojang JRE |
| 账号 | ✅ | 离线账号；外置登录（MUA 联合认证 / authlib-injector）；微软登录待 client id |
| 联机直连 | ✅ | 本机地址、端口探测、`--server` 启动 |
| 实例快照 | ✅ | saves / config / options.txt 备份与回滚 |
| 在线资源 | ✅ | Modrinth 搜索并安装模组/资源包/光影/数据包；中文名自动翻译 |
| 整合包导入 | ✅ | `.mrpack`：自动装加载器、建隔离实例、展开 overrides |
| 整合包更新/回滚 | ✅ | Modrinth 原地更新；更新前自动备份，保留玩家自加 Mod，可一键回滚 |
| CurseForge 更新检查 | ✅ | 文件指纹解析 project/file ID；只提示新版，不自动安装 |
| 设置 | ✅ | 主题、缩放、下载线程、Java、代理、启动页、PCL 目录迁移 |
| 皮肤 | ✅ | 读取认证服务器下发的皮肤；离线账号可导入本地 PNG 预览 |
| 联机扫描 | ✅ | 组播发现 + SLP 在线人数 + 地址二维码 |
| Mod 管理 | ✅ | 列表 / 启用禁用 / 删除 / Modrinth 更新检查 |
| 版本修复 | ✅ | 缺客户端 JAR / 库时一键从镜像补齐 |
| 崩溃分析 | ✅ | 读崩溃报告/latest.log，匹配常见错误并指出可疑模组 |
| 实例管理 | ✅ | 重命名 / 复制 / 删除（同步 JSON id） |
| 首次引导 | ✅ | 选目录 → 检测 Java → 登录（可跳过） |
| 一键安全模式 | ✅ | 本次启动禁用全部 Mod；游戏退出或启动失败后自动恢复 |
| 崩溃诊断包 | ✅ | 日志页导出脱敏 zip：日志、崩溃报告、Mod 清单、实例设置、Java/系统信息 |
| 下载暂停/继续 | ✅ | 暂停保留 `.parts`，继续从分块续传；取消才清理临时文件 |
| 启动器更新检查 | ✅ | 可配置 JSON feed；只展示版本与下载页，不静默替换 exe |
| `nlmcl://` 邀请 | ✅ | 单实例转发、确认卡；确认前不启动游戏 |
| 多开设置提示 | ✅ | 同实例运行中时提示并锁定内存/窗口/参数编辑 |

## 快速开始（公开部分）

```powershell
# 公开镜像：只构建 Abstractions / Core / ViewModels
dotnet build NLMCL.Public.sln -c Debug -m:1
```

要求：.NET SDK 8+（无需 Visual Studio）。首次构建会自动还原 NuGet 包。

完整启动器需要在私有环境中补齐 `NLMCL.Core.Pcl*` 后，构建 `NLMCL.sln`；本仓库不提供这些项目。

## 解决方案结构

```text
src/
  NLMCL.Abstractions      端口契约（netstandard2.0）——全项目的稳定边界
  NLMCL.Core              领域与编排（C# / net48）
  NLMCL.App.ViewModels    展示逻辑（C# / net48，零 WPF 依赖，可单测）
  NLMCL.App               WPF 视图 + 组合根（C# / net48）

  NLMCL.Core.Pcl          [私有，不在本镜像]
  NLMCL.Core.Pcl.Vb       [私有，不在本镜像]

tests/
  NLMCL.Tests             单元测试 + XAML 冒烟 + 离屏渲染快照
docs/                     架构、迁移映射、逐模块修复记录（INDEX.md）
third_party/MeloongCore   Apache-2.0，按上游原样 vendor
legacy/                   旧的 Python + Flet 实现（迁移对照物，勿删）
```

依赖方向只能向下：`App → ViewModels → Core → Abstractions ← Core.Pcl / Core.Pcl.Vb`。

## 来源与第三方

- **NLMCL**：最初由 **ZackWatts1145** 创建，后交接给 **UniverseYYZH** 维护；本仓库是公开代码镜像，完整实现由维护者私有维护。
- **NLMCL-Old**：项目旧版（Python + Flet，v1.0.0，最初由 ZackWatts1145 创建）归档在 [UniverseYYZH/NLMCL-Old](https://github.com/UniverseYYZH/NLMCL-Old)。
- **PCL（Plain Craft Launcher）**：作者 Meloong-Git 及社区贡献者。完整构建中的 PCL 适配/vendor 代码按《PCL 分发有限许可》使用，仅限私人范围；本公开仓库不分发 PCL 源码，也不因任何内容授予再分发权。许可原文见 [PCL/LICENCE](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)。
- **MeloongCore**：Apache-2.0，见 [third_party/MeloongCore](third_party/MeloongCore)。
- 其余 NuGet 依赖见 `Directory.Packages.props`。

## 文档

- [docs/INDEX.md](docs/INDEX.md) —— 全部文档索引（从哪读起）
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) —— 分层与依赖规则
- [docs/HANDOVER.md](docs/HANDOVER.md) —— 当前状态、构建、待办与已知坑
- [docs/FIX-*.md](docs/) —— 每个模块的修复记录：现象、根因、验证方法

## 约定

工程约定见仓库根目录 `AGENTS.md`。改动架构前务必先读第 3 节（PCL 代码使用前提）。
