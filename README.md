# NLMCL

Minecraft Java 版启动器（Windows / .NET Framework 4.8 / WPF）。

本仓库是 **NLMCL 的当前主仓库**，作者 **UniverseYYZH**。

项目最初使用 Python + Flet 实现，v1.0.0 旧仓库已迁至：

**[UniverseYYZH/NLMCL-Old](https://github.com/UniverseYYZH/NLMCL-Old)**

旧实现源码仍在本仓库的 [`legacy/`](legacy/) 中保留，仅用于 C#/WPF 重写时的行为对照，不再作为主开发线。

---

## 当前实现

- **前端**：WPF（XAML）自研，支持浅色/深色主题与界面缩放。
- **后端**：复用 PCL（Plain Craft Launcher）的成熟实现，经适配层接入。
- **架构**：分层单体 + 端口/适配器 + MVVM，详见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

接手开发请先读 [docs/HANDOVER.md](docs/HANDOVER.md)：现状、构建方式、剩余待办和已知问题都在里面。

## 功能

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

## 快速开始

```powershell
# 构建（-m:1：并行构建偶发 MSBuild NRE）
dotnet build NLMCL.sln -c Debug -m:1

# 测试（net48 + SDK 风格项目，VSTest 适配器不可用，必须用 xunit 控制台 runner）
& "$env:USERPROFILE\.nuget\packages\xunit.runner.console\2.9.2\tools\net472\xunit.console.exe" `
    tests\NLMCL.Tests\bin\Debug\net48\NLMCL.Tests.dll -nologo -parallel none

# 运行 exe
dotnet run --project src\NLMCL.App\NLMCL.App.csproj
```

要求：.NET SDK 8+（无需 Visual Studio）。首次构建会自动还原 NuGet 包。

> **运行位置**：不要把 exe 放在受约束的目录（例如某些 AI 工作区）里运行。此类目录可能让进程无法写 `%APPDATA%`/`%TEMP%`；启动器会回退数据目录，但 Forge 等游戏进程仍可能因 Low 完整性写不了游戏目录。建议复制到普通目录（如 `D:\NLMCL-test\`）再运行。

## 解决方案结构

```text
src/
  NLMCL.Abstractions      端口契约（netstandard2.0）——全项目的稳定边界
  NLMCL.Core              领域与编排（C# / net48）
  NLMCL.Core.Pcl          PCL 的 C# 部分（vendor）
  NLMCL.Core.Pcl.Vb       PCL 的 VB 部分 + Shim（唯一允许出现 VB 的地方）
  NLMCL.App.ViewModels    展示逻辑（C# / net48，零 WPF 依赖，可单测）
  NLMCL.App               WPF 视图 + 组合根（C# / net48）
tests/
  NLMCL.Tests             单元测试 + XAML 冒烟 + 离屏渲染快照
docs/                     架构、迁移映射、逐模块修复记录（INDEX.md）
third_party/MeloongCore   Apache-2.0，按上游原样 vendor
legacy/                   旧的 Python + Flet 实现（迁移对照物，勿删）
```

依赖方向只能向下：`App → ViewModels → Core → Abstractions ← Core.Pcl / Core.Pcl.Vb`。

## 来源与第三方

- **NLMCL**：由 **UniverseYYZH** 创建和维护；本仓库是当前主仓库。
- **NLMCL-Old**：项目旧版（Python + Flet，v1.0.0）保留在 [UniverseYYZH/NLMCL-Old](https://github.com/UniverseYYZH/NLMCL-Old)。
- **PCL（Plain Craft Launcher）**：作者 Meloong-Git 及社区贡献者。部分源码以 vendor 形式位于 `src/NLMCL.Core.Pcl.Vb/Vendor/`，按《PCL 分发有限许可》**仅限私人范围内使用，不对外分发**；许可原文见 [PCL/LICENCE](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)。本仓库不因包含 vendor 代码而授予任何再分发权。
- **MeloongCore**：Apache-2.0，见 [third_party/MeloongCore](third_party/MeloongCore)。
- 其余 NuGet 依赖见 `Directory.Packages.props`。

## 文档

- [docs/INDEX.md](docs/INDEX.md) —— 全部文档索引（从哪读起）
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) —— 分层与依赖规则
- [docs/HANDOVER.md](docs/HANDOVER.md) —— 当前状态、构建、待办与已知坑
- [docs/FIX-*.md](docs/) —— 每个模块的修复记录：现象、根因、验证方法

## 约定

工程约定见仓库根目录 `AGENTS.md`。改动架构前务必先读第 3 节（PCL 代码使用前提）。
