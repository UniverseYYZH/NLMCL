# NLMCL 架构决策记录（ADR-0001）

> 状态：已采纳
> 日期：2026-10-26
> 决策人：项目负责人
> 适用范围：`NLMCL.sln` 全部工程

---

## 1. 决策

采用**分层单体 + 端口/适配器（六边形）+ MVVM**：

- 一个解决方案、六个项目，用**程序集边界**强制分层，而不是用文件夹。
- Minecraft 领域能力（下载、安装、加载器、Java、启动）通过**接口**暴露，PCL 的 VB.NET 实现放在**唯一的适配层**里。
- UI 与业务逻辑通过 **MVVM** 解耦：ViewModel 不引用任何 WPF 类型，因此可单测。
- 目标框架统一 **.NET Framework 4.8**；**优先复用 PCL 已经写成 C# 的部分**（`PCLCS`），只有确实没有 C# 实现时才引入 VB 模块。

## 2. 为什么是这套（约束推导）

这不是"选了最时髦的架构"，而是被三条硬约束推出来的唯一可行解：

| 约束 | 推导出的结论 |
|---|---|
| 必须**直接复用 PCL 的成熟代码**（项目架构约束） | PCL 是 VB.NET + WPF + .NET Framework 4.8，且 `OptionStrict=Off`。**VB + WPF 在 .NET 5+ 上不受支持**，因此整个应用必须留在 net48。 |
| **不做 VB.NET → C# 机械翻译**（§2.3） | 复用部分保持 VB；新代码用 C#。两者共存只能靠**程序集边界**，不能靠语言转换器。 |
| **前端自研**（§2.2）且要能多人并行 | UI 必须独立成程序集，且展示逻辑要能脱离 UI 线程测试。 |

由此还有两个被动的正确选择：

- **不引 WPF 到 ViewModel**：net48 + WPF 的 ViewModel 若引用 `Dispatcher`/`Window`，就永远无法单测，也无法换 UI 框架。现在 ViewModels 这层是纯 POCO + `INotifyPropertyChanged`。
- **接口放 `netstandard2.0`**：`NLMCL.Abstractions` 刻意用 netstandard2.0，这样**将来若把后端换成 .NET 10 + 纯 C# 实现（如 CmlLib.Core），上层一行不用改**——这是给 net48 冻结风险留的逃生门。

## 3. 分层与依赖规则

```
┌──────────────────────────────────────────────────────────┐
│ NLMCL.App                     WPF 视图 + 组合根            │  net48 / C#
│   MainWindow.xaml, App.xaml.cs, Converters               │
└───────────────┬──────────────────────────────────────────┘
                │ 依赖
┌───────────────▼──────────────────────────────────────────┐
│ NLMCL.App.ViewModels          展示逻辑（零 WPF 依赖）        │  net48 / C#
│   MainViewModel, NavigationItem, VersionItem             │
└───────────────┬──────────────────────────────────────────┘
                │ 依赖
┌───────────────▼──────────────────────────────────────────┐
│ NLMCL.Core                    我们的领域与编排              │  net48 / C#
│   AppPaths ...                                            │
└───────────────┬──────────────────────────────────────────┘
                │ 依赖（只依赖接口）
┌───────────────▼──────────────────────────────────────────┐
│ NLMCL.Abstractions            稳定契约（端口）              │  netstandard2.0 / C#
│   IAppPaths, IVersionCatalog, IVersionInstaller,          │
│   IGameLauncher, MinecraftVersion, InstallRequest ...     │
└───────────────▲──────────────────────────────────────────┘
                │ 实现
┌───────────────┴──────────────────────────────────────────┐
│ NLMCL.Core.Pcl                PCL 适配器（防腐层）          │  net48 / C#
│   Vendor/（PCL 原样源码）+ PclVersionCatalog              │
└──────────────────────────────────────────────────────────┘
```

**三条不可违反的依赖规则：**

