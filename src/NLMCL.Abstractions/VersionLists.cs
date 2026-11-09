using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>可下载的原版版本（来自 Mojang 版本清单）。</summary>
    public sealed class VanillaVersion : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isInstalled;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// 本地是否已安装（下载页用来打标）。安装完成后由 ViewModel 置位，
        /// 因此需要 INotifyPropertyChanged，否则列表不会刷新。
        /// </summary>
        public bool IsInstalled
        {
            get => _isInstalled;
            set
            {
                if (_isInstalled == value)
                {
                    return;
                }

                _isInstalled = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsInstalled)));
            }
        }

        public VanillaVersion(string id, string type, DateTimeOffset? releasedAt)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Type = type ?? string.Empty;
            ReleasedAt = releasedAt;
        }

        /// <summary>版本名，例如 1.21.1。</summary>
        public string Id { get; }

        /// <summary>清单里的原始类型：release / snapshot / old_beta / old_alpha。</summary>
        public string Type { get; }

        public DateTimeOffset? ReleasedAt { get; }

        /// <summary>界面上显示的短标签。</summary>
        public string TypeLabel
        {
            get
            {
                switch (Type)
                {
                    case "release":
                        return "正式版";
                    case "snapshot":
                        return "快照";
                    case "old_beta":
                        return "Beta";
                    case "old_alpha":
                        return "Alpha";
                    default:
                        return Type;
                }
            }
        }
    }

    /// <summary>获取可下载的版本列表。</summary>
    /// <summary>某个游戏版本可用的 Fabric 加载器版本（按 meta 的推荐顺序）。</summary>
    public interface IFabricLoaderCatalog
    {
        Task<IReadOnlyList<string>> GetLoaderVersionsAsync(string gameVersion, CancellationToken cancellationToken);
    }

    /// <summary>下载栈的运行参数（线程数等）。由 PCL 侧实现。</summary>
    public interface IDownloadTuning
    {
        void ApplyThreadCount(int threads);

        /// <summary>设置下载限速；0 表示不限速，单位为 MB/s。</summary>
        void ApplySpeedLimit(int megabytesPerSecond);
    }

    public interface IVersionListProvider
    {
        Task<IReadOnlyList<VanillaVersion>> GetVersionsAsync(CancellationToken cancellationToken);
    }
}
