# 打包与发布

## 为什么不能用 `PublishSingleFile`

`dotnet publish -p:PublishSingleFile=true` **只支持 .NET Core 3.0+**。
本项目是 `net48`（受 PCL 的 VB + WPF 约束，见 AGENTS §2.3），所以这条路走不通。

`net48` 的现实方案是 **Costura.Fody**：把引用的 DLL 作为资源嵌进 exe，
运行时通过 `AppDomain.AssemblyResolve` 从内存加载。

## 怎么发

```powershell
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
```

产物：

```
artifacts\publish\NLMCL.exe      约 1.45 MB
```

**只有一个文件**。已实测：把它单独拷进一个空目录，双击能正常启动并显示主窗口。

## 为什么只有 1.45 MB

因为 `net48` 是 **Windows 系统组件**，不需要打包进 exe。

- Win10 1903+ / Win11 默认自带 .NET Framework 4.8
- 更老的系统需要先装一次 .NET Framework 4.8 运行时

对比：原版 `NLMCL.exe`（PyInstaller）86 MB，因为它要把 Python 解释器和全部依赖塞进单文件。
**体积小不等于更优**——代价是依赖系统运行时。

## 已验证 / 未验证

| 项 | 状态 |
|---|---|
| 构建出单个 exe，无同目录 DLL | ✅ Release 输出仅 exe（+ config/pdb，发布时只拷 exe） |
| 空目录独立启动 | ✅ 进程存活、主窗口标题 `NLMCL` |
| 主窗口能创建（说明嵌入的 WPF/Newtonsoft/自有程序集都解析成功） | ✅ |
| 真实游戏启动 / 下载 / 联机 | ❌ 未在发布产物上跑过完整流程 |
| 杀毒软件误报 | ❌ 未测 |

最后两条要说明：单文件打包最容易出问题的地方是**运行时才用到**的程序集
（比如某个只在下载路径里加载的库）。目前只验证了"能启动到主窗口"。

## 发布时要注意

1. **未签名** → 首次运行可能触发 SmartScreen「未知发布者」。
   选择「更多信息 → 仍要运行」。这是原版 NLMCL 也有的提示。
2. **可能被杀毒软件误报**：单文件程序把 DLL 塞进 exe 的资源段，启发式扫描容易误判。
   加白名单或提交误报。
3. **不要分发 `bin\Debug`**：那是有 43 个文件的普通构建。

## 实现细节

`src/NLMCL.App/FodyWeavers.xml`：

```xml
<Weavers>
  <Costura />
</Weavers>
```

默认行为即可：
- `CreateTemporaryAssemblies=false`（默认）→ 从内存加载，不落地 `%TEMP%`
- 压缩（默认开启）→ 1.45 MB 是压缩后的体积

`NLMCL.App.csproj` 里 `Costura.Fody` 带 `PrivateAssets="all"`，
**不要加 `IncludeAssets`**——Fody 会警告缺少 `compile` 部分。
