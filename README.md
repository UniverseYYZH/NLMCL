# NLMCL（C# / WPF 重写）

> **来源声明**
>
> - **原项目**：[UniverseYYZH/NLMCL](https://github.com/UniverseYYZH/NLMCL)（作者 **UniverseYYZH**，
>   Python + Flet 实现，v1.0.0）。本仓库是该项目的 **C#/WPF 重写**，经原作者允许后建立。
>   旧实现的源码作为对照物保留在 [`legacy/`](legacy/)，对应 v1.0.0 发布版本。
> - **后端依赖**：[PCL（Plain Craft Launcher）](https://github.com/Meloong-Git/PCL)，作者 Meloong-Git
>   及社区贡献者。其部分源码以 vendor 形式引入（`src/NLMCL.Core.Pcl.Vb/Vendor/`），
>   **仅按《PCL 分发有限许可》在私人范围内使用**，不对外分发。许可原文见
>   [PCL/LICENCE](https://github.com/Meloong-Git/PCL/blob/main/LICENCE)。
> - **第三方库**：[MeloongCore](https://github.com/Meloong-Git/MeloongCore)（Apache-2.0），
>   以及 NuGet 依赖（见 `Directory.Packages.props`）。
>
> 本仓库为**私有仓库**（含 Release 附件）：仅本人可见，不对外分发二进制或 PCL 代码。

Minecraft Java 版启动器（Windows / .NET Framework 4.8 / WPF）。

> **接手开发请先读 [docs/HANDOVER.md](docs/HANDOVER.md)** —— 现状、构建方式、剩余待办、已知坑都在里面。

- **前端**：WPF（XAML）自研，深色/浅色主题、界面缩放。
- **后端**：复用 PCL（Plain Craft Launcher）的成熟实现，经适配层接入。
- **架构**：分层单体 + 端口/适配器 + MVVM，详见 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)。

## 功能

| 能力 | 状态 | 说明 |
|---|---|---|
| 版本扫描与启动 | ✅ | 接 PCL 的 `McVersion`；父版本库合并；natives 解压 |
| 原版 / Fabric 安装 | ✅ | 下载经 BMCLAPI 镜像；Fabric 自动补装父版本 |
| **Forge / NeoForge 安装** | ✅ | 运行官方安装器 jar；先预置库绕开直连 maven |
| Java 运行时下载 | ✅ | 按版本声明的 component 拉 Mojang JRE |
| 账号 | ✅ | 离线账号；外置登录（MUA 联合认证 / authlib-injector）；微软登录待 client id |
| 联机直连 | ✅ | 本机地址、端口探测、`--server` 启动 |
| 实例快照 | ✅ | saves / config / options.txt 备份与回滚 |
| **在线资源** | ✅ | Modrinth 搜索并安装模组/资源包/光影/数据包；中文名自动翻译 |
| **整合包导入** | ✅ | `.mrpack`：自动装加载器、建隔离实例、展开 overrides |
| 设置 | ✅ | 主题（浅色/深色/跟随系统）、缩放（普通/最大化分档）、下载线程 |
| 皮肤 | ✅ | 读取认证服务器下发的皮肤；离线账号可导入本地 PNG 预览 |
| 联机扫描 | ✅ | 组播发现 + SLP 在线人数；二维码待补 |
| Mod 管理 | ✅ | 列表 / 启用禁用 / 删除 / Modrinth 更新检查 |
| 版本修复 | ✅ | 缺客户端 JAR / 库时一键从镜像补齐 |
| 崩溃分析 | ✅ | 读崩溃报告/latest.log，匹配 14 类错误并指出可疑模组 |
| 实例管理 | ✅ | 重命名 / 复制 / 删除（同步 JSON id） |
| 首次引导 | ✅ | 选目录 → 检测 Java → 登录（可跳过） |

## 快速开始

```powershell
# 构建（注意 -m:1：并行构建偶发 MSBuild NRE）
dotnet build NLMCL.sln -c Debug -m:1

# 测试（net48 + SDK 风格项目，VSTest 适配器不可用，必须用 xunit 控制台 runner）
& "$env:USERPROFILE\.nuget\packages\xunit.runner.console\2.9.2\tools\net472\xunit.console.exe" `
    tests\NLMCL.Tests\bin\Debug\net48\NLMCL.Tests.dll -nologo -parallel none

# 运行（务必复制到工作区之外再跑，见 docs/FIX-1-LAUNCH.md §11）
```

要求：.NET SDK 8+（无需 Visual Studio）。首次构建会自动还原 NuGet 包。

> ⚠️ 如果 exe 位于受约束的目录（例如某些 AI 工作区），它会**写不了 `%APPDATA%` 和 `%TEMP%`**，
> 表现为“存不了设置、启动游戏失败”。把 exe 复制到普通目录（如 `D:\NLMCL-test\`）再运行。

## 解决方案结构

```
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

## 文档

- [docs/INDEX.md](docs/INDEX.md) —— 全部文档索引（从哪读起）
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) —— 分层与依赖规则
- [docs/FIX-*.md](docs/) —— 每个模块的修复记录：现象、根因、验证方法

## 约定

工程约定见仓库根目录 `AGENTS.md`。改动架构前务必先读第 3 节（PCL 代码使用前提）。
