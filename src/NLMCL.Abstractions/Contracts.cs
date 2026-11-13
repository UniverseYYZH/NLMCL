using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 应用级路径约定。任何层都从这里取路径，禁止各自拼接 %APPDATA%。
    /// </summary>
    public interface IAppPaths
    {
        /// <summary>是否使用便携模式（数据放在 exe 同目录的 data 下）。</summary>
        bool IsPortable { get; }

        /// <summary>%APPDATA%\NLMCL，便携模式下为 exe\data。</summary>
        string DataRoot { get; }

        string SettingsFile { get; }

        string AccountsFile { get; }

        string InstancesDirectory { get; }

        string SkinDirectory { get; }

        string SnapshotDirectory { get; }

        /// <summary>默认游戏目录（%APPDATA%\.minecraft）</summary>
        string DefaultMinecraftDirectory { get; }

        /// <summary>确保自有的数据目录存在，不触碰游戏目录。</summary>
        void EnsureDirectories();
    }

    public enum VersionKind
    {
        Release,
        Snapshot,
        OldAlpha,
        OldBeta,
        Unknown
    }

    /// <summary>已安装的本地版本。</summary>
    public sealed class MinecraftVersion
    {
        public MinecraftVersion(string id, string folderName, VersionKind kind, DateTimeOffset? releasedAt,
            bool hasClientJar = true)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            FolderName = folderName ?? throw new ArgumentNullException(nameof(folderName));
            Kind = kind;
            ReleasedAt = releasedAt;
            HasClientJar = hasClientJar;
        }

        /// <summary>版本 JSON 中的 id。</summary>
        public string Id { get; }

        /// <summary>versions\ 下的目录名，可能带加载器后缀。</summary>
        public string FolderName { get; }

        public VersionKind Kind { get; }

        public DateTimeOffset? ReleasedAt { get; }

        /// <summary>客户端 JAR 是否存在。false 时 UI 提示"可能无法启动"。</summary>
        public bool HasClientJar { get; }
    }

    /// <summary>
    /// 本地版本目录读取端口。
    /// 由 <c>NLMCL.Core.Pcl</c> 用 PCL 的成熟实现来适配，UI 与业务层不感知其来源。
    /// </summary>
    public interface IVersionCatalog
    {
        Task<IReadOnlyList<MinecraftVersion>> GetInstalledAsync(
            string minecraftDirectory,
            CancellationToken cancellationToken);
    }

    public sealed class InstallRequest
    {
        public InstallRequest(string minecraftDirectory, string versionId)
        {
            MinecraftDirectory = minecraftDirectory ?? throw new ArgumentNullException(nameof(minecraftDirectory));
            VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
        }

        public string MinecraftDirectory { get; }

        public string VersionId { get; }

        /// <summary>加载器类型：null 表示原版，"fabric" / "forge" / "neoforge" / "quilt"。</summary>
        public string? Loader { get; set; }

        public string? LoaderVersion { get; set; }

        /// <summary>
        /// 同时下载的文件数。资源动辄几千个，串行会慢到不可用；
        /// 默认 8，实现方应夹在 1–16 之间（再多容易被服务器限速）。
        /// </summary>
        public int Parallelism { get; set; } = 8;
    }

    public sealed class InstallProgress
    {
        public InstallProgress(
            string stage,
            double percent,
            long bytesPerSecond,
            long bytesReceived = 0,
            long bytesTotal = 0)
        {
            Stage = stage;
            Percent = percent;
            BytesPerSecond = bytesPerSecond;
            BytesReceived = bytesReceived;
            BytesTotal = bytesTotal;
        }

        public string Stage { get; }

        /// <summary>0-100，契约要求单调不减。</summary>
        public double Percent { get; }

        public long BytesPerSecond { get; }

        /// <summary>已下载字节；0 表示当前阶段没有字节信息。</summary>
        public long BytesReceived { get; }

        /// <summary>本组总字节；0 表示未知。</summary>
        public long BytesTotal { get; }

        public bool HasByteProgress => BytesReceived > 0 || BytesTotal > 0;
    }

    /// <summary>版本安装端口，由 PCL 的下载/安装实现适配。</summary>
    public interface IVersionInstaller
    {
        Task InstallAsync(
            InstallRequest request,
            IProgress<InstallProgress> progress,
            CancellationToken cancellationToken);
    }

    public sealed class LaunchRequest
    {
        public LaunchRequest(string minecraftDirectory, string versionId)
        {
            MinecraftDirectory = minecraftDirectory ?? throw new ArgumentNullException(nameof(minecraftDirectory));
            VersionId = versionId ?? throw new ArgumentNullException(nameof(versionId));
        }

        public string MinecraftDirectory { get; }

        public string VersionId { get; }

        public int MaximumMemoryMb { get; set; } = 2048;

        /// <summary>直连服务器地址，null 表示不直连。</summary>
        public string? ServerAddress { get; set; }

        /// <summary>
        /// 用哪个 Java 启动。为空时由实现自行决定（例如读设置或回落到 PATH）。
        /// 调用方先探测再传进来，避免启动器替用户乱选版本。
        /// </summary>
        public string? JavaExecutable { get; set; }

        /// <summary>
        /// 实际使用的 Java 版本。参数管线要按它决定生成哪一档 JVM 参数
        /// （17 与 25 的编码/GC 参数不同）。为空时实现方按自己的默认值处理。
        /// </summary>
        public Version? JavaVersion { get; set; }

        /// <summary>
        /// 额外的 JVM 参数（会插到 -cp 之前，例如外置登录的
        /// <c>-javaagent:authlib-injector.jar=&lt;apiRoot&gt;</c>）。
        /// 由实现方负责与用户自己的 JVM 参数共存，不覆盖。
        /// </summary>
        public string? ExtraJvmArguments { get; set; }

        /// <summary>玩家名。为空时用占位名 Player。</summary>
        /// <summary>额外的游戏参数（--width / --height / --fullscreen 等），追加在命令行末尾。</summary>

        /// <summary>窗口宽度（0 = 不指定）。</summary>
        public int WindowWidth { get; set; }

        /// <summary>窗口高度（0 = 不指定）。</summary>
        public int WindowHeight { get; set; }

        /// <summary>是否全屏启动。</summary>
        public bool Fullscreen { get; set; }
        public IReadOnlyList<string> ExtraGameArguments { get; set; } = Array.Empty<string>();

        public string? PlayerName { get; set; }

        /// <summary>玩家 UUID（不带花括号）。</summary>
        public string? PlayerUuid { get; set; }

        /// <summary>访问令牌；离线账号传占位值即可。</summary>
        public string? AccessToken { get; set; }

        /// <summary>
        /// 传给游戏的 <c>--userType</c>。离线账号用 <c>legacy</c>，避免 Realms
        /// 把占位令牌当 JWT 解析；正版/外置登录仍用 <c>msa</c>。
        /// </summary>
        public string UserType { get; set; } = "msa";

        /// <summary>从账号一次性填好身份三件套。</summary>
        public LaunchRequest WithAccount(Account? account)
        {
            if (account == null)
            {
                return this;
            }

            PlayerName = account.Name;
            PlayerUuid = account.Uuid;
            AccessToken = account.EffectiveAccessToken;
            return this;
        }
    }

    public sealed class LaunchResult
    {
        public LaunchResult(int processId, bool succeeded, string message)
        {
            ProcessId = processId;
            Succeeded = succeeded;
            Message = message;
        }

        public int ProcessId { get; }

        public bool Succeeded { get; }

        public string Message { get; }
    }

    /// <summary>游戏启动端口，由 PCL 的 ModLaunch 适配。</summary>
    public interface IGameLauncher
    {
        Task<LaunchResult> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken);
    }
}