1. **依赖只能向下**（App → ViewModels → Core → Abstractions）。反向引用一律 review 打回。
2. **`NLMCL.Core` 永不引用 `NLMCL.Core.Pcl`**。Core 只认接口，实现在组合根注入。
3. **只有 `NLMCL.App/App.xaml.cs` 知道具体实现是谁**。换后端只改这一个文件。

## 4. 程序集职责与归属

| 程序集 | 语言/TFM | 职责 | 谁主要改它 |
|---|---|---|---|
| `NLMCL.Abstractions` | C# / netstandard2.0 | 端口接口 + 跨层 DTO。**改动需评审**，因为它是全项目的稳定契约 | 架构负责人 |
| `NLMCL.Core` | C# / net48 | 领域逻辑：路径、版本编排、实例、账号、设置、快照 | 后端/逻辑开发者 |
| `NLMCL.Core.Pcl` | **C#** / net48 | PCL 的 C# 部分（McVersion / McInstallManifest / Constants 等）的原样落脚点 | 后端开发者 |
| `NLMCL.App.ViewModels` | C# / net48 | 展示逻辑、命令、状态。**禁止引用 WPF** | 前端/逻辑开发者 |
| `NLMCL.App` | C# / net48 WPF | XAML、样式、控件、组合根 | 前端开发者 |
| `NLMCL.Tests` | C# / net48 | 单测 + XAML 冒烟测试 | 全员 |

这样拆的直接好处：**UI 开发者与后端开发者改的是不同程序集**，日常几乎不会产生合并冲突。Contracts 是唯一需要交叉的地方，因此它被刻意做得小而稳定。

## 5. 技术选型与理由

| 关注点 | 选型 | 理由 |
|---|---|---|
| 目标框架 | **net48** | VB 适配层只能跑 net48；PCL 本身也是 net48 |
| 语言分工 | VB（适配）+ C# 12（新代码） | 满足 §2.3，不写转换器 |
| UI | **WPF**（XAML） | 与 PCL 同栈，可借鉴其控件设计；net48 上 VB/C# 混编最稳 |
| MVVM | **CommunityToolkit.Mvvm 8.x** | 源生成器减少样板；netstandard2.0，net48 可用 |
| DI | **Microsoft.Extensions.DependencyInjection 8** | 标准化、可替换、便于测试 |
| 测试 | **xUnit 2.9 + xunit.runner.console** | net48 下 VSTest 适配器不稳，控制台 runner 更可靠 |
| 包版本 | **中央包管理**（`Directory.Packages.props`） | 多人协作时版本只在一处改 |
| 编译选项 | `Nullable=enable`、`LangVersion=12`、分析器开启 | 让编译器早发现空引用与坏味道 |
| 构建 | `dotnet build`（SDK 8，无需 Visual Studio） | 新人 clone 即能构建，零 IDE 依赖 |

## 6. 这套架构如何服务"长期维护 + 多人协作"

不是口号，是具体机制：

1. **编译期边界**：`NLMCL.Core` 想偷偷引用 PCL 会编译失败。架构靠编译器守，不靠自觉。
2. **可测的展示层**：`MainViewModel` 不依赖 WPF → 现有 `MainViewModelTests` 能在毫秒级验证业务行为，不需要开界面。
3. **XAML 冒烟测试**：`MainWindowSmokeTests` 在 STA 线程真实构造窗口，XAML 语法错、资源缺失、绑定目标不存在都会让 CI 变红，不需要人肉点开。
4. **组合根单点**：换实现只改 `App.ConfigureServices`，review 时一眼能看出依赖变化。
5. **中央包管理 + 统一 `Directory.Build.props`**：TFM、语言版本、可空性、分析器全员一致，不会出现"你机器上能编"。
6. **适配层可替换**：`NLMCL.Abstractions` 是 netstandard2.0，将来整体升 .NET 10 时，只需重写 `NLMCL.Core.Pcl` 这一个程序集。

## 7. 构建、测试、运行

```powershell
# 首次（SDK 已按 per-user 安装到 %LOCALAPPDATA%\dsh-dotnet）
.\scripts\build.ps1
.\scripts\test.ps1
.\src\NLMCL.App\bin\Debug\net48\NLMCL.exe
```

