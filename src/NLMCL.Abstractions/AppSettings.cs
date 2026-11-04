using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>应用级设置（与具体实例无关）。</summary>
    public sealed class AppSettings
    {
        /// <summary>
        /// 上一次使用的 .minecraft 目录。
        /// 用户可能把游戏装在别的盘（很常见），每次启动都重选一次太蠢。
        /// </summary>
        public string? GameDirectory { get; set; }

        /// <summary>
        /// 微软登录用的 Azure 应用 client id。
        /// 不内置（与 PCL 一致），用户在登录弹层里填一次、存在这里；
        /// 环境变量 <c>NLMCL_MS_CLIENT_ID</c> 仍优先。
        /// </summary>
        public string? MicrosoftClientId { get; set; }

        /// <summary>外观主题："light" 或 "dark"。</summary>
        public string Theme { get; set; } = "light";

        /// <summary>普通窗口状态的界面缩放（1.0 = 100%）。</summary>
        public double UiScale { get; set; } = 1.0;

        /// <summary>最大化窗口状态的界面缩放（默认 150%）。</summary>
        public double UiScaleMaximized { get; set; } = 1.5;

        /// <summary>下载线程数（对应 PCL 的 ToolDownloadThread）。</summary>
        public int DownloadThreads { get; set; } = 6;

        /// <summary>新实例的默认内存（MB）。</summary>
        public int DefaultMemoryMb { get; set; } = 2048;

        /// <summary>首次启动引导是否已完成（跳过/关掉也算完成，避免反复弹）。</summary>
        public bool WizardCompleted { get; set; }
    }

    /// <summary>应用设置的持久化。</summary>
    public interface IAppSettingsStore
    {
        Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
    }
}
