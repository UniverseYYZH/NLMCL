using System.Threading;
using System.Threading.Tasks;

namespace NLCL.Abstractions
{
    /// <summary>应用级设置（与具体实例无关）。</summary>
    public sealed class AppSettings
    {
        /// <summary>
        /// 上一次使用的 .minecraft 目录。
        /// 用户可能把游戏装在别的盘（很常见），每次启动都重选一次太蠢。
        /// </summary>
        public string? GameDirectory { get; set; }
    }

    /// <summary>应用设置的持久化。</summary>
    public interface IAppSettingsStore
    {
        Task<AppSettings> LoadAsync(CancellationToken cancellationToken);

        Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
    }
}
