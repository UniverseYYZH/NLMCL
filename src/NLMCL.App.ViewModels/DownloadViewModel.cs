using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 版本下载页的展示逻辑：拉取可下载版本、选择目标目录、执行安装并上报进度。
    /// 与主窗口的 ViewModel 一样，**不引用任何 WPF 类型**，因此可单测。
    /// </summary>
    public sealed partial class DownloadViewModel : ObservableObject
    {
        private readonly IVersionListProvider _provider;
        private readonly IVersionInstaller _installer;
        private readonly IAppPaths _paths;

        private readonly IFabricLoaderCatalog? _loaderCatalog;
        private readonly IVersionCatalog? _catalog;
        private readonly IForgeCatalog? _forgeCatalog;
        private readonly INeoForgeCatalog? _neoForgeCatalog;
        private readonly ILoaderInstaller? _forgeInstaller;
        private readonly IJavaLocator? _javaLocator;

        public DownloadViewModel(
            IVersionListProvider provider,
            IVersionInstaller installer,
            IAppPaths paths,
            IFabricLoaderCatalog? loaderCatalog = null,
            IVersionCatalog? catalog = null,
            IForgeCatalog? forgeCatalog = null,
            ILoaderInstaller? forgeInstaller = null,
            IJavaLocator? javaLocator = null,
            INeoForgeCatalog? neoForgeCatalog = null)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _loaderCatalog = loaderCatalog;
            _catalog = catalog;
            _forgeCatalog = forgeCatalog;
            _forgeInstaller = forgeInstaller;
            _javaLocator = javaLocator;
            _neoForgeCatalog = neoForgeCatalog;

            TargetDirectory = paths.DefaultMinecraftDirectory;
        }

        public ObservableCollection<VanillaVersion> Versions { get; } = new ObservableCollection<VanillaVersion>();

        [ObservableProperty]
        private VanillaVersion? _selectedVersion;

        /// <summary>一个版本安装成功。主窗口订阅它来自动刷新本地列表。</summary>
        public event EventHandler? InstallCompleted;

        [ObservableProperty]
        private string _targetDirectory = string.Empty;



        /// <summary>可选加载器。空串表示原版。</summary>


        public IReadOnlyList<string> Loaders { get; } = new[] { "原版", "Fabric", "Forge", "NeoForge" };



        [ObservableProperty]
        private string _selectedLoader = "原版";



        /// <summary>该游戏版本可选的 Fabric 加载器版本（来自 meta）。</summary>
        public ObservableCollection<string> LoaderVersions { get; } = new ObservableCollection<string>();

        /// <summary>Fabric 加载器版本（原版安装时忽略）。</summary>


        [ObservableProperty]
        private string _loaderVersion = "0.20.1";



        public bool IsFabricSelected => string.Equals(SelectedLoader, "Fabric", StringComparison.OrdinalIgnoreCase);

        public bool IsForgeSelected => string.Equals(SelectedLoader, "Forge", StringComparison.OrdinalIgnoreCase);

        public bool IsNeoForgeSelected => string.Equals(SelectedLoader, "NeoForge", StringComparison.OrdinalIgnoreCase);

        /// <summary>Fabric 与 Forge 都需要选加载器版本，原版不用。</summary>
        public bool IsLoaderVersionVisible => IsFabricSelected || IsForgeSelected || IsNeoForgeSelected;



        partial void OnSelectedLoaderChanged(string value)


        {
            OnPropertyChanged(nameof(IsFabricSelected));
            OnPropertyChanged(nameof(IsForgeSelected));
            OnPropertyChanged(nameof(IsNeoForgeSelected));
            OnPropertyChanged(nameof(IsLoaderVersionVisible));

            if (IsLoaderVersionVisible)
            {
                _ = ReloadLoaderVersionsAsync();
            }
        }

        partial void OnSelectedVersionChanged(VanillaVersion? value)
        {
            if (IsLoaderVersionVisible)
            {
                _ = ReloadLoaderVersionsAsync();
            }
        }

        /// <summary>按当前游戏版本拉取可用的加载器版本（Fabric 走 meta，Forge 走 BMCLAPI）。</summary>
        public async Task ReloadLoaderVersionsAsync()
        {
            if (SelectedVersion == null)
            {
                return;
            }

            try
            {
                IReadOnlyList<string> versions;

                if (IsForgeSelected || IsNeoForgeSelected)
                {
                    IReadOnlyList<LoaderBuild> builds;

                    if (IsNeoForgeSelected)
                    {
                        if (_neoForgeCatalog == null)
                        {
                            StatusText = "NeoForge 列表不可用";
                            return;
                        }

                        builds = await _neoForgeCatalog
                            .GetBuildsAsync(SelectedVersion.Id, CancellationToken.None)
                            .ConfigureAwait(true);
                    }
                    else
                    {
                        if (_forgeCatalog == null)
                        {
                            StatusText = "Forge 列表不可用";
                            return;
                        }

                        builds = await _forgeCatalog
                            .GetBuildsAsync(SelectedVersion.Id, CancellationToken.None)
                            .ConfigureAwait(true);
                    }

                    versions = builds.Select(build => build.Version).ToList();
                }
                else
                {
                    if (_loaderCatalog == null)
                    {
                        return;
                    }

                    versions = await _loaderCatalog
                        .GetLoaderVersionsAsync(SelectedVersion.Id, CancellationToken.None)
                        .ConfigureAwait(true);
                }

                LoaderVersions.Clear();
                foreach (string version in versions)
                {
                    LoaderVersions.Add(version);
                }

                if (LoaderVersions.Count > 0)
                {
                    LoaderVersion = LoaderVersions[0];
                }

                StatusText = SelectedVersion.Id + " 可选加载器版本 " + LoaderVersions.Count + " 个";
            }
            catch (Exception ex)
            {
                StatusText = "获取加载器版本失败：" + ex.Message;
            }
        }

        /// <summary>按本地已安装版本刷新标记；没有目录服务时静默跳过。</summary>
        private async Task MarkInstalledFromCatalogAsync()
        {
            if (_catalog == null)
            {
                return;
            }

            try
            {
                IReadOnlyList<MinecraftVersion> installed = await _catalog
                    .GetInstalledAsync(TargetDirectory, CancellationToken.None)
                    .ConfigureAwait(true);

                MarkInstalled(installed.Select(version => version.Id));
            }
            catch (Exception)
            {
                // 扫描失败不影响下载列表本身
            }
        }

        /// <summary>把本地已安装的版本打上标记（安装完成后主窗口会再调一次）。</summary>
        public void MarkInstalled(IEnumerable<string> installedIds)
        {
            HashSet<string> installed = new HashSet<string>(installedIds, StringComparer.OrdinalIgnoreCase);

            foreach (VanillaVersion version in Versions)
            {
                version.IsInstalled = installed.Contains(version.Id);
            }
        }

        [ObservableProperty]
        private string _statusText = "尚未加载版本列表";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isLoading;

        /// <summary>并发下载数（跟随设置里的下载线程）。</summary>
        public int Parallelism { get; set; } = 8;

        /// <summary>安装进度 0–100；未开始时为 0。</summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>是否已开始安装（决定进度条是否显示）。</summary>
        [ObservableProperty]
        private bool _hasProgress;

        // ── 筛选与安装弹层 ────────────────────────────────────────────────
        public IReadOnlyList<string> FilterOptions { get; } = new[] { "全部", "正式版", "快照" };

        [ObservableProperty]
        private string _selectedFilter = "全部";

        [ObservableProperty]
        private string _searchText = string.Empty;

        /// <summary>筛选后的列表（界面绑这个，不再绑 Versions）。</summary>
        public ObservableCollection<VanillaVersion> FilteredVersions { get; } =
            new ObservableCollection<VanillaVersion>();

        partial void OnSelectedFilterChanged(string value) => ApplyFilter();

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        /// <summary>按类型与关键字筛选；关键字不区分大小写。</summary>
        public void ApplyFilter()
        {
            IEnumerable<VanillaVersion> query = Versions;

            if (string.Equals(SelectedFilter, "正式版", StringComparison.Ordinal))
            {
                query = query.Where(version =>
                    string.Equals(version.Type, "release", StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(SelectedFilter, "快照", StringComparison.Ordinal))
            {
                query = query.Where(version =>
                    string.Equals(version.Type, "snapshot", StringComparison.OrdinalIgnoreCase));
            }

            string term = (SearchText ?? string.Empty).Trim();

            if (term.Length > 0)
            {
                query = query.Where(version =>
                    version.Id.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            FilteredVersions.Clear();

            foreach (VanillaVersion version in query)
            {
                FilteredVersions.Add(version);
            }
        }

        /// <summary>安装弹层是否打开（仿 PCL：点版本 → 弹层选加载器 → 装）。</summary>
        [ObservableProperty]
        private bool _isInstallPanelOpen;

        /// <summary>弹层里要安装的版本。</summary>
        [ObservableProperty]
        private VanillaVersion? _installTarget;

        public string InstallTargetLabel => InstallTarget?.Id ?? "未选择版本";

        [RelayCommand]
        public async Task OpenInstallAsync(VanillaVersion? version)
        {
            if (version == null)
            {
                return;
            }

            InstallTarget = version;
            SelectedVersion = version;
            OnPropertyChanged(nameof(InstallTargetLabel));
            IsInstallPanelOpen = true;

            if (IsFabricSelected)
            {
                await ReloadLoaderVersionsAsync().ConfigureAwait(true);
            }
        }

        [RelayCommand]
        public void CloseInstall()
        {
            IsInstallPanelOpen = false;
        }

        /// <summary>筛选按钮（全部 / 正式版 / 快照）。</summary>
        [RelayCommand]
        public void SelectFilter(string? filter)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                SelectedFilter = filter!;
            }
        }

        /// <summary>拉取可下载版本列表。</summary>
        [RelayCommand]
        public async Task LoadVersionsAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsLoading = true;
            StatusText = "正在获取版本列表…";

            try
            {
                IReadOnlyList<VanillaVersion> versions = await _provider
                    .GetVersionsAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                Versions.Clear();
                foreach (VanillaVersion version in versions)
                {
                    Versions.Add(version);
                }

                // 列表拉回来后立刻标一次"已安装"（主窗口扫描时可能还没加载过这个列表）
                await MarkInstalledFromCatalogAsync().ConfigureAwait(true);

                // 默认选第一个正式版：清单按发布时间倒序，第一项通常是最新快照
                SelectedVersion = Versions.FirstOrDefault(version => version.Type == "release")
                    ?? (Versions.Count > 0 ? Versions[0] : null);
                ApplyFilter();

                StatusText = Versions.Count == 0
                    ? "版本列表为空"
                    : "共 " + Versions.Count + " 个可下载版本";
            }
            catch (Exception ex)
            {
                StatusText = "获取版本列表失败：" + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>进入「版本下载」页时自动拉一次列表（已有数据就不重复请求）。</summary>
        public async Task EnsureLoadedAsync()
        {
            if (Versions.Count > 0 || IsLoading)
            {
                return;
            }

            await LoadVersionsAsync().ConfigureAwait(true);
        }
        private CancellationTokenSource? _installCts;

        /// <summary>下载/安装过程中是否允许取消。</summary>
        [ObservableProperty]
        private bool _canCancel;

        /// <summary>取消正在进行的下载（给用户反悔的机会）。</summary>
        [RelayCommand]
        private void CancelInstall()
        {
            _installCts?.Cancel();
            StatusText = "正在取消…";
        }

        /// <summary>安装当前选中的版本。</summary>
        [RelayCommand]
        public async Task InstallAsync()
        {
            if (IsBusy)
            {
                return;
            }

            VanillaVersion? target = InstallTarget ?? SelectedVersion;

            if (target == null)
            {
                StatusText = "请先选择一个版本";
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetDirectory))
            {
                StatusText = "请先指定安装目录";
                return;
            }

            IsBusy = true;
            CanCancel = true;
            HasProgress = true;
            Progress = 0;
            StatusText = "准备安装 " + target!.Id;

            _installCts = new CancellationTokenSource();
            CancellationToken token = _installCts.Token;

            try
            {
                if (IsForgeSelected || IsNeoForgeSelected)
                {
                    await InstallModdedLoaderAsync(target!, token).ConfigureAwait(true);
                }
                else
                {
                    InstallRequest request = new InstallRequest(TargetDirectory, target!.Id)
                    {
                        Parallelism = Parallelism
                    };

                    if (IsFabricSelected)
                    {
                        request.Loader = "fabric";
                        request.LoaderVersion = LoaderVersion;
                    }

                    await _installer
                        .InstallAsync(request, new InlineProgress(ApplyProgress), token)
                        .ConfigureAwait(true);

                    Progress = 100;
                    StatusText = "已安装 " + target!.Id + " 到 " + TargetDirectory;
                }

                await MarkInstalledFromCatalogAsync().ConfigureAwait(true);
                ApplyFilter();
                IsInstallPanelOpen = false;

                InstallCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                StatusText = "已取消安装（已下载的文件会保留，下次接着用）";
            }
            catch (Exception ex)
            {
                StatusText = "安装失败：" + ex.Message;
            }
            finally
            {
                _installCts?.Dispose();
                _installCts = null;
                CanCancel = false;
                IsBusy = false;
            }
        }

        /// <summary>Forge 走安装器 jar：需要挑一个能跑安装器的 Java。</summary>
        private async Task InstallModdedLoaderAsync(VanillaVersion target, CancellationToken token)
        {
            if (_forgeInstaller == null || _javaLocator == null)
            {
                throw new InvalidOperationException("加载器安装不可用（未装配安装器或 Java 探测）。");
            }

            if (string.IsNullOrWhiteSpace(LoaderVersion))
            {
                throw new InvalidOperationException(
                    "该游戏版本没有可用的 " + SelectedLoader + " 版本；换一个游戏版本或加载器再试。");
            }

            IReadOnlyList<JavaRuntime> javas = await _javaLocator
                .FindInstalledAsync(CancellationToken.None)
                .ConfigureAwait(true);

            int required = RequiredJavaFor(target.Id);

            JavaRuntime? java = NLMCL.Core.Installing.JavaRequirements.Pick(javas, required)
                ?? NLMCL.Core.Installing.JavaRequirements.Pick(javas, null);

            if (java == null)
            {
                throw new InvalidOperationException("没有可用的 Java，无法运行 Forge 安装器。");
            }

            string installedId = IsNeoForgeSelected
                ? await _forgeInstaller
                    .InstallNeoForgeAsync(
                        target.Id, LoaderVersion, TargetDirectory, java.ExecutablePath,
                        new InlineProgress(ApplyProgress), token)
                    .ConfigureAwait(true)
                : await _forgeInstaller
                    .InstallForgeAsync(
                        target.Id, LoaderVersion, TargetDirectory, java.ExecutablePath,
                        new InlineProgress(ApplyProgress), token)
                    .ConfigureAwait(true);

            Progress = 100;
            StatusText = "已安装 " + installedId + " 到 " + TargetDirectory
                + "（Java " + java.Major + "）";
        }

        /// <summary>Forge 安装器需要与游戏版本匹配的 Java：1.20.5+ 用 21，更早用 17。</summary>
        public static int RequiredJavaFor(string minecraftVersion)
        {
            string[] parts = (minecraftVersion ?? string.Empty).Split('.');
            int major = parts.Length > 1 && int.TryParse(parts[1], out int m) ? m : 0;

            if (major > 20)
            {
                return 21;
            }

            if (major == 20 && parts.Length > 2 && int.TryParse(parts[2], out int patch) && patch >= 5)
            {
                return 21;
            }

            return 17;
        }

        private void ApplyProgress(InstallProgress value)
        {
            Progress = value.Percent;
            StatusText = value.Stage;
        }

        /// <summary>
        /// 同步转发的进度实现。刻意不用 <see cref="Progress{T}"/>——
        /// 后者在没有同步上下文时会往线程池投递，测试里断言不到最终值。
        /// </summary>
        private sealed class InlineProgress : IProgress<InstallProgress>
        {
            private readonly Action<InstallProgress> _apply;

            public InlineProgress(Action<InstallProgress> apply)
            {
                _apply = apply;
            }

            public void Report(InstallProgress value)
            {
                _apply(value);
            }
        }
    }
}
