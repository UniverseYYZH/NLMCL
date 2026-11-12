using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NLMCL.Abstractions
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

        /// <summary>窗口宽度（0 = 不指定，用游戏默认）。</summary>
        public int WindowWidth { get; set; }

        /// <summary>窗口高度（0 = 不指定）。</summary>
        public int WindowHeight { get; set; }

        /// <summary>是否全屏启动。</summary>
        public bool Fullscreen { get; set; }

        /// <summary>是否让该实例走 HTTP/HTTPS 代理。</summary>
        public bool UseProxy { get; set; }

        /// <summary>代理主机（UseProxy 为 true 时生效）。</summary>
        public string? ProxyHost { get; set; }

        /// <summary>代理端口（UseProxy 为 true 时生效）。</summary>
        public int ProxyPort { get; set; }

        /// <summary>
        /// 允许 LWJGL 回退到软件 OpenGL。只对使用 LWJGL2 的旧版本有效，
        /// 新版本（LWJGL3）会忽略该属性。
        /// </summary>
        public bool SoftwareOpenGl { get; set; }

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

        /// <summary>这个实例是否已经有设置文件（没有就说明是新建的，可以用全局默认值）。</summary>
        bool Exists(string minecraftDirectory, string versionId);
    }

    /// <summary>
    /// 解析某个实例实际使用的游戏目录（.minecraft 根或 versions\&lt;id&gt;\）。
    /// 版本隔离规则由 PCL 决定（版本目录里有 mods/saves 会自动隔离），
    /// 启动模组和「Mod 管理」必须用同一个答案，否则会像以前那样对不上。
    /// </summary>
    public interface IInstancePathResolver
    {
        string GetGameDirectory(string minecraftDirectory, string versionId);
    }
}