已验证的基线（本次实测）：

- 解决方案构建：**0 警告 / 0 错误**
- 测试：**16 个全部通过**（ViewModel 行为、分组导航、JAR 告警、路径、XAML/渲染冒烟，以及 6 条对真实 `.minecraft` 夹具的版本扫描集成测试）
- 构建环境：仅 .NET SDK 8.0.427，**不需要 Visual Studio，不需要管理员权限**
- UI 有端到端渲染快照：`docs/ui-m2-preview.png`（`RenderSnapshotTests` 生成，无需桌面会话）

## 8. 当前进度与里程碑

| 里程碑 | 内容 | 状态 |
|---|---|---|
| **M1** | 分层骨架、契约、MVVM、DI、测试、可运行壳 | ✅ 完成 |
| **M2** | 版本扫描接入 PCL：vendor `McVersion.cs`，实现 `PclVersionCatalog` | ✅ 完成 |
| **M3** | 下载与安装：`IDownloadService` ← PCL `ModNet`；`IVersionInstaller` ← 自写编排（§2.1a 豁免） | ✅ 完成 |
| **M4** | 启动：`IGameLauncher` ← PCL `ModLaunch`，日志分类与多实例控制台 | 已完成 |
| **M5** | 账号 / 实例 / 快照 / 导入：自有 C# 领域逻辑从 Python 版本迁移 | 已完成 |

**M2 的一个关键发现**：PCL 的版本识别逻辑（`McVersion`）**本来就是 C#**，属于 PCL 自己的 `PCLCS` 项目。因此 M2 没有碰任何 VB 代码——原样 vendor `McVersion.cs` / `McInstallManifest.cs` / `Constants.cs`，再补一层兼容垫片（`Shim/PclCompat.cs`，替代未开源的 `MeloongCore` submodule）。VB 只在真正需要 VB-only 模块（`ModDownload` / `ModLaunch`）时才会引入，届时单独建程序集。

## 9. 已知取舍与风险

1. **net48 是冻结平台**。缓解：所有新代码在接口之后，`Abstractions` 用 netstandard2.0，必要时整层替换。
2. **VB 与 C# 混编的认知成本**。缓解：VB 只允许出现在 `NLMCL.Core.Pcl`，其余一律 C#；该目录设 CODEOWNERS。
3. **PCL 的 `ModMain` 是上帝对象**（全局状态 + 导航）。接入时不要把它搬进 Core，应按职责拆成状态容器与导航服务，否则分层会被它腐化。
4. **`Option Strict Off` 的 VB 代码**。适配时必须在边界处显式转换类型，不能让 `Object` 漏进 C# 层。
5. **XAML 冒烟测试不验证绑定表达式的运行时求值**（未 Show 窗口时绑定可能延迟），只验证 XAML 可加载与 DataContext 装配。视觉回归靠 `RenderSnapshotTests` 产出的 PNG 人工比对，还没有像素级自动断言。

> 迁移范围与逐模块去向见 [MIGRATION-MAP.md](MIGRATION-MAP.md)。
> 新人上手、以及「只审不写」的读码顺序见 [READING-GUIDE.md](READING-GUIDE.md)。

---

## 10. 界面分区

侧栏每个导航项带一个 `ShellSection`（见 `Items.cs`）；`MainViewModel.SelectSection` 维护
`IsVersionsSection` / `IsDownloadSection`，`MainWindow` 用它切换右侧页面。

目前已实现两个页面：

| 分区 | 页面 | 数据来源 |
|---|---|---|
| `Versions` | `MainWindow` 内联的版本列表 | `PclVersionCatalog`（PCL `McVersion`） |
| `Download` | `Views/DownloadPage.xaml` | `MojangVersionListProvider` + `VanillaVersionInstaller` |

新增页面时：加一个 `ShellSection` 枚举值 → 在 `SelectSection` 里置位 → 在 `MainWindow` 加一个带
`Visibility` 绑定的 `UserControl`。**不做路由框架**——桌面应用的分区切换用布尔可见性即可，引入
导航框架只会增加一层间接。
