# NLMCL

Minecraft Java 版启动器，Windows / .NET Framework 4.8 / WPF。

本仓库是 **NLMCL 的公开代码镜像**，由 **UniverseYYZH** 维护，用于比赛展示与代码评审。

完整可运行实现包含 PCL 适配层及其 vendor 代码，只保留在维护者的私有仓库中。本仓库**不包含 PCL 源码，不能独立构建或运行完整启动器**；公开部分可以构建领域层与 ViewModel 层。

## 公开范围

- `NLMCL.Abstractions`：端口契约（netstandard2.0）
- `NLMCL.Core`：领域与编排（C# / net48）
- `NLMCL.App.ViewModels`：展示逻辑（C# / net48，零 WPF 依赖，可单测）
- `NLMCL.App`：WPF 视图与组合根源码（本镜像提供源码，公开解决方案不构建该项目的 PCL 适配依赖）
- `tests`：单元测试与离屏渲染快照
- `third_party/MeloongCore`：Apache-2.0，按上游原样 vendor

公开镜像不包含：

- `src/NLMCL.Core.Pcl`（PCL 的 C# 适配）
- `src/NLMCL.Core.Pcl.Vb`（PCL 的 VB 适配与 vendor）
- 完整启动器所需的私有运行时组件

## 构建

```powershell
dotnet build NLMCL.Public.sln -c Release -m:1
```

要求：.NET SDK 8+。首次构建会自动还原 NuGet 包。

完整启动器的运行演示、构建产物与 PCL 适配由维护者另行提供。

## 功能概览

- 原版 / Fabric / Forge / NeoForge 实例扫描与启动
- 离线、外置登录、正版账号与皮肤
- 下载中心：版本、资源、Mod、整合包、任务暂停/继续
- 实例快照、实例包导入导出、世界详情
- 联机大厅、服务器档案、`nlmcl://` 邀请
- 一键安全模式、崩溃分析、脱敏诊断包
- PCL 目录迁移、多目录、启动前预检
- Modrinth / CurseForge 整合包更新检查与 Modrinth 回滚

## 目录

```text
src/
  NLMCL.Abstractions      端口契约
  NLMCL.Core              领域与编排
  NLMCL.App.ViewModels    展示逻辑
  NLMCL.App               WPF 视图（公开解决方案不构建 PCL 依赖）
tests/
  NLMCL.Tests             单元测试与 XAML 冒烟
docs/                     架构、实例设置、联机、打包说明
third_party/MeloongCore   Apache-2.0 vendor
```

依赖方向只能向下：`App → ViewModels → Core → Abstractions`。

## 文档

- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)：分层与依赖规则
- [docs/INSTANCE-SETTINGS.md](docs/INSTANCE-SETTINGS.md)：实例设置与数据模型
- [docs/MULTIPLAYER.md](docs/MULTIPLAYER.md)：联机与服务器档案
- [docs/DEVIATIONS.md](docs/DEVIATIONS.md)：与 PCL 行为的差异
- [docs/PACKAGING.md](docs/PACKAGING.md)：打包说明

## 来源与许可

- **NLMCL**：由 **UniverseYYZH** 维护。
- **NLMCL-Old**：早期 Python + Flet 实现已归档到 [UniverseYYZH/NLMCL-Old](https://github.com/UniverseYYZH/NLMCL-Old)，不再更新。
- **PCL（Plain Craft Launcher）**：作者 Meloong-Git 及社区贡献者。完整构建中的 PCL 适配/vendor 代码按《PCL 分发有限许可》使用，仅限私人范围；本公开仓库不分发 PCL 源码，也不因任何内容授予再分发权。许可原文见 [PCL/LICENCE](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)。
- **MeloongCore**：Apache-2.0，见 [third_party/MeloongCore](third_party/MeloongCore)。

其余 NuGet 依赖见 `Directory.Packages.props`。
