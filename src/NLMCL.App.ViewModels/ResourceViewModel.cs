using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 「导入资源」页：在线搜索 Modrinth 的模组 / 资源包 / 光影 / 数据包，一键装进当前实例。
    ///
    /// 安装目录由 <see cref="IResourceInstaller"/> 决定（隔离实例优先），
    /// 这里只负责搜索、筛选和状态展示。
    /// </summary>
    public sealed partial class ResourceViewModel : ObservableObject
    {
        private readonly IResourceCatalog _catalog;
        private readonly IResourceInstaller _installer;

        public ResourceViewModel(IResourceCatalog catalog, IResourceInstaller installer)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        }

        public IReadOnlyList<string> KindOptions { get; } = new[] { "模组", "资源包", "光影", "数据包" };

        public IReadOnlyList<string> LoaderOptions { get; } = new[] { "不限", "Fabric", "Forge", "NeoForge" };

        public ObservableCollection<ResourceSummary> Results { get; } = new ObservableCollection<ResourceSummary>();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedKind = "模组";

        [ObservableProperty]
        private string _selectedLoader = "Fabric";

        /// <summary>限定游戏版本；由主界面按当前选中实例带进来。</summary>
        [ObservableProperty]
        private string _gameVersion = string.Empty;

        [ObservableProperty]
        private string _statusText = "输入关键字搜索资源（数据来自 Modrinth）";

        [ObservableProperty]
        private bool _isBusy;

        /// <summary>当前实例所在的 .minecraft 与版本目录（由主界面设置）。</summary>
        public string MinecraftDirectory { get; set; } = string.Empty;

        public string VersionFolder { get; set; } = string.Empty;

        partial void OnSelectedKindChanged(string value)
        {
            OnPropertyChanged(nameof(IsModKind));
        }

        /// <summary>资源包/光影/数据包没有加载器维度，界面上要隐藏加载器下拉。</summary>
        public bool IsModKind => string.Equals(SelectedKind, "模组", StringComparison.Ordinal);

        [RelayCommand]
        public async Task SearchAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在搜索…";

            try
            {
                ResourceQuery query = new ResourceQuery(SearchText, KindOf(SelectedKind))
                {
                    GameVersion = string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim(),
                    Loader = LoaderOf(SelectedLoader)
                };

                IReadOnlyList<ResourceSummary> found = await _catalog
                    .SearchAsync(query, CancellationToken.None)
                    .ConfigureAwait(true);

                Results.Clear();
                foreach (ResourceSummary item in found)
                {
                    Results.Add(item);
                }

                StatusText = found.Count == 0
                    ? "没有找到匹配的资源"
                    : "找到 " + found.Count + " 个结果";
            }
            catch (Exception ex)
            {
                StatusText = "搜索失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task InstallAsync(ResourceSummary? item)
        {
            if (item == null || IsBusy)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(MinecraftDirectory))
            {
                StatusText = "请先选择一个本地版本（决定装到哪个实例）";
                return;
            }

            IsBusy = true;
            StatusText = "正在获取「" + item.Title + "」的可用版本…";

            try
            {
                IReadOnlyList<ResourceFile> files = await _catalog
                    .GetFilesAsync(
                        item.Id,
                        item.Kind,
                        string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim(),
                        LoaderOf(SelectedLoader),
                        CancellationToken.None)
                    .ConfigureAwait(true);

                if (files.Count == 0)
                {
                    StatusText = "「" + item.Title + "」没有匹配当前游戏版本/加载器的文件";
                    return;
                }

                ResourceFile file = files[0];

                StatusText = "正在下载 " + file.Name + "…";

                string path = await _installer
                    .InstallAsync(file, item.Kind, MinecraftDirectory, VersionFolder, CancellationToken.None)
                    .ConfigureAwait(true);

                StatusText = "已安装到 " + path;
            }
            catch (Exception ex)
            {
                StatusText = "安装失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        internal static ResourceKind KindOf(string text)
        {
            switch (text)
            {
                case "资源包":
                    return ResourceKind.ResourcePack;
                case "光影":
                    return ResourceKind.ShaderPack;
                case "数据包":
                    return ResourceKind.DataPack;
                default:
                    return ResourceKind.Mod;
            }
        }

        internal static string? LoaderOf(string text)
        {
            switch (text)
            {
                case "Fabric":
                    return "fabric";
                case "Forge":
                    return "forge";
                case "NeoForge":
                    return "neoforge";
                default:
                    return null;
            }
        }

        /// <summary>按实例名猜加载器（Fabric 实例的目录名一般带 Fabric）。</summary>
        public void SyncFromInstance(string minecraftDirectory, string versionFolder)
        {
            MinecraftDirectory = minecraftDirectory;
            VersionFolder = versionFolder;

            // 目录名可能是 "1.20.1-Fabric 0.20.1" 这类，Modrinth 只认纯游戏版本
            Match match = Regex.Match(versionFolder ?? string.Empty, @"\d+\.\d+(\.\d+)?");
            GameVersion = match.Success ? match.Value : (versionFolder ?? string.Empty);

            if (versionFolder.IndexOf("Fabric", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SelectedLoader = "Fabric";
            }
        }
    }
}
