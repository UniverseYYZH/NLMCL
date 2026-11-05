using System;
using System.Collections.Generic;
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

        Mods = 9
    }

    /// <summary>
    /// 侧栏导航项。是否选中由视图模型驱动（<see cref="IsActive"/>），
    /// 不依赖 ListBox 的选中态——因为侧栏是分组的，用 ListBox 会出现跨组选择冲突。
    /// </summary>
    public sealed partial class NavigationItem : ObservableObject
    {
        public NavigationItem(string title, string description, ShellSection section = ShellSection.Versions)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Description = description ?? string.Empty;
            Section = section;
        }

        public string Title { get; }

        public string Description { get; }

        public ShellSection Section { get; }

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
        public VersionItem(MinecraftVersion version, bool clientJarMissing = false)
        {
            if (version == null)
            {
                throw new ArgumentNullException(nameof(version));
            }

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

            Warning = clientJarMissing ? "未找到客户端 JAR，可能无法启动" : null;
            HasWarning = clientJarMissing;
        }

        public string Name { get; }

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

        /// <summary>卡片上的告警行，null 表示无告警。</summary>
        public string? Warning { get; }

        public bool HasWarning { get; }

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
}
