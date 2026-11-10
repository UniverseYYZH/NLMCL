using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>应用级设置（与具体实例无关）。</summary>
    public sealed class AppSettings
    {
        /// <summary>当前程序支持的设置结构版本。</summary>
        public const int CurrentSchemaVersion = 8;

        /// <summary>设置结构版本；旧配置没有此字段时按 1 处理。</summary>
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        /// <summary>
        /// 设置文件来自更高版本时为 true：只读，避免旧程序覆盖新字段。
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>只读原因；用于界面提示。</summary>
        public string? ReadOnlyReason { get; set; }

        /// <summary>
        /// 上一次使用的 .minecraft 目录。
        /// 用户可能把游戏装在别的盘（很常见），每次启动都重选一次太蠢。
        /// </summary>
        public string? GameDirectory { get; set; }

        /// <summary>已添加的多个游戏目录；扫描时全部扫描，当前目录仍是 <see cref="GameDirectory"/>。</summary>
        public List<string> GameDirectories { get; set; } = new List<string>();

        /// <summary>
        /// 微软登录用的 Azure 应用 client id。
        /// 不内置（与 PCL 一致），用户在登录弹层里填一次、存在这里；
        /// 环境变量 <c>NLMCL_MS_CLIENT_ID</c> 仍优先。
        /// </summary>
        public string? MicrosoftClientId { get; set; }

        /// <summary>外观主题："light" 或 "dark"。</summary>
        public string Theme { get; set; } = "light";

        /// <summary>减少动效：跳过提示条的滑入/淡出动画。</summary>
        public bool ReduceMotion { get; set; }

        /// <summary>普通窗口状态的界面缩放（1.0 = 100%）。</summary>
        public double UiScale { get; set; } = 1.0;

        /// <summary>最大化窗口状态的界面缩放（默认 150%）。</summary>
        public double UiScaleMaximized { get; set; } = 1.5;

        /// <summary>下载线程数（对应 PCL 的 ToolDownloadThread）。</summary>
        public int DownloadThreads { get; set; } = 6;

        /// <summary>
        /// 下载限速（MB/s）；0 表示不限速。
        /// 对应 PCL 的 ToolDownloadSpeed，实际编码见 <see cref="DownloadSpeedLimits"/>。
        /// </summary>
        public int DownloadSpeedLimitMb { get; set; }

        /// <summary>新实例的默认内存（MB）。</summary>
        public int DefaultMemoryMb { get; set; } = 2048;

        /// <summary>
        /// 全局 JVM 参数（如 -XX:+UseG1GC），启动时拼在实例参数之前。
        /// 实例自己的"额外参数"仍然生效，两者都会追加。
        /// </summary>
        public string? DefaultJvmArgs { get; set; }

        /// <summary>
        /// 全局游戏参数（只传给游戏本体，如 --demo），启动时拼在实例游戏参数之前。
        /// 不要在这里放 -Xmx 等 JVM 参数，那些属于 <see cref="DefaultJvmArgs"/>。
        /// </summary>
        public string? DefaultGameArgs { get; set; }

        /// <summary>
        /// 下载是否走国内镜像（BMCLAPI）。关掉就走官方源，慢但不经过第三方。
        /// </summary>
        public bool UseMirror { get; set; } = true;

        /// <summary>
        /// 全局默认 Java 的可执行文件路径（javaw.exe）。
        /// 为空表示「自动」：按实例要求挑选最高的兼容版本。
        /// </summary>
        public string? DefaultJavaPath { get; set; }

        /// <summary>首次启动引导是否已完成（跳过/关掉也算完成，避免反复弹）。</summary>
        public bool WizardCompleted { get; set; }

        /// <summary>启动页策略："versions" / "multiplayer" / "download" / "last"。</summary>
        public string DefaultPage { get; set; } = "versions";

        /// <summary>上一次页面；DefaultPage=last 时使用。</summary>
        public string LastPage { get; set; } = "versions";

        /// <summary>
        /// 每个实例最后一次成功启动的时间，key 为实例目录的绝对路径。
        /// 只用于世界库展示；下载页仍显示版本发布时间。
        /// </summary>
        public Dictionary<string, DateTimeOffset> LastLaunched { get; set; } =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        /// <summary>启动游戏前是否自动创建快照。</summary>
        public bool AutoSnapshotBeforeLaunch { get; set; }

        /// <summary>自动快照保留数量。</summary>
        public int SnapshotRetention { get; set; } = 5;

        /// <summary>关闭窗口行为："exit" / "tray" / "ask"。</summary>
        public string CloseBehavior { get; set; } = "exit";
    }

    /// <summary>应用设置的持久化。</summary>
    public interface IAppSettingsStore
    {
        Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 串行化的设置服务：把「读—改—写」收在一处，避免多个设置项同时保存时互相覆盖。
    /// </summary>
    public interface IAppSettingsService
    {
        Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

        Task UpdateAsync(Action<AppSettings> mutate, CancellationToken cancellationToken);

        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
    }
}
