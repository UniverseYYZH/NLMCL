# 已挂起：PCL 的 VB 核心抽取（M3 前置）

**状态：不在 `NLCL.sln` 中，不参与构建。**

## 这里有什么

| 文件 | 来源 | 说明 |
|---|---|---|
| `Vendor/ModBase.vb` | `Modules/Base/ModBase.vb` | Loader 框架、`RunInUi`、线程工具（1217 行） |
| `Vendor/ModNet.vb` | `Modules/Base/ModNet.vb` | HTTP 下载、镜像、重试、断点（1934 行） |
| `Vendor/ModLoader.vb` | `Modules/Base/ModLoader.vb` | `LoaderBase` / `LoaderTask` + 加载器安装（792 行） |
| `Shim/LoadingShim.vb` | 复刻 `Controls/MyLoading.xaml.vb` | `ILoadingTrigger`、`MyLoadingState`、`MyLoadingStateSimulator` |
| `lib/*.dll` | PCL `Resources/` | CacheCow、System.Net.Http.Formatting、ThrottleDebounce |

以上 VB 文件均为**逐字节复制**，未做任何修改。

## 为什么挂起

编译器已经把缺口逼到 4 个符号：

```
FrmMain        （主窗口）
FrmSpeedLeft   （下载速度面板）
Settings       （全局设置单例，定义在 ModMain.vb）
GetIdentify    （ModMain.vb）
```

也就是说，`ModLoader.vb` 虽然只有 9 处窗体引用，但 `LoaderBase` 就写在它里面，而 `LoaderBase` 会去读写 `FrmSpeedLeft` 的进度、`Settings` 的线程数/限速——**PCL 的加载框架本身就长在窗体上**。

错误数演进（同一份代码，逐步补依赖）：

| 步骤 | 错误数 | 主要缺口 |
|---|---:|---|
| 初次编译 | 98 | 缺项目级 `Imports`、`LoaderBase`、`FileChecker` |
| 补 `Imports` + 搬 `ModLoader.vb` | 44 | MeloongCore 类型（`IWorker`/`ProgressProvider`/`ConcurrentList`/`FileChecker`） |
| 接入 MeloongCore（Apache-2.0） | 16 | `ILoadingTrigger` / `MyLoadingState` |
| 补 LoadingShim | **72** | 暴露真问题：`FrmMain` / `FrmSpeedLeft` / `Settings` / `GetIdentify` |

最后一步错误数反而上升，是因为前面的类型错误把引用**掩盖**了；一旦类型齐了，VB 才开始报"未声明"。

## 恢复方式

1. 把本工程加回解决方案。
2. 补 `Settings`（可自建 JSON 实现，键名沿用 `ToolDownloadThread` / `ToolDownloadSpeed` / `SystemDebugSkipCopy`）。
3. 补 `FrmMain` / `FrmSpeedLeft` 的**空壳窗体**，或改 `ModLoader.vb` 里那 9 处引用为无 UI 的进度回调。

第 3 步是真正的分叉点：改 VB 源码意味着**偏离上游**，日后无法逐字节 diff；不改就得伪造两个窗体。
两条路都需要先做决策，见 `docs/M3-NOTES.md`。
