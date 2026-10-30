using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLCL.Abstractions
{
    /// <summary>单个实例（版本）的启动设置。</summary>
    public sealed class InstanceSettings
    {
        public const int DefaultMemoryMb = 2048;

        /// <summary>分配内存（MB）。</summary>
        public int MemoryMb { get; set; } = DefaultMemoryMb;

        /// <summary>
        /// 指定的 Java 可执行文件路径。为空表示自动挑选（挑已探测到的最高版本）。
        /// </summary>
        public string? JavaPath { get; set; }

        /// <summary>额外的游戏参数，按空格分词。</summary>
        public string? ExtraArguments { get; set; }

        /// <summary>直连服务器地址。</summary>
        public string? ServerAddress { get; set; }

        /// <summary>把额外参数按空白切开（忽略多余空格）。</summary>
        public IReadOnlyList<string> ParsedExtraArguments()
        {
            if (string.IsNullOrWhiteSpace(ExtraArguments))
            {
                return Array.Empty<string>();
            }

            return ExtraArguments!
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    /// <summary>按实例读写启动设置。</summary>
    public interface IInstanceSettingsStore
    {
        Task<InstanceSettings> LoadAsync(
            string minecraftDirectory, string versionId, CancellationToken cancellationToken);

        Task SaveAsync(
            string minecraftDirectory, string versionId, InstanceSettings settings,
            CancellationToken cancellationToken);
    }
}
