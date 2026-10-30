# Vendor —— 来自 PCL 的 VB 源码

这里的 `.vb` 文件来自 [PCL](https://github.com/Meloong-Git/PCL) 的 `Plain Craft Launcher 2\Modules\`，
**不要手工修改**。所有适配改动只写在 `../Shim/` 与 `../*.vb` 适配器里。

## 文件清单

| 文件 | 上游路径 | 状态 |
|---|---|---|
| `ModBase.vb` | `Modules\Base\ModBase.vb` | 逐字节复制 |
| `ModNet.vb` | `Modules\Base\ModNet.vb` | 逐字节复制 |
| `ModLoader.vb` | `Modules\Base\ModLoader.vb` | 逐字节复制 |
| `ModMinecraft.McInstance.vb` | `Modules\Minecraft\ModMinecraft.vb` **切分** | 见下 |
| `ModMinecraft.Helpers.vb` | `Modules\Minecraft\ModMinecraft.vb` **切分**（1554–2152）+ `McLibEntry`（1499–1548） | M4 需要库/资源枚举 |
| `ModLaunch.Arguments.vb` | `Modules\Minecraft\ModLaunch.vb` **切分**（1191–1655）+ `GetNativesFolder`（1726–1737） | M4 的启动参数管线 |
| `ModLaunch.Natives.vb` | `Modules\Minecraft\ModLaunch.vb` **切分**（1659–1722，#Region "解压 Natives"） | 启动前解压 natives；漏了它 → `lwjgl.dll` 找不到 |

## 两处改动代码内容的地方

1. `ModLaunch.Arguments.vb` 里 5 个入口由 `Private` 放宽为 **`Friend`**，以便同程序集内的
   C#/VB 适配器调用（`McLaunchArgumentMain` / `McLaunchArgumentsJVM` / `McLaunchArgumentsGame` /
   `McLaunchArgumentsReplace` / `DeduplicateJavaArguments`）。**函数体一行未改。**
2. `ModLaunch.Natives.vb` 里唯一的入口 `McLaunchNatives` 由 `Private` 放宽为 **`Friend`**，
   供 `PclLaunchCommandBuilder.PrepareNatives` 调用。**函数体一行未改。**

除这两处可见性调整外，其余切分均逐字一致。

## 切分说明（B1 方案）

`McInstance` 没有独立文件，与 `ModMinecraft` 的模块级 UI 代码（登录、页面跳转、资源广场、
动画开关、配置）同处一个 2154 行的文件，导致整份文件无法脱离 UI 编译。

因此按行号**原样切分**：

| 切分块 | 上游行号 | 行数 | 说明 |
|---|---|---:|---|
| `McInstance` 类 | 218–867 | 650 | 主体；已校验与上游**逐字一致** |
| `McInstanceCardType` 枚举 | 1348–1357 | 10 | 模块级声明，但被 `McInstance.Load()` 使用 |

切分只改变**存放位置**，不改变任何一行代码；文件头部记录了原始行号。

## 上游更新后如何重新切分

```powershell
# 1. 重新拉取 PCL，确认 McInstance 的起止行号（行号会漂移）
Select-String -Path "<PCL>\Modules\Minecraft\ModMinecraft.vb" -Pattern "^\s*Public Class McInstance|^\s*Public Enum McInstanceCardType"

# 2. 用切分脚本重出本文件（脚本逻辑：从 Class 行起做 Class/Enum/Structure 深度计数，直到深度归零）
# 3. 校验：切出的主体应与上游对应行区间逐字相同
```

## 已知的未解依赖簇（M3-B 阻塞点）

尝试加入 `ModDownload.vb` 时发现，它与以下文件构成**不可分簇**：

```
ModDownload.vb (1389)
  └─ ModMinecraft 的模块级工具函数（McLib* / McAssets*，约 600 行）
       ↕ 循环依赖（工具函数反过来调用 ModDownload 的 DlSource* 函数）
  └─ ResourceVersion.vb (427)
       └─ ResourceProject.vb (712)          ← 资源广场
            ├─ WikiEntry（PCLCS 的 C#）+ WikiEntries.txt
            └─ MyImage / MyResourceItem     ← WPF 控件
```

也就是说：**想要 `ModDownload`，就得把整个"资源广场"界面层一起搬进来。**
详见 `../../docs/M3-NOTES.md` §8。

## 许可

上游 PCL 为自定义《PCL 分发有限许可》，本项目按私人使用前提直接复用（见仓库根 `AGENTS.md` 第 3 节）。
**一旦对外分发，必须重新评估该许可的全部约束。**
