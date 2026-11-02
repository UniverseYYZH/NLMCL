using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
{
    /// <summary>可下载的原版版本（来自 Mojang 版本清单）。</summary>
    public sealed class VanillaVersion
    {
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
    public interface IVersionListProvider
    {
        Task<IReadOnlyList<VanillaVersion>> GetVersionsAsync(CancellationToken cancellationToken);
    }
}
