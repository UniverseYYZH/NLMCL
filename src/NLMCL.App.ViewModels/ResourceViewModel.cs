using System.IO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;
using NLMCL.Core.Resources;

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
    private readonly ICurseForgeInstaller? _curseForge;
    private readonly NLMCL.Core.Resources.CurseForgeCatalog? _curseForgeCatalog;

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
            IJavaLocator? javaLocator = null,
        ICurseForgeInstaller? curseForge = null,
            NLMCL.Core.Resources.CurseForgeCatalog? curseForgeCatalog = null,
            IFileDownloader? fileDownloader = null,
            DownloadQueue? queue = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _translator = translator;
            _filePicker = filePicker;
            _modpackInstaller = modpackInstaller;
        _curseForge = curseForge;
        _curseForgeCatalog = curseForgeCatalog;
            _fileDownloader = fileDownloader;
            _javaLocator = javaLocator;
            _queue = queue ?? new DownloadQueue();
        }

        private readonly DownloadQueue _queue;

        public IReadOnlyList<string> KindOptions { get; } = new[] { "模组", "资源包", "光影", "数据包" };

    partial void OnSelectedSourceChanged(string value)
    {
        // 换来源立刻重搜：不然用户会以为"切了没反应"
        _ = SearchAsync();
    }
    /// <summary>临时诊断：记录资源安装链路，排查"点了没反应"。</summary>
    internal static void Trace(string message)
    {
        try
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "nlmcl-resource.log");
            System.IO.File.AppendAllText(
                path, System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + System.Environment.NewLine);
        }
        catch (System.IO.IOException)
        {
        }
    }
    /// <summary>当前是否选的是 CurseForge。</summary>
    public bool IsCurseForge => string.Equals(SelectedSource, "CurseForge", StringComparison.Ordinal);

    /// <summary>资源来源：Modrinth（默认）或 CurseForge（走 mcim 镜像）。</summary>
    public IReadOnlyList<string> SourceOptions { get; } = new[] { "Modrinth", "CurseForge" };

    [ObservableProperty]
    private string _selectedSource = "Modrinth";

        public IReadOnlyList<string> LoaderOptions { get; } = new[] { "不限", "Fabric", "Forge", "NeoForge" };

        public ObservableCollection<ResourceSummary> Results { get; } = new ObservableCollection<ResourceSummary>();

    /// <summary>CurseForge 项目链接（cfwidget 没有搜索，只能按链接装）。</summary>
    [ObservableProperty]
    private string _curseForgeUrl = string.Empty;

    /// <summary>是否显示 CurseForge 行（未注入实现时隐藏）。</summary>
    public bool ShowCurseForgeRow => _curseForge != null;

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


        /// <summary>自带下载器：PCL 的 ModNet 拉 Modrinth 的 CDN 会报"小文件下载失败"，</summary>
        /// <summary>资源下载统一走它（和 CurseForge 同样的绕法）。</summary>
        private readonly IFileDownloader? _fileDownloader;

        private CancellationTokenSource? _installCts;

        /// <summary>下载过程中是否允许取消。</summary>
        [ObservableProperty]
        private bool _canCancel;

        /// <summary>取消正在进行的资源下载（用户反悔的出口）。</summary>
        [RelayCommand]
        private void CancelInstall()
        {
            _installCts?.Cancel();
            StatusText = "正在取消下载…";
        }
        /// <summary>整合包安装进度 0–100。</summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>字节进度文案：12.3 MB / 45.6 MB · 速度 · 剩余。</summary>
        [ObservableProperty]
        private string _progressDetails = string.Empty;

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

                if (IsCurseForge && _curseForgeCatalog != null)
                {
                    ResourceQuery cfQuery = new ResourceQuery(text, KindOf(SelectedKind))
                    {
                        GameVersion = gameVersion,
                        Loader = loader
                    };

                    IReadOnlyList<ResourceSummary> cfFound = await _curseForgeCatalog
                        .SearchAsync(cfQuery, CancellationToken.None).ConfigureAwait(true);

                    Results.Clear();
                    foreach (ResourceSummary cfItem in cfFound)
                    {
                        Results.Add(cfItem);
                    }

                    HasMore = cfFound.Count >= PageSize;
                    ResultsCountText = "已显示 " + Results.Count + " 个" + (HasMore ? "（还有更多）" : string.Empty);
                    StatusText = cfFound.Count == 0
                        ? "CurseForge 没有找到匹配资源" + note
                        : "CurseForge：找到 " + cfFound.Count + " 个结果" + note;
                    return;
                }

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

        /// <summary>
        /// 从 CurseForge 链接安装（走 cfwidget 只读接口）。
        /// Modrinth 搜不到的独占资源用这条路。
        /// </summary>
        [RelayCommand]
        public async Task InstallFromCurseForgeAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (_curseForge == null)
            {
                StatusText = "CurseForge 支持未启用";
                return;
            }

            if (string.IsNullOrWhiteSpace(MinecraftDirectory) || string.IsNullOrWhiteSpace(VersionFolder))
            {
                StatusText = "请先在「世界库」里选一个实例";
                return;
            }

            if (string.IsNullOrWhiteSpace(CurseForgeUrl))
            {
                StatusText = "请填入 CurseForge 项目链接，例如 https://www.curseforge.com/minecraft/mc-mods/jei";
                return;
            }

            IsBusy = true;
            StatusText = "正在从 CurseForge 获取文件清单…";

            try
            {
                string path = await _curseForge.InstallAsync(
                    CurseForgeUrl,
                    MinecraftDirectory,
                    VersionFolder,
                    string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim(),
                    LoaderOf(SelectedLoader),
                    CancellationToken.None).ConfigureAwait(true);

                StatusText = "已从 CurseForge 安装到 " + path;
            }
            catch (Exception ex)
            {
                StatusText = "CurseForge 安装失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
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

                IResourceCatalog activeCatalog = IsCurseForge && _curseForgeCatalog != null ? _curseForgeCatalog : _catalog;
                IReadOnlyList<ResourceSummary> found = await activeCatalog
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

        /// <summary>
        /// 安装本地 jar：CurseForge 独有、或朋友发来的模组，直接拷进当前实例的 mods 目录。
        /// 只做拷贝与重名检查，不解析依赖——这是"手动兜底"入口，不是安装器。
        /// </summary>
        [RelayCommand]
        public async Task InstallLocalJarAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(MinecraftDirectory) || string.IsNullOrWhiteSpace(VersionFolder))
            {
                StatusText = "请先在「世界库」里选一个实例，再安装本地 jar";
                return;
            }

            if (_filePicker == null)
            {
                StatusText = "当前环境没有文件选择器";
                return;
            }

            string? picked = _filePicker.PickFile("选择要安装的 jar（.jar / .zip）", "模组文件|*.jar;*.zip");

            if (string.IsNullOrWhiteSpace(picked) || !File.Exists(picked))
            {
                return;
            }

            try
            {
                string mods = Path.Combine(MinecraftDirectory, "versions", VersionFolder, "mods");
                Directory.CreateDirectory(mods);

                string target = Path.Combine(mods, Path.GetFileName(picked));
                bool existed = File.Exists(target);

                File.Copy(picked, target, overwrite: true);
                StatusText = (existed ? "已覆盖 " : "已安装 ") + Path.GetFileName(picked) + " → mods";
            }
            catch (Exception ex)
            {
                StatusText = "安装本地 jar 失败：" + ex.Message;
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
        [RelayCommand]
        public async Task InstallAsync(ResourceSummary? item)
        {
            Trace("点击安装：item=" + (item?.Id ?? "null") + " busy=" + IsBusy + " dir=" + (string.IsNullOrEmpty(MinecraftDirectory) ? "(空)" : "有") + " folder=" + (string.IsNullOrEmpty(VersionFolder) ? "(空)" : VersionFolder) + " source=" + SelectedSource);
            if (item == null || IsBusy)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(MinecraftDirectory))
            {
                StatusText = "请先选择一个世界库（决定装到哪个实例）";
                return;
            }

            _installCts = new CancellationTokenSource();
            CancellationToken token = _installCts.Token;
            CanCancel = true;

            IsBusy = true;
            StatusText = "正在获取「" + item.Title + "」的可用版本…";

            try
            {
                IResourceCatalog fileCatalog = IsCurseForge && _curseForgeCatalog != null ? _curseForgeCatalog : _catalog;
                IReadOnlyList<ResourceFile> files = await fileCatalog
                    .GetFilesAsync(
                        item.Id,
                        item.Kind,
                        string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim(),
                        LoaderOf(SelectedLoader),
                token)
                    .ConfigureAwait(true);

                Trace("取到文件 " + files.Count + " 个");
                if (files.Count == 0)
                {
                    StatusText = "「" + item.Title + "」没有匹配当前游戏版本/加载器的文件";
                    return;
                }

                ResourceFile file = files[0];

                if (!IsCurseForge && item.Kind == ResourceKind.Mod)
                {
                    await InstallRequiredDependenciesAsync(
                        file,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { item.Id },
                        token).ConfigureAwait(true);
                }

                StatusText = "正在下载 " + file.Name + "…";

                string path = string.Empty;

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            double lastRatio = 0;
            System.TimeSpan lastAt = System.TimeSpan.Zero;

            Progress<double> installProgress = new Progress<double>(ratio =>
            {
                Progress = ratio * 100;
                HasProgress = true;

                System.TimeSpan now = watch.Elapsed;
                System.TimeSpan elapsed = now - lastAt;
                string speed = string.Empty;
                long received = file.Size > 0 ? (long)(ratio * file.Size) : 0;

                if (elapsed.TotalMilliseconds >= 400 && ratio > lastRatio && file.Size > 0)
                {
                    double bytes = (ratio - lastRatio) * file.Size;
                    double perSecond = bytes / elapsed.TotalSeconds;
                    double remain = perSecond > 1 ? (1 - ratio) * file.Size / perSecond : 0;

                    speed = " · " + FormatSize((long)perSecond) + "/s"
                        + (remain > 0 ? " · 剩余 " + System.TimeSpan.FromSeconds(remain).ToString(@"mm\:ss") : string.Empty);

                    lastRatio = ratio;
                    lastAt = now;
                }

                ProgressDetails = file.Size > 0
                    ? FormatSize(received) + " / " + FormatSize(file.Size) + speed
                    : string.Empty;

                StatusText = "正在下载 " + file.Name + " " + (ratio * 100).ToString("0") + "%" + speed;
            });

            await _queue.RunAsync("安装 " + file.Name, async () =>
            {
                if (IsCurseForge && _curseForge != null)
                {
                    path = await _curseForge
                        .InstallFileAsync(
                            file.Url, file.Name, item.Kind, MinecraftDirectory, VersionFolder, installProgress, token)
                        .ConfigureAwait(true);
                }
                else if (_fileDownloader != null)
                {
                    // PCL 的 ModNet 拉 Modrinth 的 CDN 会报"小文件下载失败"，
                    // 这里也走自带下载器（分片 + 断点续传 + 超时看门狗）。
                    string targetDirectory = Path.Combine(
                        MinecraftDirectory, "versions", VersionFolder, SubdirectoryFor(item.Kind));

                    Directory.CreateDirectory(targetDirectory);

                    string target = Path.Combine(targetDirectory, file.Name);

                    await _fileDownloader
                        .DownloadAsync(file.Url, target, installProgress, token)
                        .ConfigureAwait(true);

                    path = target;
                }
                else
                {
                    path = await _installer
                        .InstallAsync(file, item.Kind, MinecraftDirectory, VersionFolder, token)
                        .ConfigureAwait(true);
                }

                // 直连下载器不校验哈希；Modrinth 给了 SHA1 就必须验，
                // 否则 CDN 返回半截/错误内容也会被当成安装成功。
                if (!string.IsNullOrWhiteSpace(file.Sha1) &&
                    !ResourceInstaller.IsChecksumValid(path, file.Sha1, out string actualSha1, out _))
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    throw new InvalidDataException(
                        "文件校验失败：" + file.Name + "（期望 SHA1 " + file.Sha1 +
                        "，实际 " + actualSha1 + "）");
                }
            }, token).ConfigureAwait(true);

            Trace("安装成功：" + path);
                StatusText = "已安装到 " + path;
                HasProgress = false;
                Progress = 0;
                ProgressDetails = string.Empty;
            }
            catch (OperationCanceledException)
            {
                ProgressDetails = string.Empty;
                StatusText = "已取消下载（已下载的部分会保留）";
            }
            catch (Exception ex)
            {
                ProgressDetails = string.Empty;
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

        /// <summary>
        /// 安装 Modrinth 版本声明里的 required 前置；递归但不重复安装同一项目。
        /// 先装前置再装主体，避免玩家启动时才发现缺依赖。
        /// </summary>
        private async Task InstallRequiredDependenciesAsync(
            ResourceFile file,
            HashSet<string> visited,
            CancellationToken cancellationToken)
        {
            foreach (ResourceDependency dependency in file.Dependencies)
            {
                if (!dependency.IsRequired || string.IsNullOrWhiteSpace(dependency.ProjectId))
                {
                    continue;
                }

                if (!visited.Add(dependency.ProjectId))
                {
                    continue;
                }

                IReadOnlyList<ResourceFile> candidates = await _catalog
                    .GetFilesAsync(
                        dependency.ProjectId,
                        ResourceKind.Mod,
                        string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim(),
                        LoaderOf(SelectedLoader),
                        cancellationToken)
                    .ConfigureAwait(true);

                ResourceFile? dependencyFile = SelectDependencyFile(
                    candidates,
                    dependency,
                    string.IsNullOrWhiteSpace(GameVersion) ? null : GameVersion.Trim(),
                    LoaderOf(SelectedLoader));

                if (dependencyFile == null)
                {
                    throw new InvalidOperationException(
                        "缺少必需前置（Modrinth 项目 " + dependency.ProjectId + "），请先手动安装后再试。");
                }

                StatusText = "正在补装前置 " + dependencyFile.Name + "…";
                string dependencyPath = string.Empty;
                await _queue.RunAsync("补装前置 " + dependencyFile.Name, async () =>
                {
                    dependencyPath = await _installer
                        .InstallAsync(
                            dependencyFile,
                            ResourceKind.Mod,
                            MinecraftDirectory,
                            VersionFolder,
                            cancellationToken)
                        .ConfigureAwait(true);
                }, cancellationToken).ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(dependencyFile.Sha1) &&
                    !ResourceInstaller.IsChecksumValid(
                        dependencyPath,
                        dependencyFile.Sha1,
                        out string actualSha1,
                        out _))
                {
                    try
                    {
                        File.Delete(dependencyPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }

                    throw new InvalidDataException(
                        "前置校验失败：" + dependencyFile.Name + "（期望 SHA1 " +
                        dependencyFile.Sha1 + "，实际 " + actualSha1 + "）");
                }

                await InstallRequiredDependenciesAsync(dependencyFile, visited, cancellationToken)
                    .ConfigureAwait(true);
            }
        }

        /// <summary>
        /// 从实例目录名里提取游戏版本：优先取最后一个 1.x 形式的，
        /// 因为 "fabric-loader-0.16.9-1.20.1" 的第一个匹配是加载器版本。
        /// </summary>
        /// <summary>资源种类对应的子目录名。</summary>
    internal static string SubdirectoryFor(ResourceKind kind)
    {
        switch (kind)
        {
            case ResourceKind.ResourcePack: return "resourcepacks";
            case ResourceKind.ShaderPack: return "shaderpacks";
            case ResourceKind.DataPack: return "datapacks";
            default: return "mods";
        }
    }

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
                StatusText = "请先在「世界库」选一个实例，再导入整合包（决定装到哪个 .minecraft）。";
                return;
            }

            string? path = _filePicker.PickFile(
                "选择整合包", "整合包 (*.mrpack;*.zip)|*.mrpack;*.zip|所有文件 (*.*)|*.*");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            IsBusy = true;
            HasProgress = true;
            Progress = 0;
            ProgressDetails = string.Empty;

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
                StatusText = "已创建实例「" + versionId + "」，去「世界库」里启动它。";
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

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            if (bytes < 1024 * 1024)
            {
                return (bytes / 1024.0).ToString("0.0") + " KB";
            }

            if (bytes < 1024L * 1024 * 1024)
            {
                return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            }

            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0") + " GB";
        }

        private void ApplyProgress(InstallProgress value)
        {
            Progress = value.Percent;
            StatusText = value.Stage;

            if (value.BytesTotal > 0)
            {
                ProgressDetails = FormatSize(value.BytesReceived) + " / " + FormatSize(value.BytesTotal);
            }
            else if (value.BytesReceived > 0)
            {
                ProgressDetails = "已下载 " + FormatSize(value.BytesReceived);
            }
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


        /// <summary>
        /// 依赖声明指定了版本 id 时严格按 id；否则优先当前游戏版本与加载器，再取最新发布版本。
        /// </summary>
        internal static ResourceFile? SelectDependencyFile(
            IReadOnlyList<ResourceFile> candidates,
            ResourceDependency dependency,
            string? gameVersion,
            string? loader)
        {
            List<ResourceFile> usable = candidates
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Url))
                .ToList();

            if (usable.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(dependency.VersionId))
            {
                return usable.FirstOrDefault(candidate => string.Equals(
                    candidate.VersionId,
                    dependency.VersionId,
                    StringComparison.OrdinalIgnoreCase));
            }

            List<ResourceFile> gameMatches = string.IsNullOrWhiteSpace(gameVersion)
                ? usable
                : usable.Where(candidate => candidate.GameVersions.Any(version =>
                    string.Equals(version, gameVersion, StringComparison.OrdinalIgnoreCase))).ToList();

            if (gameMatches.Count == 0)
            {
                gameMatches = usable;
            }

            List<ResourceFile> loaderMatches = string.IsNullOrWhiteSpace(loader)
                ? gameMatches
                : gameMatches.Where(candidate => string.Equals(
                    candidate.Loader, loader, StringComparison.OrdinalIgnoreCase)).ToList();

            if (loaderMatches.Count > 0)
            {
                gameMatches = loaderMatches;
            }

            return gameMatches
                .OrderByDescending(candidate => candidate.PublishedAt ?? DateTimeOffset.MinValue)
                .First();
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
