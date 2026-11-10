using System;
using System.Collections.Generic;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>侧栏可达的功能分区。界面按它切换右侧页面。</summary>
    public enum ShellSection
    {
        Versions = 0,
        Download = 1,
        Launch = 2,
        Multiplayer = 3,
        LanScan = 4,
        Snapshot = 5,
        Import = 6,
        Settings = 7,

        Skin = 8,

        Mods = 9,

        Ningli = 10
    }

    /// <summary>
    /// 侧栏导航项。是否选中由视图模型驱动（<see cref="IsActive"/>），
    /// 不依赖 ListBox 的选中态——因为侧栏是分组的，用 ListBox 会出现跨组选择冲突。
    /// </summary>
    public sealed partial class NavigationItem : ObservableObject
    {
        public NavigationItem(string title, string description, ShellSection section = ShellSection.Versions, bool topLevel = true)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Description = description ?? string.Empty;
            Section = section;
            IsTopLevel = topLevel;
        }

        public string Title { get; }

        public string Description { get; }

        public ShellSection Section { get; }

        /// <summary>是否出现在 F 顶部主导航；导入/Mod 合并进下载中心后不再单独占一项。</summary>
        public bool IsTopLevel { get; }

        [ObservableProperty]
        private bool _isActive;
    }

    /// <summary>侧栏分组（工作区 / 工具）。</summary>
    public sealed class NavigationGroup
    {
        public NavigationGroup(string caption, IReadOnlyList<NavigationItem> items)
        {
            Caption = caption ?? throw new ArgumentNullException(nameof(caption));
            Items = items ?? throw new ArgumentNullException(nameof(items));
        }

        public string Caption { get; }

        public IReadOnlyList<NavigationItem> Items { get; }
    }

    /// <summary>版本列表行。</summary>
    public sealed partial class VersionItem : ObservableObject
    {
        public VersionItem(MinecraftVersion version, bool clientJarMissing = false, string? gameDirectory = null,
            DateTimeOffset? lastLaunchedAt = null)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

            GameDirectory = gameDirectory ?? string.Empty;
            DirectoryLabel = GameDirectory;

            // 标题用目录名——它才是「实例」的身份。
            // PCL 的 Id 是归一化后的原版版本号（加载器实例会变成 1.21.11，beta 会变成 "Old"），
            // 拿它当标题会出现两行同名、以及 "Old" 这种对用户无意义的字样。
            Name = version.FolderName;
            FolderName = version.FolderName;
            MinecraftVersion = version.Id;
            LoaderHint = DetectLoader(version.FolderName);

            // PCL 对 2013 年前的版本返回 "Old"、对实验性快照返回 "pending"，
            // 这些是识别用的哨兵值，不是给用户看的，因此不生成副标题。
            bool vanillaIsSentinel = string.Equals(version.Id, "Old", StringComparison.OrdinalIgnoreCase)
                || string.Equals(version.Id, "pending", StringComparison.OrdinalIgnoreCase);

            BasedOn = vanillaIsSentinel || string.Equals(version.Id, version.FolderName, StringComparison.Ordinal)
                ? null
                : "基于 " + version.Id;
            HasBasedOn = BasedOn != null;

            Kind = version.Kind;
            KindLabel = ToKindLabel(version.Kind);
            ReleasedLabel = version.ReleasedAt.HasValue
                ? version.ReleasedAt.Value.LocalDateTime.ToString("yyyy-MM-dd") + " 发布"
                : "发布时间未知";

            InstancePath = string.IsNullOrEmpty(GameDirectory)
                ? string.Empty
                : Path.GetFullPath(Path.Combine(GameDirectory, "versions", FolderName));
            _lastLaunchLabel = FormatLastLaunch(lastLaunchedAt);

            Warning = clientJarMissing ? "未找到客户端 JAR，可能无法启动" : null;
            HasWarning = clientJarMissing;
        }

        public string Name { get; }

        /// <summary>这个版本所在的 .minecraft 目录。</summary>
        public string GameDirectory { get; }

        /// <summary>目录短名，用于多目录时区分同名版本。</summary>
        public string DirectoryLabel { get; }

        /// <summary>多个目录时在版本行显示来源路径。</summary>
        [ObservableProperty]
        private bool _showDirectory;

        public bool HasDirectoryLabel => !string.IsNullOrEmpty(DirectoryLabel);

        /// <summary>是否被点选（决定卡片高亮与启动目标）。</summary>
        [ObservableProperty]
        private bool _isSelected;

        public string FolderName { get; }

        /// <summary>基础 MC 版本（如 1.21.1），用于资源搜索预筛。</summary>
        public string MinecraftVersion { get; }

        /// <summary>加载器标识（fabric / forge / neoforge / quilt），原版为空串。</summary>
        public string LoaderHint { get; }

        /// <summary>加载器实例的「基于 x.x.x」副标题；原版为 null。</summary>
        public string? BasedOn { get; }

        public bool HasBasedOn { get; }

        public VersionKind Kind { get; }

        public string KindLabel { get; }

        public string ReleasedLabel { get; }

        /// <summary>实例目录的绝对路径；最后启动时间用它作为稳定 key。</summary>
        public string InstancePath { get; }

        /// <summary>世界库卡片上的“最后启动”标签；从未成功启动时为“从未启动”。</summary>
        [ObservableProperty]
        private string _lastLaunchLabel;

        /// <summary>启动成功后立即刷新当前卡片，不必等下一次扫描。</summary>
        public void MarkLaunched(DateTimeOffset at)
        {
            LastLaunchLabel = FormatLastLaunch(at);
        }

        private static string FormatLastLaunch(DateTimeOffset? at)
        {
            return at.HasValue
                ? "最后启动 " + at.Value.LocalDateTime.ToString("yyyy-MM-dd")
                : "从未启动";
        }

        /// <summary>卡片上的告警行，null 表示无告警。</summary>
        public string? Warning { get; }

        public bool HasWarning { get; }

        /// <summary>F 世界卡片上的加载器短标签。</summary>
        public string LoaderLabel => LoaderHint switch
        {
            "fabric" => "Fabric",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "quilt" => "Quilt",
            _ => Kind == VersionKind.Snapshot ? "快照" : "原版"
        };

        /// <summary>F 世界卡片上的单字加载器徽标。</summary>
        public string LoaderBadgeText => LoaderHint switch
        {
            "fabric" => "F",
            "forge" => "F",
            "neoforge" => "N",
            "quilt" => "Q",
            _ => Kind == VersionKind.Snapshot ? "S" : "V"
        };
        /// <summary>从目录名推断加载器。</summary>
        private static string DetectLoader(string folder)
        {
            if (folder.IndexOf("neoforge", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "neoforge";
            }

            if (folder.IndexOf("fabric", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "fabric";
            }

            if (folder.IndexOf("forge", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "forge";
            }

            if (folder.IndexOf("quilt", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "quilt";
            }

            return string.Empty;
        }


        private static string ToKindLabel(VersionKind kind)
        {
            switch (kind)
            {
                case VersionKind.Release:
                    return "正式版";
                case VersionKind.Snapshot:
                    return "快照";
                case VersionKind.OldAlpha:
                    return "Alpha";
                case VersionKind.OldBeta:
                    return "Beta";
                default:
                    return "未知";
            }
        }
    }

    /// <summary>
    /// 设置页「Java 运行时」列表里的一项。
    /// Path 为空表示"自动"（按实例要求挑）。
    /// </summary>
    public sealed partial class JavaOption : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        /// <summary>是否为当前选中的默认 Java（单选按钮绑定）。</summary>
        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private bool _isSelected;

        public JavaOption(string? path, string title, string subtitle)
        {
            Path = path;
            Title = title;
            Subtitle = subtitle;
        }

        /// <summary>javaw.exe 的路径；null 表示自动。</summary>
        public string? Path { get; }

        public string Title { get; }

        public string Subtitle { get; }

        public bool IsAuto => string.IsNullOrEmpty(Path);
    }
}
