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

        private readonly ITranslator? _translator;

        private readonly IFilePicker? _filePicker;
        private readonly IModpackInstaller? _modpackInstaller;
        private readonly IJavaLocator? _javaLocator;

        public ResourceViewModel(
            IResourceCatalog catalog,
            IResourceInstaller installer,
            ITranslator? translator = null,
            IFilePicker? filePicker = null,
            IModpackInstaller? modpackInstaller = null,
            IJavaLocator? javaLocator = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _translator = translator;
            _filePicker = filePicker;
            _modpackInstaller = modpackInstaller;
            _javaLocator = javaLocator;
        }

        public IReadOnlyList<string> KindOptions { get; } = new[] { "模组", "资源包", "光影", "数据包" };

        public IReadOnlyList<string> LoaderOptions { get; } = new[] { "不限", "Fabric", "Forge", "NeoForge" };

        public ObservableCollection<ResourceSummary> Results { get; } = new ObservableCollection<ResourceSummary>();

        /// <summary>每页条数（Modrinth 单次上限 50，20 是兼顾速度和滚动体验的值）。</summary>
        private const int PageSize = 20;

        /// <summary>是否还有下一页（上一页正好返回满页）。</summary>
        [ObservableProperty]
        private bool _hasMore;

        /// <summary>结果计数文案。</summary>
        [ObservableProperty]
        private string _resultsCountText = string.Empty;

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

        /// <summary>整合包安装进度 0–100。</summary>
        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private bool _hasProgress;

        /// <summary>当前实例所在的 .minecraft 与版本目录（由主界面设置）。</summary>
        public string MinecraftDirectory { get; set; } = string.Empty;

        public string VersionFolder { get; set; } = string.Empty;

        partial void OnSelectedKindChanged(string value)
        {
            OnPropertyChanged(nameof(IsModKind));
        }

        /// <summary>资源包/光影/数据包没有加载器维度，界面上要隐藏加载器下拉。</summary>
        public bool IsModKind => string.Equals(SelectedKind, "模组", StringComparison.Ordinal);

        /// <summary>
        /// 进入「导入资源」页时自动查一次热门内容，避免空白页。
        /// 用户已经搜过（Results 非空）就不打扰。
        /// </summary>
        public async Task EnsureLoadedAsync()
        {
            if (Results.Count > 0 || IsBusy)
            {
                return;
            }

            await SearchAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// 进入页面时按**当前实例**预筛：把游戏版本和加载器填上，再查一次。
        /// 不带筛的"全部热门"对玩家没什么用——大多装不进这个实例。
        /// </summary>
        public async Task EnsureLoadedForInstanceAsync(string? gameVersion, string? loader)
        {
            if (Results.Count > 0 || IsBusy)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                GameVersion = gameVersion!;
            }

            if (!string.IsNullOrWhiteSpace(loader))
            {
                SelectedLoader = LoaderDisplayName(loader!);
            }

            await SearchAsync().ConfigureAwait(true);
        }
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
                // Modrinth 只有英文元数据：中文名先按别名换成英文再搜
                string text = SearchText;
                string? alias = NLMCL.Core.Resources.ChineseAliases.TryMap(text);
                string note = string.Empty;

                if (alias != null && !string.Equals(alias, text, StringComparison.OrdinalIgnoreCase))
                {
                    text = alias;
                    note = "（按英文名 " + alias + " 搜索）";
                }
                else if (_translator != null && NLMCL.Core.Resources.ChineseAliases.ContainsChinese(text))
                {
                    // 别名表命中不了的中文名，交给翻译接口兜底
                    string? translated = await _translator
                        .TranslateToEnglishAsync(text, CancellationToken.None)
                        .ConfigureAwait(true);

                    if (!string.IsNullOrWhiteSpace(translated))
                    {
                        text = translated!;
                        note = "（已翻译为 " + translated + " 搜索）";
                    }
                }

                string? gameVersion = string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim();
                string? loader = LoaderOf(SelectedLoader);

                (IReadOnlyList<ResourceSummary> found, string relaxed) = await SearchWithFallbackAsync(
                    text, gameVersion, loader).ConfigureAwait(true);

                Results.Clear();
                foreach (ResourceSummary item in found)
                {
                    Results.Add(item);
                }

                if (found.Count == 0)
                {
                    StatusText = "没有找到匹配的资源" + note
                        + "。可以试试英文名，或去掉游戏版本/加载器筛选。";
                    return;
                }

                HasMore = found.Count >= PageSize;
                ResultsCountText = "已显示 " + Results.Count + " 个" + (HasMore ? "（还有更多）" : string.Empty);
                StatusText = "找到 " + found.Count + " 个结果" + note + relaxed;
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

        /// <summary>
        /// 搜索并按需放宽条件：
        /// 先「版本 + 加载器」；0 条则去掉加载器；仍然 0 条则去掉版本。
        ///
        /// 为什么必须这样：很多模组只更新到某个旧版本（例如 Yes Steve Model 只到 1.20.1），
        /// 用户当前实例是 1.21.2 时，带版本筛就永远搜不到。
        /// </summary>
        public async Task<(IReadOnlyList<ResourceSummary> Found, string Relaxed)> SearchWithFallbackAsync(
            string text, string? gameVersion, string? loader)
        {
            ResourceKind kind = KindOf(SelectedKind);

            var attempts = new (string? Version, string? Loader, string Note)[]
            {
                (gameVersion, loader, string.Empty),
                (gameVersion, null, "（已忽略加载器筛选）"),
                (null, null, "（已忽略版本与加载器筛选）")
            };

            IReadOnlyList<ResourceSummary> last = Array.Empty<ResourceSummary>();
            (string? Version, string? Loader)? previous = null;

            foreach ((string? version, string? currentLoader, string note) in attempts)
            {
                // 条件没变化就别重复请求（例如本来就没选加载器）
                if (previous != null && previous.Value.Version == version && previous.Value.Loader == currentLoader)
                {
                    continue;
                }

                previous = (version, currentLoader);

                ResourceQuery query = new ResourceQuery(text, kind)
                {
                    GameVersion = version,
                    Loader = currentLoader
                };

                last = await _catalog.SearchAsync(query, CancellationToken.None).ConfigureAwait(true);

                if (last.Count > 0)
                {
                    return (last, note);
                }
            }

            return (last, string.Empty);
        }

        /// <summary>再取下一页（Modrinth 用 offset 翻页），追加到现有列表。</summary>
        [RelayCommand]
        public async Task LoadMoreAsync()
        {
            if (IsBusy || !HasMore)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在加载更多…";

            try
            {
                ResourceKind kind = KindOf(SelectedKind);
                string? gameVersion = string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim();

                ResourceQuery query = new ResourceQuery(SearchText, kind)
                {
                    GameVersion = gameVersion,
                    Loader = LoaderOf(SelectedLoader),
                    Offset = Results.Count
                };

                IReadOnlyList<ResourceSummary> found = await _catalog
                    .SearchAsync(query, CancellationToken.None).ConfigureAwait(true);

                foreach (ResourceSummary item in found)
                {
                    Results.Add(item);
                }

                HasMore = found.Count >= PageSize;
                ResultsCountText = "已显示 " + Results.Count + " 个" + (HasMore ? "（还有更多）" : string.Empty);
                StatusText = HasMore ? "已加载 " + Results.Count + " 个" : "已加载全部 " + Results.Count + " 个";
            }
            catch (Exception ex)
            {
                StatusText = "加载更多失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

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

        /// <summary>
        /// 从实例目录名里提取游戏版本：优先取最后一个 1.x 形式的，
        /// 因为 "fabric-loader-0.16.9-1.20.1" 的第一个匹配是加载器版本。
        /// </summary>
        public static string ExtractGameVersion(string? folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return string.Empty;
            }

            MatchCollection matches = Regex.Matches(folderName!, @"\d+\.\d+(\.\d+)?");

            if (matches.Count == 0)
            {
                return string.Empty;
            }

            // 从后往前找第一个 1.x，找不到就用最后一个匹配
            for (int index = matches.Count - 1; index >= 0; index--)
            {
                if (matches[index].Value.StartsWith("1.", StringComparison.Ordinal))
                {
                    return matches[index].Value;
                }
            }

            return matches[matches.Count - 1].Value;
        }

        /// <summary>导入 .mrpack：读索引 → 装加载器 → 建隔离实例 → 落 mods/overrides。</summary>
        [RelayCommand]
        public async Task ImportModpackAsync()
        {
            if (_filePicker == null || _modpackInstaller == null)
            {
                StatusText = "整合包导入不可用。";
                return;
            }

            if (string.IsNullOrWhiteSpace(MinecraftDirectory))
            {
                StatusText = "请先在「本地版本」选一个实例，再导入整合包（决定装到哪个 .minecraft）。";
                return;
            }

            string? path = _filePicker.PickFile(
                "选择 Modrinth 整合包", "Modrinth 整合包 (*.mrpack)|*.mrpack|所有文件 (*.*)|*.*");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            IsBusy = true;
            HasProgress = true;
            Progress = 0;

            try
            {
                ModpackInfo info = await _modpackInstaller
                    .ReadAsync(path!, CancellationToken.None)
                    .ConfigureAwait(true);

                StatusText = "准备安装「" + info.Name + "」（" + info.Summary + "）…";

                string java = ResolveJava(info.MinecraftVersion);

                string versionId = await _modpackInstaller
                    .InstallAsync(path!, MinecraftDirectory, java, new InlineProgress(ApplyProgress), CancellationToken.None)
                    .ConfigureAwait(true);

                Progress = 100;
                StatusText = "已创建实例「" + versionId + "」，去「本地版本」里启动它。";
            }
            catch (Exception ex)
            {
                StatusText = "导入失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyProgress(InstallProgress value)
        {
            Progress = value.Percent;
            StatusText = value.Stage;
        }

        /// <summary>Forge/NeoForge 安装器需要 Java；Fabric 其实不需要，但统一挑一个更省事。</summary>
        private string ResolveJava(string minecraftVersion)
        {
            if (_javaLocator == null)
            {
                return string.Empty;
            }

            IReadOnlyList<JavaRuntime> javas = _javaLocator
                .FindInstalledAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            int required = DownloadViewModel.RequiredJavaFor(minecraftVersion);

            JavaRuntime? java = NLMCL.Core.Installing.JavaRequirements.Pick(javas, required)
                ?? NLMCL.Core.Installing.JavaRequirements.Pick(javas, null);

            return java?.ExecutablePath ?? string.Empty;
        }

        private sealed class InlineProgress : IProgress<InstallProgress>
        {
            private readonly Action<InstallProgress> _apply;

            public InlineProgress(Action<InstallProgress> apply)
            {
                _apply = apply;
            }

            public void Report(InstallProgress value) => _apply(value);
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

        /// <summary>把加载器标识（fabric/forge/neoforge）映射成下拉里的显示名。</summary>
        internal static string LoaderDisplayName(string loader)
        {
            switch (loader.ToLowerInvariant())
            {
                case "fabric":
                    return "Fabric";
                case "forge":
                    return "Forge";
                case "neoforge":
                    return "NeoForge";
                default:
                    return "不限";
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

            // 目录名可能是 "1.20.1-Fabric 0.20.1" / "fabric-loader-0.16.9-1.20.1" 这类，
            // Modrinth 只认纯游戏版本，所以取最后一个 "1.x" 形式的版本号
            GameVersion = ExtractGameVersion(versionFolder);

            if (versionFolder.IndexOf("Fabric", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SelectedLoader = "Fabric";
            }
        }
    }
}
