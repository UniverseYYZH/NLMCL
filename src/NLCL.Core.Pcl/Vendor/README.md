# Vendor —— 来自 PCL 的源码

这些文件**逐字节复制**自 [PCL](https://github.com/Meloong-Git/PCL) 的 `PCLCS` 项目，不要手工修改。

| 文件 | 上游路径 | 说明 |
|---|---|---|
| `McVersion.cs` | `PCLCS/Minecraft/McVersion.cs` | 版本号识别、加载器识别、快照/愚人节判定 |
| `McInstallManifest.cs` | `PCLCS/Minecraft/McInstallManifest.cs` | 安装清单与组件模型 |
| `Constants.cs` | `PCLCS/Constants.cs` | `ModLoaders` 等枚举 |
| `MCUtils.cs` | `PCLCS/Minecraft/MCUtils.cs` | 版本名比较（M3-B 起需要） |
| `WikiEntry.cs` | `PCLCS/Resource/WikiEntry.cs` | 资源百科条目；**当前未使用**，M3-B2 否决后保留作参考 |

## 为什么要搬这一份

PCL 的版本 JSON 解析逻辑**本来就是 C#**（作者已经在把 VB 往 C# 迁）。直接用它，比抄 VB 的 `ModMinecraft.vb` 省事得多，也不需要 VB/C# 混编。

## 上游依赖与本仓库的处理

上游 `PCLCS.csproj` 依赖两个外部东西：

1. **Newtonsoft.Json** —— 本项目直接加包引用（版本见 `Directory.Packages.props`）。
2. **MeloongCore** —— 提供 `RegexSeek`、`Capitalize`、`GetOrDefault`、`FileUtils`、`Logger` 等。
   本项目**直接引用** `third_party/MeloongCore/Shared`（Apache-2.0），不再自建近似垫片。

`PolySharp` 用于在 net48 上补齐 `record` / 集合表达式所需的编译器支持类型。

## 更新方式

```powershell
# 拉取上游后重新复制，然后核对哈希
Copy-Item <PCL>/PCLCS/Minecraft/McVersion.cs          .\Vendor\McVersion.cs
Copy-Item <PCL>/PCLCS/Minecraft/McInstallManifest.cs  .\Vendor\McInstallManifest.cs
Copy-Item <PCL>/PCLCS/Constants.cs                    .\Vendor\Constants.cs
Get-FileHash .\Vendor\*.cs -Algorithm SHA256
```

## 许可

上游 PCL 为自定义《PCL 分发有限许可》，本项目按私人使用前提直接复用（见仓库根 `AGENTS.md` 第 3 节）。
**一旦对外分发，必须重新评估该许可的全部约束。**
