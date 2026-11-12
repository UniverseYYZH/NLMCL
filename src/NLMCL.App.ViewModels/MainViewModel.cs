using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;
using NLMCL.Core.Accounts;
using NLMCL.Core.Installing;
using NLMCL.Core.Networking;
using NLMCL.Core.Servers;
using NLMCL.Core.Settings;
using NLMCL.Core.Maintenance;
using System.Diagnostics;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 主窗口的展示逻辑。刻意不引用任何 WPF 类型，因此可以在没有 UI 线程的情况下单测。
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject, ISettingsCommandHost
    {
        private readonly IVersionCatalog _catalog;
        private readonly IGameLauncher _launcher;
        private readonly IJavaLocator _javaLocator;

        private readonly IAccountStore _accounts;
        private readonly IMicrosoftAuthenticator _microsoft;
        private readonly IInstanceSettingsStore _instanceSettings;
        private readonly ILocalNetworkInfo _network;
        private readonly IClipboard _clipboard;
        private readonly IFolderPicker _folderPicker;
        private readonly IAppSettingsService _appSettings;
    private readonly IMirrorSwitch? _mirror;
    private readonly IAppPaths _paths;

        /// <summary>没有合适的 Java 时按版本要求自动下载（组合根注入；单测里可为 null）。</summary>
        private readonly IJavaRuntimeInstaller? _javaInstaller;

        /// <summary>会话式启动器（捕获 stdout/stderr）。为 null 时退回 IGameLauncher。</summary>
        private readonly IGameSessionLauncher? _sessionLauncher;

        /// <summary>外置登录（Yggdrasil）。null 时按钮会提示不可用。</summary>
        private readonly IExternalAuthenticator? _externalAuthenticator;

        /// <summary>authlib-injector（外置登录启动所需）。null 时外置账号无法启动。</summary>
        private readonly IAuthlibInjector? _authlibInjector;

        /// <summary>实例快照（saves / config / options.txt）。null 时按钮提示不可用。</summary>
        private readonly ISnapshotService? _snapshotService;

        /// <summary>确认框（删除快照这类不可撤销的操作）。null 时视为直接确认。</summary>
        private readonly IDialogService? _dialogs;

        /// <summary>主题与缩放（App 层实现）。null 时设置页只保存、不生效。</summary>
        private readonly IAppearance? _appearance;

        /// <summary>下载线程数接线到 PCL 设置。null 时只保存不生效。</summary>
        private readonly IDownloadTuning? _downloadTuning;

        /// <summary>在线资源搜索与安装（Modrinth）。</summary>
        private readonly ResourceViewModel? _resource;

        /// <summary>皮肤预览。</summary>
        private readonly SkinViewModel? _skin;

        /// <summary>局域网扫描。</summary>
        private readonly LanScanViewModel? _lanScan;

        /// <summary>皮肤服务（取账号头像用；可能为 null）。</summary>
        private readonly ISkinService? _skins;

        /// <summary>头像请求代次：快速切换账号时，旧请求不能覆盖新账号的头像。</summary>
        private int _avatarGeneration;

        private readonly IGameDirectoryScanner? _directoryScanner;
        private readonly IVersionDetailsService? _versionDetails;
        private readonly IFilePicker? _filePicker;
        private readonly IFileSavePicker? _saveFilePicker;
        private readonly IInstancePackageService? _instancePackages;
        private readonly IInstanceContentService? _instanceContent;

        /// <summary>常用服务器（快捷入口）。</summary>
        private readonly IServerBookmarkStore? _bookmarks;

        /// <summary>实例游戏目录解析（版本隔离由 PCL 决定，必须跟启动共用同一答案）。</summary>
        private readonly IInstancePathResolver? _instancePaths;

        /// <summary>SLP 探测（诊断用）。</summary>
        private readonly IServerPinger? _pinger;
        private readonly IServerAddressResolver? _addressResolver;
        private readonly IServerProfileStore? _profileStore;
        private readonly ServerProfilePicker? _profilePicker;
        /// <summary>上一次启动用的服务器地址（"重启游戏"要用）。</summary>
        private string? _lastServerAddress;

        /// <summary>实例绝对路径 → 最后一次成功启动时间。</summary>
        private Dictionary<string, DateTimeOffset> _lastLaunched =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);

        private readonly IVersionInstaller? _versionInstaller;
        private readonly IFileDownloader? _fileDownloader;
        private readonly IResourceCatalog? _resourceCatalog;
        private readonly IFabricLoaderCatalog? _fabricCatalog;

        /// <summary>Mod 管理页。</summary>
        private readonly ModsViewModel? _mods;

        /// <summary>版本修复。</summary>
        private readonly IVersionRepairer? _repairer;

        /// <summary>实例重命名/复制/删除。</summary>
        private readonly IVersionManager? _versionManager;

        /// <summary>设置加载完成前不要回写，否则初始化时会把默认值覆盖用户配置。</summary>
        private bool _settingsLoaded;

        /// <summary>缩放滑块的防抖令牌（拖动中不断取消上一次）。</summary>
        private CancellationTokenSource? _scaleDebounce;

        /// <summary>最大化缩放滑块的防抖令牌。</summary>
        private CancellationTokenSource? _scaleMaximizedDebounce;

        /// <summary>窗口状态变化只订阅一次。</summary>
        private bool _windowStateHooked;


        private bool _accountLoaded;
        private CancellationTokenSource? _scanCts;

        public MainViewModel(
            IVersionCatalog catalog,
            IAppPaths paths,
            DownloadViewModel download,
            LaunchConsoleViewModel console,
            IGameLauncher launcher,
            IJavaLocator javaLocator,
            IAccountStore accounts,
            IMicrosoftAuthenticator microsoft,
            IInstanceSettingsStore instanceSettings,
            ILocalNetworkInfo network,
            IClipboard clipboard,
            IFolderPicker folderPicker,
            IAppSettingsStore appSettings,
            IJavaRuntimeInstaller? javaInstaller = null,
            IGameSessionLauncher? sessionLauncher = null,
            IExternalAuthenticator? externalAuthenticator = null,
            IAuthlibInjector? authlibInjector = null,
            ISnapshotService? snapshotService = null,
            IDialogService? dialogs = null,
            IAppearance? appearance = null,
            IDownloadTuning? downloadTuning = null,
            ResourceViewModel? resource = null,
            SkinViewModel? skin = null,
            LanScanViewModel? lanScan = null,
            ISkinService? skins = null,
            IServerBookmarkStore? bookmarks = null,
        IInstancePathResolver? instancePaths = null,
            IServerPinger? pinger = null,
            ModsViewModel? mods = null,
            IVersionRepairer? repairer = null,
            ICrashAnalyzer? crashAnalyzer = null,
            IVersionManager? versionManager = null,
            IServerAddressResolver? addressResolver = null,
            IServerProfileStore? profileStore = null,
            ServerProfilePicker? profilePicker = null,
            IVersionInstaller? versionInstaller = null,
            IFileDownloader? fileDownloader = null,
            IResourceCatalog? resourceCatalog = null,
            IFabricLoaderCatalog? fabricCatalog = null,
            IMirrorSwitch? mirror = null,
            ICacheCleaner? cacheCleaner = null,
            IAppSettingsService? appSettingsService = null,
            ISettingsTransferService? settingsTransfer = null,
            IFilePicker? filePicker = null,
            IFileSavePicker? saveFilePicker = null,
            IGameDirectoryScanner? directoryScanner = null,
            IVersionDetailsService? versionDetails = null,
            IInstancePackageService? instancePackages = null,
            IInstanceContentService? instanceContent = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
            _javaLocator = javaLocator ?? throw new ArgumentNullException(nameof(javaLocator));

            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _microsoft = microsoft ?? throw new ArgumentNullException(nameof(microsoft));
            _instanceSettings = instanceSettings ?? throw new ArgumentNullException(nameof(instanceSettings));
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
            _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
            _appSettings = appSettingsService
                ?? new AppSettingsService(appSettings ?? throw new ArgumentNullException(nameof(appSettings)));
            _paths = paths;
            Cache = new CacheCleanupViewModel(
                cacheCleaner ?? new CacheCleaner(paths),
                dialogs);
            Transfer = new SettingsTransferViewModel(
                settingsTransfer ?? new SettingsTransferService(_appSettings),
                filePicker,
                saveFilePicker,
                dialogs);
            Transfer.Imported += () => _ = ApplyImportedSettingsAsync();
            _mirror = mirror;
            _sessionLauncher = sessionLauncher;
            _externalAuthenticator = externalAuthenticator;
            _authlibInjector = authlibInjector;
            _snapshotService = snapshotService;
            _dialogs = dialogs;
            _appearance = appearance;
            _downloadTuning = downloadTuning;
            _directoryScanner = directoryScanner;
            _versionDetails = versionDetails;
            _filePicker = filePicker;
            _saveFilePicker = saveFilePicker;
            _instancePackages = instancePackages;
            _instanceContent = instanceContent;
            _resource = resource;
            _skin = skin;
            _lanScan = lanScan;
            _skins = skins;
            _bookmarks = bookmarks;
            _instancePaths = instancePaths;
            _pinger = pinger;
            _addressResolver = addressResolver;
            _profileStore = profileStore;
            _profilePicker = profilePicker;
            _versionInstaller = versionInstaller;
            _fileDownloader = fileDownloader;
            _resourceCatalog = resourceCatalog;
            _fabricCatalog = fabricCatalog;
            _mods = mods;
            _repairer = repairer;
            _ = LoadServersAsync();
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            Download = download ?? throw new ArgumentNullException(nameof(download));
            Download.TargetDirectoryPicked += (_, path) => _ = ApplyDownloadDirectoryAsync(path);


            Console = console ?? throw new ArgumentNullException(nameof(console));
            Console.CrashAnalyzer = crashAnalyzer;
            _versionManager = versionManager;

            CurrentGameDirectory = paths.DefaultMinecraftDirectory;

            NavigationItem versions = new NavigationItem("世界库", "扫描本地 .minecraft", ShellSection.Versions);
            NavigationItem world = new NavigationItem("世界", "当前实例详情", ShellSection.World);
            NavigationItem consoleNav = new NavigationItem("日志", "多实例控制台", ShellSection.Launch);
            NavigationItem multiplayer = new NavigationItem("联机", "局域网与直连", ShellSection.Multiplayer);
            NavigationItem ningli = new NavigationItem("方块宁理", "宝可梦与还原服", ShellSection.Ningli);
            NavigationItem snapshot = new NavigationItem("快照", "备份存档与配置", ShellSection.Snapshot);
            NavigationItem lanScanNav = new NavigationItem("局域网", "发现附近的房间", ShellSection.LanScan);
            NavigationItem settings = new NavigationItem("设置", "外观 / Java / 下载", ShellSection.Settings);
            NavigationItem downloadNav = new NavigationItem("下载中心", "原版与加载器", ShellSection.Download);
            NavigationItem import = new NavigationItem("导入资源", "mod 与整合包", ShellSection.Import, topLevel: false);
            NavigationItem modsNav = new NavigationItem("Mod 管理", "启用 / 删除 / 更新", ShellSection.Mods, topLevel: false);
            NavigationItem skinNav = new NavigationItem("皮肤", "预览与保存", ShellSection.Skin);

            NavigationGroups = new ReadOnlyCollection<NavigationGroup>(new List<NavigationGroup>
            {
                new NavigationGroup("工作区", new[] { versions, world, consoleNav, multiplayer, ningli, snapshot, lanScanNav, settings }),
                new NavigationGroup("工具", new[] { downloadNav, import, modsNav, skinNav })
            });

            // F 顶部导航顺序与逻辑分组解耦：分组保留给内部逻辑/测试，顶部按 F 排列。
            TopNavigation = new ReadOnlyCollection<NavigationItem>(new[]
            {
                versions, world, downloadNav, multiplayer, ningli, snapshot, skinNav, consoleNav, settings
            });
            SelectSection(versions);

            // 装完一个版本后自动重扫本地列表，省掉"回版本页再点扫描"这一步。
            Download.InstallCompleted += OnDownloadInstallCompleted;

            Settings = new SettingsViewModel(
                this, _javaLocator, _filePicker, _paths, Cache, Transfer, this);
            Settings.SaveRequested += (_, __) => _ = SaveAppearanceAsync();
            Settings.SpeedLimitChanged += (_, __) => OnSettingsSpeedLimitChanged();
            Settings.ThreadsChanged += (_, __) => OnSettingsThreadsChanged();
            Settings.MirrorChanged += (_, __) => OnSettingsMirrorChanged();
            Settings.ThemeChanged += (_, __) => OnSettingsThemeChanged();
            Settings.ScaleChanged += (_, __) => OnSettingsScaleChanged();
            Settings.MemoryChanged += (_, __) => OnSettingsMemoryChanged();
            Settings.LaunchArgsChanged += (_, __) => OnSettingsLaunchArgsChanged();
            Settings.GeneralChanged += (_, __) => OnSettingsGeneralChanged();
            Settings.DefaultJavaPathChanged += (_, __) => OnSettingsJavaPathChanged();
        }

        /// <summary>下载页的展示逻辑（由组合根注入）。</summary>
        public DownloadViewModel Download { get; }

        /// <summary>设置页的 ViewModel 边界；当前状态仍由 MainViewModel 持有。</summary>
        public SettingsViewModel Settings { get; }



        /// <summary>启动控制台的展示逻辑。</summary>
        public LaunchConsoleViewModel Console { get; }

        /// <summary>设置页的缓存清理（独立 ViewModel，避免 MainViewModel 继续膨胀）。</summary>
        public CacheCleanupViewModel Cache { get; }

        /// <summary>设置导入导出。</summary>
        public SettingsTransferViewModel Transfer { get; }

        /// <summary>
        /// 游戏目录是"一个"状态：世界库、启动控制台、下载页必须看到同一个。
        ///
        /// 之前三个页面各自记一份：世界库扫的是 D 盘，控制台却去 %APPDATA%\.minecraft 找版本、
        /// 下载页也往默认目录装——表现就是"启动失败：未找到版本目录"和"装完列表里没有"。
        /// </summary>
        partial void OnCurrentGameDirectoryChanged(string value)
        {
            Console.GameDirectory = value;
            Download.TargetDirectory = value;
            OnPropertyChanged(nameof(GameDirectoryStatusText));
            OnPropertyChanged(nameof(GameDirectoryDiskText));

            if (!string.Equals(SelectedGameDirectory, value, StringComparison.OrdinalIgnoreCase))
            {
                SelectedGameDirectory = value;
            }

            RefreshInstanceContent();
        }

        /// <summary>选中版本时把控制台的"版本目录名"一起带过去，省得用户再抄一遍。</summary>
        partial void OnSelectedVersionChanged(VersionItem? value)
        {
            if (value != null)
            {
                Console.VersionId = value.FolderName;
            }
        }

        private void OnDownloadInstallCompleted(object? sender, EventArgs e)
        {
            // 事件来自下载页，不 await：扫描失败会自己写进 StatusText。
            _ = RefreshAsync();
        }

        public string ProductName => "NLMCL";

        public string ProductTagline => "Minecraft Launcher";

        /// <summary>设置页“当前生效”摘要用。</summary>
        public string GameDirectoryStatusText => Directory.Exists(CurrentGameDirectory)
            ? "目录存在"
            : "目录不存在";

        [ObservableProperty]
        private int _saveSlotCount;

        [ObservableProperty]
        private int _resourcePackCount;

        [ObservableProperty]
        private int _shaderPackCount;

        /// <summary>当前游戏目录所在磁盘的可用空间。</summary>
        public string GameDirectoryDiskText
        {
            get
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(CurrentGameDirectory))
                    {
                        return "未选择目录";
                    }

                    string root = Path.GetPathRoot(Path.GetFullPath(CurrentGameDirectory))
                        ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(root))
                    {
                        return "无法读取磁盘空间";
                    }

                    DriveInfo drive = new DriveInfo(root);

                    if (!drive.IsReady)
                    {
                        return "磁盘未就绪";
                    }

                    return drive.Name.TrimEnd('\\') +
                        " · 可用 " + FormatDiskSize(drive.AvailableFreeSpace) +
                        " / " + FormatDiskSize(drive.TotalSize);
                }
                catch (Exception)
                {
                    return "无法读取磁盘空间";
                }
            }
        }

        public string EffectiveJavaText => Settings?.EffectiveJavaText ?? "自动（按版本要求）";

        public string EffectiveJavaDetailText => Settings?.EffectiveJavaDetailText
            ?? "自动：由启动器按每个实例需要的 Java 版本挑选";

        public string DownloadSourceText => UseMirror ? "镜像（元数据）+ 官方 CDN" : "官方源";

        public string DownloadRetryText => DownloadRetryPolicy.Describe();

        public string LogDirectory => Path.Combine(_paths.DataRoot, "logs");

        public string StageLabel => "v1.0 · 全功能";

        public string StageHint => "版本 · 下载 · 启动 · 联机 · 快照 · 导入";

        public IReadOnlyList<NavigationGroup> NavigationGroups { get; }

        /// <summary>F 顶部导航的扁平顺序。</summary>
        public IReadOnlyList<NavigationItem> TopNavigation { get; }

        public ObservableCollection<VersionItem> Versions { get; } = new ObservableCollection<VersionItem>();

        public ObservableCollection<VersionDetailsLine> VersionDetailsLines { get; }
            = new ObservableCollection<VersionDetailsLine>();

        [ObservableProperty]
        private bool _isVersionDetailsOpen;

        [ObservableProperty]
        private string _versionDetailsTitle = string.Empty;

        [ObservableProperty]
        private string _versionDetailsSubtitle = string.Empty;

        /// <summary>用户添加过的 .minecraft 目录；当前目录是 <see cref="CurrentGameDirectory"/>。</summary>
        public ObservableCollection<string> GameDirectories { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private bool _isScanningDirectories;

        [ObservableProperty]
        private string _scanProgressText = string.Empty;

        private CancellationTokenSource? _scanDirectoriesCts;
        private readonly List<string> _scanFoundDuringRun = new List<string>();

        [ObservableProperty]
        private NavigationItem? _selectedNavigation;

        /// <summary>我要告诉别人的端口（游戏里"对局域网开放"后聊天栏显示的那个）。</summary>
        [ObservableProperty]
        private string _myPort = string.Empty;

        /// <summary>我的联机地址，例如 192.168.1.7:54321。</summary>
        [ObservableProperty]
        private string _myAddress = string.Empty;

        /// <summary>对方的联机地址，例如 10.21.3.9:54321。</summary>
        [ObservableProperty]
        private string _serverAddress = string.Empty;

        /// <summary>是否选中了版本（决定设置条是否显示）。</summary>
        [ObservableProperty]
        private bool _hasSelection;

        /// <summary>该实例分配的内存（MB）。</summary>
        [ObservableProperty]
        private int _memoryMb = InstanceSettings.DefaultMemoryMb;

        /// <summary>指定的 Java 路径；为空表示自动挑。</summary>
        [ObservableProperty]
        private string _javaPath = string.Empty;

        /// <summary>额外的游戏参数（按空格分词）。</summary>
        [ObservableProperty]
        private string _extraArguments = string.Empty;

        [ObservableProperty]
        private int _windowWidth;

        [ObservableProperty]
        private int _windowHeight;

        [ObservableProperty]
        private bool _fullscreen;

        /// <summary>该实例是否走 HTTP/HTTPS 代理。</summary>
        [ObservableProperty]
        private bool _useProxy;

        [ObservableProperty]
        private string _proxyHost = string.Empty;

        [ObservableProperty]
        private int _proxyPort;

        private CancellationTokenSource? _proxyPreflightDebounce;
        private bool _suppressProxyPreflightRefresh;

        /// <summary>允许 LWJGL2 回退到软件 OpenGL（旧显卡/驱动兼容）。</summary>
        [ObservableProperty]
        private bool _softwareOpenGl;

        /// <summary>列表里被点中的版本。启动按钮用它决定启动哪个。</summary>
        [ObservableProperty]
        private VersionItem? _selectedVersion;


        /// <summary>当前账号。null 表示未登录（启动时用占位名）。</summary>

        [ObservableProperty]
        private Account? _account;


        /// <summary>输入框里的玩家名（离线登录）。</summary>

        [ObservableProperty]
        private string _playerNameInput = "Player";

        /// <summary>
        /// Azure 应用的 client id（登录弹层里填）。
        /// 不内置：与 PCL 一样要求使用方自行注册公共客户端应用。
        /// </summary>
        [ObservableProperty]
        private string _microsoftClientId = string.Empty;

        /// <summary>
        /// 外置登录的认证服务器（Yggdrasil API 根）。默认 MUA 用户中心，
        /// 支持其它 Blessing Skin 站点（填站点自己的 /api/yggdrasil）。
        /// </summary>
        [ObservableProperty]
        private string _externalServer = "https://skin.mualliance.ltd/api/yggdrasil";

        [ObservableProperty]
        private string _externalUserName = string.Empty;

        /// <summary>密码只在内存里过一遍，登录成功后立即清空，不落盘。</summary>
        [ObservableProperty]
        private string _externalPassword = string.Empty;

        [ObservableProperty]
        private bool _isExternalSigningIn;

        /// <summary>快照面板是否打开。</summary>
        [ObservableProperty]
        private bool _isSnapshotPanelOpen;

        /// <summary>新建快照的备注。</summary>
        [ObservableProperty]
        private string _snapshotNote = string.Empty;

        [ObservableProperty]
        private bool _isSnapshotBusy;

        /// <summary>当前版本的快照列表（按时间倒序）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<SnapshotInfo> Snapshots { get; }
            = new System.Collections.ObjectModel.ObservableCollection<SnapshotInfo>();


        /// <summary>登录状态文案。</summary>

        public string AccountLabel => Account == null ? "点击登录账号" : Account.Name + " · " + Account.TypeLabel;


        partial void OnAccountChanged(Account? value)
        {
            OnPropertyChanged(nameof(AccountLabel));

            // 侧栏卡片直接显示名字与类型：在这里同步，避免出现两份状态。
            AccountName = value == null ? "未登录" : value.Name;
            AccountType = value == null ? "点击登录账号" : value.TypeLabel;

            RefreshSavedAccountSelection();

            try
            {
                _skin?.SetAccount(value);
            }
            catch (Exception)
            {
                // 皮肤页初始化失败不能阻断账号切换与头像加载。
            }

            if (IsSkinSection)
            {
                _skin?.ActivateLibrary();
            }

            _ = LoadAvatarAsync(value);
        }
        /// <summary>拉取账号头像（头部正面）。失败时回退默认 Steve，不阻断界面。</summary>
        private async Task LoadAvatarAsync(Account? account)
        {
            int generation = ++_avatarGeneration;

            if (_skins == null || account == null)
            {
                if (generation == _avatarGeneration)
                {
                    AvatarPath = null;
                    AvatarPng = null;
                }

                return;
            }

            // 切账号先清掉上一个账号的头像，避免短暂显示别人的头像。
            if (generation == _avatarGeneration)
            {
                AvatarPath = null;
                AvatarPng = null;
            }

            try
            {
                byte[]? png = await _skins
                    .GetAvatarPngAsync(account, CancellationToken.None)
                    .ConfigureAwait(true);

                // 切换账号后旧请求可能晚回来：只接受当前代次的结果。
                if (generation == _avatarGeneration)
                {
                    AvatarPng = png;
                }
            }
            catch (Exception)
            {
                if (generation == _avatarGeneration)
                {
                    AvatarPng = null;
                }
            }
        }


        /// <summary>账号弹层是否展开。</summary>
        [ObservableProperty]
        private bool _isAccountPopupOpen;

        /// <summary>正在等待微软登录完成。</summary>
        [ObservableProperty]
        private bool _isMicrosoftSigningIn;

        /// <summary>要用户输入到浏览器的设备码。</summary>
        [ObservableProperty]
        private string _deviceCodeText = string.Empty;

        /// <summary>设备码下方的说明（有效期 / 等待状态）。</summary>
        [ObservableProperty]
        private string _deviceCodeHint = string.Empty;

        /// <summary>复制设备码到剪贴板。</summary>
        [RelayCommand]
        private void CopyDeviceCode()
        {
            if (string.IsNullOrWhiteSpace(DeviceCodeText))
            {
                return;
            }

            _clipboard.SetText(DeviceCodeText);
            StatusText = "已复制设备码：" + DeviceCodeText;
        }


        /// <summary>登录过程中的验证页地址（用于「重新打开网页」）。</summary>
        private string? _deviceVerificationUri;

        /// <summary>重新打开微软验证页：浏览器被关掉或没自动弹出时用。</summary>
        [RelayCommand]
        private void ReopenDeviceCodePage()
        {
            string uri = string.IsNullOrWhiteSpace(_deviceVerificationUri)
                ? "https://www.microsoft.com/link"
                : _deviceVerificationUri!;

            try
            {
                Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                StatusText = "已重新打开验证页：" + uri;
            }
            catch (Exception ex)
            {
                StatusText = "打不开浏览器，请手动访问 " + uri + "（" + ex.Message + "）";
            }
        }
        /// <summary>展开/收起账号弹层。</summary>
        [RelayCommand]
        public void ToggleAccountPopup()
        {
            IsAccountPopupOpen = !IsAccountPopupOpen;
        }

        /// <summary>是否正在启动（防止重复点击）。</summary>
        [ObservableProperty]
        private bool _isLaunching;

        /// <summary>是否正在执行“进模组服”的准备流程（挑入口/安装/AutoModpack）。</summary>
        [ObservableProperty]
        private bool _isJoiningProfile;

        /// <summary>启动入口的原子闸门：预检是异步的，只靠 IsLaunching 会放过双击。</summary>
        private int _launchGate;

        /// <summary>大厅入口的原子闸门：连续点击不能重复挑入口。</summary>
        private int _lobbyGate;

        [ObservableProperty]
        private string _currentGameDirectory = string.Empty;

        /// <summary>游戏目录下拉框的当前项。</summary>
        [ObservableProperty]
        private string? _selectedGameDirectory;

        partial void OnSelectedGameDirectoryChanged(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, CurrentGameDirectory, StringComparison.OrdinalIgnoreCase))
            {
                CurrentGameDirectory = value!;
            }
        }

        [ObservableProperty]
        private string _settingsReadOnlyWarning = string.Empty;

        private bool _settingsFileReadOnly;

        public bool HasSettingsReadOnlyWarning => !string.IsNullOrEmpty(SettingsReadOnlyWarning);

        partial void OnSettingsReadOnlyWarningChanged(string value)
        {
            OnPropertyChanged(nameof(HasSettingsReadOnlyWarning));
        }

        [ObservableProperty]
        private string _statusText = "就绪";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isEmpty = true;

        /// <summary>账号卡（M5 接入 AccountStore 后由真实数据驱动）。</summary>
        [ObservableProperty]
        private string _accountName = "未登录";

        [ObservableProperty]
        private string _accountType = "点击登录账号";

        [ObservableProperty]
        private string _versionCountLabel = "0 个版本";

        [ObservableProperty]
        private string _releaseCountLabel = string.Empty;

        [ObservableProperty]
        private string _snapshotCountLabel = string.Empty;

        [ObservableProperty]
        private string _otherCountLabel = string.Empty;

        [ObservableProperty]
        private bool _hasRelease;

        [ObservableProperty]
        private bool _hasSnapshot;

        [ObservableProperty]
        private bool _hasOther;

        /// <summary>当前显示「世界库」页。</summary>
        [ObservableProperty]
        private bool _isVersionsSection = true;

        /// <summary>当前显示「世界」实例详情页。</summary>
        [ObservableProperty]
        private bool _isWorldSection;

        /// <summary>是否停在「实例快照」页。</summary>
        [ObservableProperty]
        private bool _isSnapshotSection;

        /// <summary>快照总占用（「N 个 · X MB」）。</summary>
        [ObservableProperty]
        private string _snapshotTotalText = "0 个";
        /// <summary>当前显示「下载中心」页。</summary>
        [ObservableProperty]
        private bool _isDownloadSection;


        /// <summary>当前显示「联机房间」页。</summary>
        [ObservableProperty]
        private bool _isMultiplayerSection;

        /// <summary>当前显示方块宁理服务器页。</summary>
        [ObservableProperty]
        private bool _isNingliSection;

        /// <summary>在线资源页的视图模型（可能为 null，例如单测未注入）。</summary>
        public ResourceViewModel? Resource => _resource;

        /// <summary>当前显示「导入资源」页。</summary>
        [ObservableProperty]
        private bool _isImportSection;

        /// <summary>皮肤页的视图模型（可能为 null）。</summary>
        public SkinViewModel? Skin => _skin;

        /// <summary>当前显示「皮肤」页。</summary>
        [ObservableProperty]
        private bool _isSkinSection;

        /// <summary>Mod 管理页的视图模型（可能为 null）。</summary>
        public ModsViewModel? Mods => _mods;

        /// <summary>当前显示「Mod 管理」页。</summary>
        [ObservableProperty]
        private bool _isModsSection;

        /// <summary>局域网扫描页的视图模型（可能为 null）。</summary>
        public LanScanViewModel? LanScan => _lanScan;

        /// <summary>当前显示「局域网扫描」页。</summary>
        [ObservableProperty]
        private bool _isLanScanSection;

        /// <summary>方块宁理页的展示状态。</summary>
        public NingliViewModel Ningli { get; } = new NingliViewModel();

        /// <summary>服务器档案：进入一台服需要什么（版本/加载器/模组）。</summary>
        public ObservableCollection<ServerProfile> ServerProfiles => Ningli.ServerProfiles;

        /// <summary>进服前检查（账号 / 客户端 / 模组 / 内存 / 整合包）。</summary>
        public ObservableCollection<ServerJoinCheck> JoinChecks => Ningli.JoinChecks;

        /// <summary>主按钮对应的服务器（第一个需要模组的档案）。</summary>
        public ServerProfile? PrimaryProfile => Ningli.PrimaryProfile;

        /// <summary>纯净子服档案（还原服等）：不装任何加载器。</summary>
        public ServerProfile? VanillaProfile => Ningli.VanillaProfile;

        /// <summary>是否有可显示的服务器档案。</summary>
        public bool HasPrimaryProfile => Ningli.HasPrimaryProfile;

        /// <summary>是否有纯净档案。</summary>
        public bool HasVanillaProfile => Ningli.HasVanillaProfile;

        /// <summary>联机页当前会显示几个服档案；用于 1 栏 / 2 栏自适应。</summary>
        public int VisibleServerProfileCount => Ningli.VisibleServerProfileCount;


        /// <summary>已保存的账号（当前账号在第一位）。</summary>
        public ObservableCollection<AccountItemViewModel> SavedAccounts { get; }
            = new ObservableCollection<AccountItemViewModel>();

        /// <summary>常用服务器列表（方块宁理三条线路 + 用户自定义）。</summary>
        public ObservableCollection<ServerBookmark> Servers { get; }
            = new ObservableCollection<ServerBookmark>();

        /// <summary>「添加服务器」输入：名称（可空，默认用地址）。</summary>
        [ObservableProperty]
        private string _newServerName = string.Empty;

        /// <summary>「添加服务器」输入：host[:port]。</summary>
        [ObservableProperty]
        private string _newServerAddress = string.Empty;

        /// <summary>联机诊断输出（多行文本）。</summary>
        [ObservableProperty]
        private string _diagnosticsText = "点上方「诊断」按钮，结果会显示在这里。";

        /// <summary>账号头像（头部正面 PNG 路径）；没有皮肤时为 null。</summary>
        [ObservableProperty]
        private string? _avatarPath;

        /// <summary>账号头像（内存 PNG 字节），优先级高于 AvatarPath。</summary>
        [ObservableProperty]
        private byte[]? _avatarPng;

        public bool HasAvatar => (AvatarPng != null && AvatarPng.Length > 0) || !string.IsNullOrEmpty(AvatarPath);

        partial void OnAvatarPathChanged(string? value)
        {
            OnPropertyChanged(nameof(HasAvatar));
        }

        partial void OnAvatarPngChanged(byte[]? value)
        {
            OnPropertyChanged(nameof(HasAvatar));
        }

        /// <summary>当前显示「设置」页。</summary>
        [ObservableProperty]
        private bool _isSettingsSection;

        // ── 设置页的选项（界面用字符串，存盘时换算）─────────────────────────
        public IReadOnlyList<string> ThemeOptions { get; } = new[] { "浅色", "深色", "高对比度", "跟随系统" };

        public string SelectedTheme
        {
            get => Settings?.SelectedTheme ?? "浅色";
            set
            {
                if (Settings != null && !string.Equals(Settings.SelectedTheme, value, StringComparison.Ordinal))
                {
                    Settings.SelectedTheme = value;
                }
            }
        }

        public double UiScalePercent
        {
            get => Settings?.UiScalePercent ?? 100;
            set
            {
                if (Settings != null && Math.Abs(Settings.UiScalePercent - value) > double.Epsilon)
                {
                    Settings.UiScalePercent = value;
                }
            }
        }

        public double UiScaleMaximizedPercent
        {
            get => Settings?.UiScaleMaximizedPercent ?? 150;
            set
            {
                if (Settings != null
                    && Math.Abs(Settings.UiScaleMaximizedPercent - value) > double.Epsilon)
                {
                    Settings.UiScaleMaximizedPercent = value;
                }
            }
        }

        public IReadOnlyList<string> ThreadOptions { get; } = new[] { "2", "4", "6", "8", "16" };

        public string SelectedThreads
        {
            get => Settings?.SelectedThreads ?? "6";
            set
            {
                if (Settings != null && !string.Equals(Settings.SelectedThreads, value, StringComparison.Ordinal))
                {
                    Settings.SelectedThreads = value;
                }
            }
        }

        public IReadOnlyList<string> SpeedLimitOptions { get; }
            = new[] { "不限速", "1 MB/s", "2 MB/s", "5 MB/s", "10 MB/s" };

        public string SelectedSpeedLimit
        {
            get => Settings?.SelectedSpeedLimit ?? "不限速";
            set
            {
                if (Settings != null && !string.Equals(Settings.SelectedSpeedLimit, value, StringComparison.Ordinal))
                {
                    Settings.SelectedSpeedLimit = value;
                }
            }
        }

        public IReadOnlyList<string> DefaultPageOptions => PageNavigation.DefaultOptions;

        public string SelectedDefaultPage
        {
            get => Settings?.SelectedDefaultPage ?? "世界库";
            set
            {
                if (Settings != null && !string.Equals(Settings.SelectedDefaultPage, value, StringComparison.Ordinal))
                {
                    Settings.SelectedDefaultPage = value;
                }
            }
        }

        public bool AutoSnapshotBeforeLaunch
        {
            get => Settings?.AutoSnapshotBeforeLaunch ?? false;
            set
            {
                if (Settings != null && Settings.AutoSnapshotBeforeLaunch != value)
                {
                    Settings.AutoSnapshotBeforeLaunch = value;
                }
            }
        }

        public IReadOnlyList<int> SnapshotRetentionOptions { get; } = new[] { 5, 10, 20 };

        public int SnapshotRetention
        {
            get => Settings?.SnapshotRetention ?? 5;
            set
            {
                if (Settings != null && Settings.SnapshotRetention != value)
                {
                    Settings.SnapshotRetention = value;
                }
            }
        }

        public IReadOnlyList<string> CloseBehaviorOptions { get; }
            = new[] { "退出", "最小化到托盘", "每次询问" };

        public string SelectedCloseBehavior
        {
            get => Settings?.SelectedCloseBehavior ?? "退出";
            set
            {
                if (Settings != null && !string.Equals(Settings.SelectedCloseBehavior, value, StringComparison.Ordinal))
                {
                    Settings.SelectedCloseBehavior = value;
                }
            }
        }

        public bool ReduceMotion
        {
            get => Settings?.ReduceMotion ?? false;
            set
            {
                if (Settings != null && Settings.ReduceMotion != value)
                {
                    Settings.ReduceMotion = value;
                }
            }
        }

        private void OnSettingsGeneralChanged()
        {
            if (_settingsLoaded)
            {
                _ = SaveAppearanceAsync();
            }
        }

        private void OnSettingsJavaPathChanged()
        {
            OnPropertyChanged(nameof(EffectiveJavaText));
            OnPropertyChanged(nameof(EffectiveJavaDetailText));

            if (_settingsLoaded)
            {
                _ = SaveAppearanceAsync();
            }
        }

        private static string CloseBehaviorKey(string? text)
        {
            switch (text)
            {
                case "最小化到托盘":
                    return "tray";
                case "每次询问":
                    return "ask";
                default:
                    return "exit";
            }
        }

        private static string CloseBehaviorText(string? key)
        {
            switch (key)
            {
                case "tray":
                    return "最小化到托盘";
                case "ask":
                    return "每次询问";
                default:
                    return "退出";
            }
        }



        private void OnSettingsSpeedLimitChanged()
        {
            _downloadTuning?.ApplySpeedLimit(ParseSpeedLimit(SelectedSpeedLimit));

            if (_settingsLoaded)
            {
                _ = SaveAppearanceAsync();
            }
        }

        private static int ParseSpeedLimit(string? text)
        {
            switch (text)
            {
                case "1 MB/s":
                    return 1;
                case "2 MB/s":
                    return 2;
                case "5 MB/s":
                    return 5;
                case "10 MB/s":
                    return 10;
                default:
                    return 0;
            }
        }

        private static string SpeedLimitToText(int megabytesPerSecond)
        {
            switch (megabytesPerSecond)
            {
                case 1:
                    return "1 MB/s";
                case 2:
                    return "2 MB/s";
                case 5:
                    return "5 MB/s";
                case 10:
                    return "10 MB/s";
                default:
                    return "不限速";
            }
        }

        /// <summary>默认内存（MB）：新建实例第一次启动时使用。</summary>
        public int DefaultMemoryMb
        {
            get => Settings?.DefaultMemoryMb ?? 2048;
            set
            {
                if (Settings != null && Settings.DefaultMemoryMb != value)
                {
                    Settings.DefaultMemoryMb = value;
                }
            }
        }

        /// <summary>下载是否走国内镜像（BMCLAPI）。</summary>
        public bool UseMirror
        {
            get => Settings?.UseMirror ?? true;
            set
            {
                if (Settings != null && Settings.UseMirror != value)
                {
                    Settings.UseMirror = value;
                }
            }
        }

        /// <summary>全局 JVM 参数（拼在实例参数之前）。</summary>
        public string DefaultJvmArgs
        {
            get => Settings?.DefaultJvmArgs ?? string.Empty;
            set
            {
                if (Settings != null && !string.Equals(Settings.DefaultJvmArgs, value, StringComparison.Ordinal))
                {
                    Settings.DefaultJvmArgs = value;
                }
            }
        }

        /// <summary>全局游戏参数（只传给游戏本体，拼在实例游戏参数之前）。</summary>
        public string DefaultGameArgs
        {
            get => Settings?.DefaultGameArgs ?? string.Empty;
            set
            {
                if (Settings != null && !string.Equals(Settings.DefaultGameArgs, value, StringComparison.Ordinal))
                {
                    Settings.DefaultGameArgs = value;
                }
            }
        }

        /// <summary>内存预设，点一下直接填。</summary>

        /// <summary>全局默认 Java 的可执行文件路径；空表示自动。</summary>
        public string? DefaultJavaPath
        {
            get => Settings?.DefaultJavaPath;
            set
            {
                if (Settings != null && !string.Equals(Settings.DefaultJavaPath, value, StringComparison.Ordinal))
                {
                    Settings.DefaultJavaPath = value;
                }
            }
        }
        public IReadOnlyList<string> MemoryPresets { get; } = new[] { "2048", "4096", "6144", "8192" };

        /// <summary>打开日志目录（%APPDATA%\NLMCL\logs）。</summary>
        [RelayCommand]
        public void OpenLogsFolder()
        {
            string logs = Path.Combine(_paths.DataRoot, "logs");

            try
            {
                Directory.CreateDirectory(logs);
                Process.Start(new ProcessStartInfo(logs) { UseShellExecute = true });
                StatusText = "已打开日志目录：" + logs;
            }
            catch (Exception ex)
            {
                StatusText = "打开日志目录失败：" + ex.Message;
            }
        }

        /// <summary>把日志与启动记录打成一个 zip，方便发给开发者。</summary>
        [RelayCommand]
        public void CreateDiagnosticBundle()
        {
            try
            {
                string target = Path.Combine(
                    _paths.DataRoot,
                    "diagnostics",
                    "NLMCL-诊断-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                // 只打包日志与启动记录：accounts.json 里有账号令牌，绝不能进诊断包
                string staging = Path.Combine(Path.GetTempPath(), "nlmcl-diag-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);

                try
                {
                    string logs = Path.Combine(_paths.DataRoot, "logs");

                    if (Directory.Exists(logs))
                    {
                        string logTarget = Path.Combine(staging, "logs");
                        Directory.CreateDirectory(logTarget);

                        foreach (string file in Directory.GetFiles(logs))
                        {
                            File.Copy(file, Path.Combine(logTarget, Path.GetFileName(file)), overwrite: true);
                        }
                    }

                    string launchRecord = Path.Combine(_paths.DataRoot, "last-launch.txt");

                    if (File.Exists(launchRecord))
                    {
                        File.Copy(launchRecord, Path.Combine(staging, "last-launch.txt"), overwrite: true);
                    }

                    System.IO.Compression.ZipFile.CreateFromDirectory(
                        staging, target, System.IO.Compression.CompressionLevel.Optimal, false);
                }
                finally
                {
                    try { Directory.Delete(staging, true); } catch (IOException) { }
                }

                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + target + "\"")
                {
                    UseShellExecute = true
                });

                StatusText = "诊断包已生成：" + target;
            }
            catch (Exception ex)
            {
                StatusText = "生成诊断包失败：" + ex.Message;
            }
        }

        /// <summary>在资源管理器里打开当前游戏目录。</summary>
        [RelayCommand]
        public void OpenGameDirectory()
        {
            try
            {
                if (!Directory.Exists(CurrentGameDirectory))
                {
                    StatusText = "游戏目录不存在：" + CurrentGameDirectory;
                    return;
                }

                Process.Start(new ProcessStartInfo(CurrentGameDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText = "打开游戏目录失败：" + ex.Message;
            }
        }
        /// <summary>点内存预设：直接填进默认内存。</summary>
        [RelayCommand]
        private void UseMemoryPreset(string? preset)
        {
            if (int.TryParse(preset, out int value) && value >= 512)
            {
                DefaultMemoryMb = value;
            }
        }

        private void OnSettingsMirrorChanged()
        {
            OnPropertyChanged(nameof(DownloadSourceText));

            if (_mirror != null)
            {
                _mirror.Enabled = UseMirror;
            }

            if (_settingsLoaded)
            {
                _ = SaveAppearanceAsync();
            }
        }

        private void OnSettingsMemoryChanged()
        {
            if (_settingsLoaded)
            {
                _ = SaveAppearanceAsync();
            }
        }

        private void OnSettingsLaunchArgsChanged()
        {
            if (_settingsLoaded)
            {
                _ = SaveAppearanceAsync();
            }
        }

        private void OnSettingsThreadsChanged()
        {
            if (int.TryParse(SelectedThreads, out int threads))
            {
                _downloadTuning?.ApplyThreadCount(threads);
                Download.Parallelism = threads;
            }

            if (_settingsLoaded)
            {
                _ = SaveAppearanceAsync();
            }
        }

        private void OnSettingsThemeChanged()
        {
            if (!_settingsLoaded)
            {
                return;
            }

            _appearance?.ApplyTheme(ThemeFromText(SelectedTheme));
            _ = SaveAppearanceAsync();
        }

        private void OnSettingsScaleChanged()
        {
            if (!_settingsLoaded)
            {
                return;
            }

            if (_appearance?.IsMaximized == true)
            {
                ScheduleScaleApply(ref _scaleMaximizedDebounce);
            }
            else
            {
                ScheduleScaleApply(ref _scaleDebounce);
            }
        }

        /// <summary>
        /// 拖动滑块时不能每变一次就重排窗口：缩放会改变控件尺寸，
        /// 滑块自己也会跟着缩放，鼠标下的位置一变就来回弹（窗口抖动）。
        /// 停手 250ms 后才真正缩放并保存。
        /// </summary>
        private void ScheduleScaleApply(ref CancellationTokenSource? slot)
        {
            slot?.Cancel();

            CancellationTokenSource source = new CancellationTokenSource();
            slot = source;

            _ = ApplyScaleDebouncedAsync(source.Token);
        }

        private async Task ApplyScaleDebouncedAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(250, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
            {
                return;
            }

            ApplyScaleForCurrentWindowState();
            await SaveAppearanceAsync().ConfigureAwait(true);
        }

        /// <summary>按当前窗口状态取对应档位：普通 100%，最大化 150%（可调）。</summary>
        public void ApplyScaleForCurrentWindowState()
        {
            bool maximized = _appearance?.IsMaximized ?? false;
            double percent = maximized ? UiScaleMaximizedPercent : UiScalePercent;

            _appearance?.ApplyScale(percent / 100.0);
        }


        private static string ThemeFromText(string text)
        {
            if (string.Equals(text, "深色", StringComparison.Ordinal))
            {
                return "dark";
            }

            if (string.Equals(text, "高对比度", StringComparison.Ordinal))
            {
                return "highcontrast";
            }

            return string.Equals(text, "跟随系统", StringComparison.Ordinal) ? "system" : "light";
        }

        private static string ThemeToText(string? theme)
        {
            if (string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                return "深色";
            }

            if (string.Equals(theme, "highcontrast", StringComparison.OrdinalIgnoreCase))
            {
                return "高对比度";
            }

            return string.Equals(theme, "system", StringComparison.OrdinalIgnoreCase) ? "跟随系统" : "浅色";
        }

        private static double ClampPercent(double percent)
            => Math.Max(80, Math.Min(150, Math.Round(percent)));

        private static double ClampMaximizedPercent(double percent)
            => Math.Max(100, Math.Min(200, Math.Round(percent)));

// ---------- 分区重置 ----------

        /// <summary>重置外观（主题 / 缩放），不影响其它设置。</summary>
        [RelayCommand]
        public async Task ResetAppearanceDefaultsAsync()
        {
            if (!ConfirmReset("外观", "主题、窗口缩放、最大化缩放"))
            {
                return;
            }

            SelectedTheme = "浅色";
            UiScalePercent = 100;
            UiScaleMaximizedPercent = 150;
            ReduceMotion = false;

            await SaveAppearanceAsync().ConfigureAwait(true);
            StatusText = "已重置外观设置";
        }

        /// <summary>重置下载（线程 / 镜像开关）。</summary>
        [RelayCommand]
        public async Task ResetDownloadDefaultsAsync()
        {
            if (!ConfirmReset("下载", "下载线程、镜像源开关"))
            {
                return;
            }

            SelectedThreads = "6";
            SelectedSpeedLimit = "不限速";
            UseMirror = true;

            await SaveAppearanceAsync().ConfigureAwait(true);
            StatusText = "已重置下载设置";
        }

        /// <summary>重置启动（默认内存 / 默认 Java / 全局 JVM 与游戏参数）。</summary>
        [RelayCommand]
        public async Task ResetLaunchDefaultsAsync()
        {
            if (!ConfirmReset("启动", "新实例默认内存、默认 Java、全局 JVM 参数、全局游戏参数"))
            {
                return;
            }

            DefaultMemoryMb = 2048;
            DefaultJavaPath = null;
            DefaultJvmArgs = string.Empty;
            DefaultGameArgs = string.Empty;

            await SaveAppearanceAsync().ConfigureAwait(true);

            if (JavaOptions.Count > 0)
            {
                SelectedJava = JavaOptions[0];
            }

            StatusText = "已重置启动设置";
        }

        /// <summary>重置全部设置（保留账号与游戏目录）。</summary>
        [RelayCommand]
        public async Task ResetAllDefaultsAsync()
        {
            if (!ConfirmReset("全部", "主题、缩放、下载、镜像、内存、Java、JVM 参数、游戏参数"))
            {
                return;
            }

            SelectedTheme = "浅色";
            UiScalePercent = 100;
            UiScaleMaximizedPercent = 150;
            ReduceMotion = false;
            SelectedThreads = "6";
            SelectedSpeedLimit = "不限速";
            UseMirror = true;
            DefaultMemoryMb = 2048;
            DefaultJavaPath = null;
            DefaultJvmArgs = string.Empty;
            DefaultGameArgs = string.Empty;
            AutoSnapshotBeforeLaunch = false;
            SnapshotRetention = 5;
            SelectedCloseBehavior = "退出";

            await SaveAppearanceAsync().ConfigureAwait(true);

            if (JavaOptions.Count > 0)
            {
                SelectedJava = JavaOptions[0];
            }

            StatusText = "已重置全部设置（账号与游戏目录保留）";
        }

        private bool ConfirmReset(string section, string items)
        {
            return _dialogs == null || _dialogs.Confirm(
                "重置" + section + "设置",
                "将把以下设置恢复默认：\n\n" + items + "\n\n账号、游戏目录、实例设置不受影响。继续吗？");
        }

        private async Task SaveAppearanceAsync()
        {
            try
            {
                // 先在 UI 线程取好值，再交给串行化服务落盘，避免回调里跨线程读 VM。
                string theme = ThemeFromText(SelectedTheme);
                double scale = ClampPercent(UiScalePercent) / 100.0;
                double scaleMaximized = ClampMaximizedPercent(UiScaleMaximizedPercent) / 100.0;
                int threads = int.TryParse(SelectedThreads, out int parsedThreads) ? parsedThreads : 6;
                int speedLimit = ParseSpeedLimit(SelectedSpeedLimit);
                int memory = DefaultMemoryMb <= 0 ? 2048 : DefaultMemoryMb;
                bool mirror = UseMirror;
                string? jvmArgs = string.IsNullOrWhiteSpace(DefaultJvmArgs) ? null : DefaultJvmArgs.Trim();
                string? gameArgs = string.IsNullOrWhiteSpace(DefaultGameArgs) ? null : DefaultGameArgs.Trim();
                string? javaPath = string.IsNullOrWhiteSpace(DefaultJavaPath) ? null : DefaultJavaPath;
                string defaultPage = PageNavigation.ToKey(SelectedDefaultPage);
                bool autoSnapshot = AutoSnapshotBeforeLaunch;
                int snapshotRetention = SnapshotRetention;
                string closeBehavior = CloseBehaviorKey(SelectedCloseBehavior);
                bool reduceMotion = ReduceMotion;

                await _appSettings.UpdateAsync(settings =>
                {
                    settings.Theme = theme;
                    settings.ReduceMotion = reduceMotion;
                    settings.UiScale = scale;
                    settings.UiScaleMaximized = scaleMaximized;
                    settings.DownloadThreads = threads;
                    settings.DownloadSpeedLimitMb = speedLimit;
                    settings.DefaultMemoryMb = memory;
                    settings.UseMirror = mirror;
                    settings.DefaultJvmArgs = jvmArgs;
                    settings.DefaultGameArgs = gameArgs;
                    settings.DefaultJavaPath = javaPath;
                    settings.DefaultPage = defaultPage;
                    settings.AutoSnapshotBeforeLaunch = autoSnapshot;
                    settings.SnapshotRetention = snapshotRetention;
                    settings.CloseBehavior = closeBehavior;
                }, CancellationToken.None).ConfigureAwait(true);

                if (_mirror != null)
                {
                    _mirror.Enabled = UseMirror;
                }

                // 更高版本的 settings.json 会被视为只读：SaveAsync 会安全地跳过写入，
                // 此时不能仍然告诉用户“设置已保存”，否则重启后全部复原。
                // 只读状态在启动加载时已缓存，避免每次拖动滑块都重新读一遍 JSON。
                StatusText = _settingsFileReadOnly
                    ? "设置文件由更新版本创建，本次修改不会保存；请升级启动器后重试"
                    : "设置已保存";
            }
            catch (Exception ex)
            {
                StatusText = "保存设置失败：" + ex.Message;
            }
        }

        /// <summary>当前显示「启动与日志」页。</summary>
        [ObservableProperty]
        private bool _isLaunchSection;

        /// <summary>点击侧栏：切换选中项与右侧页面。</summary>
        [RelayCommand]
        public void SelectSection(NavigationItem item)
        {
            if (item == null)
            {
                return;
            }

            // 每次进入设置页都从「全部」开始，避免上次搜索把设置藏起来。
            if (item.Section == ShellSection.Settings)
            {
                Settings?.ClearSettingsSearchCommand.Execute(null);
            }

            // 「实例快照」是独立页面：切过去时加载列表。
            if (item.Section == ShellSection.Snapshot || item.Section == ShellSection.World)
            {
                _ = LoadSnapshotPageAsync();
            }

            if (item.Section == ShellSection.World)
            {
                RefreshInstanceContent();
                _ = RefreshLaunchPreflightAsync();
            }

            foreach (NavigationGroup group in NavigationGroups)
            {
                foreach (NavigationItem candidate in group.Items)
                {
                    candidate.IsActive = ReferenceEquals(candidate, item);
                }
            }

            SelectedNavigation = item;

            IsVersionsSection = item.Section == ShellSection.Versions;
            IsWorldSection = item.Section == ShellSection.World;
            IsDownloadSection = item.Section == ShellSection.Download;

            IsLaunchSection = item.Section == ShellSection.Launch;
            IsMultiplayerSection = item.Section == ShellSection.Multiplayer;
            IsNingliSection = item.Section == ShellSection.Ningli;
            IsSettingsSection = item.Section == ShellSection.Settings;
            IsImportSection = item.Section == ShellSection.Import;
            IsSkinSection = item.Section == ShellSection.Skin;
            IsModsSection = item.Section == ShellSection.Mods;

            if (IsModsSection)
            {
                RefreshModsInstance();
            }
            IsLanScanSection = item.Section == ShellSection.LanScan;
            IsSnapshotSection = item.Section == ShellSection.Snapshot;

            // 首次进入这两页时自动拉数据，避免"点进去一片空白，要手动刷新"
            if (IsDownloadSection)
            {
                _ = Download.EnsureLoadedAsync();
            }

            if (IsSettingsSection)
            {
                _ = EnsureJavaScannedAsync();
                Cache.GameDirectory = CurrentGameDirectory;
                _ = Cache.RefreshAsync();
            }

            if (IsImportSection)
            {
                string? gameVersion = SelectedVersion?.MinecraftVersion;
                string? loader = SelectedVersion?.LoaderHint;

                _ = Resource.EnsureLoadedForInstanceAsync(gameVersion, loader);
            }

            if (IsSkinSection)
            {
                _skin?.SetAccount(Account);
                _skin?.RefreshLocalSkins();
                _skin?.ActivateLibrary();
            }
            if (IsImportSection && _resource != null)
            {
                _resource.SyncFromInstance(CurrentGameDirectory, SelectedVersion?.FolderName ?? string.Empty);
            }

            _ = SaveLastPageAsync(item.Section);
        }

        /// <summary>按分区选中；导入/Mod 已合并进下载中心，仍保留内部激活入口。</summary>
        public void SelectSection(ShellSection section)
        {
            // 导入资源 / Mod 管理已并入下载中心、局域网已并入联机；
            // 直接请求旧分区时回落到对应页面，避免空白页。
            if (section == ShellSection.Import || section == ShellSection.Mods)
            {
                section = ShellSection.Download;
            }
            else if (section == ShellSection.LanScan)
            {
                section = ShellSection.Multiplayer;
            }

            NavigationItem? item = NavigationGroups
                .SelectMany(group => group.Items)
                .FirstOrDefault(candidate => candidate.Section == section);

            if (item != null)
            {
                SelectSection(item);
            }
        }

        /// <summary>下载中心的「导入资源」标签激活时调用。</summary>
        public void ActivateResourceTab()
        {
            if (_resource == null)
            {
                return;
            }

            string? gameVersion = SelectedVersion?.MinecraftVersion;
            string? loader = SelectedVersion?.LoaderHint;
            _ = _resource.EnsureLoadedForInstanceAsync(gameVersion, loader);
            _resource.SyncFromInstance(CurrentGameDirectory, SelectedVersion?.FolderName ?? string.Empty);
        }

        /// <summary>下载中心的「Mod 管理」标签激活时调用。</summary>
        public void ActivateModsTab()
        {
            RefreshModsInstance();
        }
        

        private void ApplyStartupPage(AppSettings settings)
        {
            string key = settings.DefaultPage;
            if (string.Equals(key, "last", StringComparison.OrdinalIgnoreCase))
            {
                key = settings.LastPage;
            }

            ShellSection target = PageNavigation.ToSection(key);

            NavigationItem? item = NavigationGroups
                .SelectMany(group => group.Items)
                .FirstOrDefault(candidate => candidate.Section == target);

            if (item != null)
            {
                SelectSection(item);
            }
        }

        private async Task SaveLastPageAsync(ShellSection section)
        {
            if (!_settingsLoaded)
            {
                return;
            }

            if (!PageNavigation.TryGetKey(section, out string key))
            {
                return;
            }

            try
            {
                await _appSettings.UpdateAsync(
                    settings => settings.LastPage = key, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 页面记忆失败不影响使用
            }
        }

        /// <summary>选择游戏目录（.minecraft 所在文件夹）。</summary>
        [RelayCommand]
        public async Task PickGameDirectoryAsync()
        {
            string? picked = _folderPicker.PickFolder("选择 .minecraft 文件夹", CurrentGameDirectory);

            if (string.IsNullOrEmpty(picked))
            {
                return;
            }

            CurrentGameDirectory = picked!;
            EnsureCurrentDirectoryInList();
            await SaveGameDirectoryAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        /// <summary>刷新"我的联机地址"：本机 IPv4 + 用户填的端口。</summary>
        [RelayCommand]
        public void RefreshMyAddress()
        {
            string? ip = _network.GetLocalIPv4();

            if (string.IsNullOrEmpty(ip))
            {
                MyAddress = "未找到本机局域网地址（可能没连网络）";
                return;
            }

            if (!int.TryParse(MyPort, out int port) || port < 1 || port > 65535)
            {
                MyAddress = ip + ":端口";
                StatusText = "请在游戏里对局域网开放后，把聊天栏显示的端口填进来";
                return;
            }

            MyAddress = ip + ":" + port;
            StatusText = "把这个地址发给对方：" + MyAddress;
        }

        /// <summary>复制我的联机地址。</summary>
        [RelayCommand]
        public void CopyMyAddress()
        {
            if (string.IsNullOrEmpty(MyAddress) || MyAddress.IndexOf(':') < 0)
            {
                RefreshMyAddress();
            }

            if (string.IsNullOrEmpty(MyAddress) || MyAddress.IndexOf(':') < 0)
            {
                return;
            }

            _clipboard.SetText(MyAddress);
            StatusText = "已复制：" + MyAddress;
        }

        /// <summary>一键诊断：本机地址 → DNS → TCP → SLP，把结论写出来，不用猜。</summary>
        [RelayCommand]
        public async Task DiagnoseAsync()
        {
            var report = new System.Text.StringBuilder();

            string local = _network.GetLocalIPv4() ?? "(未获取)";
            report.AppendLine("本机 IPv4：" + local);
            report.AppendLine("本机端口：" + MyPort);
            report.AppendLine("待测地址：" + ServerAddress);

            if (!LanAddress.TryParse(ServerAddress, out string host, out int port, out string? error))
            {
                report.AppendLine("地址解析失败：" + error);
                DiagnosticsText = report.ToString();
                return;
            }

            report.AppendLine("解析结果：" + host + ":" + port);

            // 很多服务器把真实端口放在 SRV 记录里（例如 mc2.nxist.top → 46718）。
            // 游戏自己会查 SRV，但我们这里的探测不会，所以先解析一次。
            if (_addressResolver != null)
            {
                ServerEndpoint resolved = await _addressResolver
                    .ResolveAsync(host, port, CancellationToken.None).ConfigureAwait(true);

                if (resolved.FromSrv)
                {
                    report.AppendLine("SRV 记录：" + resolved.Host + ":" + resolved.Port);
                    host = resolved.Host;
                    port = resolved.Port;
                }
                else
                {
                    report.AppendLine("SRV 记录：无（按 " + port + " 连接）");
                }
            }

            if (local.IndexOf('.') > 0)
            {
                string[] a = local.Split('.');
                string[] b = host.Split('.');

                if (a.Length == 4 && b.Length == 4)
                {
                    bool sameSubnet = a[0] == b[0] && a[1] == b[1] && a[2] == b[2];
                    report.AppendLine("同网段（前三段一致）：" + (sameSubnet ? "是" : "否 —— 跨 VLAN 通常连不通"));
                }
            }

            report.AppendLine("TCP 连接测试…");
            bool open = await _network.IsPortOpenAsync(host, port, CancellationToken.None).ConfigureAwait(true);
            report.AppendLine("  TCP：" + (open ? "通" : "不通"));

            if (open && _pinger != null)
            {
                report.AppendLine("Minecraft 状态查询（SLP）…");

                try
                {
                    LanRoom room = await _pinger.PingAsync(host, port, CancellationToken.None).ConfigureAwait(true);

                    report.AppendLine("  SLP：通");
                    report.AppendLine("  版本：" + room.VersionName);
                    report.AppendLine("  人数：" + room.Online + "/" + room.Max);
                    report.AppendLine("  MOTD：" + room.Motd);
                    report.AppendLine("结论：服务器在线，可以直接连。");
                }
                catch (Exception ex)
                {
                    report.AppendLine("  SLP：失败（" + ex.Message + "）");
                    report.AppendLine("结论：端口开着但不是 Minecraft 服务，或对方世界已关闭。");
                }
            }
            else if (!open)
            {
                report.AppendLine("结论：连不上对方端口。常见原因是跨 VLAN/端口隔离、对方没开房、防火墙拦截。");
            }

            DiagnosticsText = report.ToString();
            StatusText = "诊断完成，见下方结果";
        }
        /// <summary>探测失败时的退路：不探测，直接带 --server 启动，让游戏自己报结果。</summary>
        [RelayCommand]
        public async Task ForceConnectAsync()
        {
            if (IsLaunching)
            {
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在「世界库」里选一个版本";
                return;
            }

            if (!LanAddress.TryParse(ServerAddress, out string host, out int port, out string? error))
            {
                StatusText = error ?? "联机地址格式不对";
                return;
            }

            StatusText = "跳过端口探测，直接启动并连接 " + host + ":" + port + " …";

            await LaunchCoreAsync(host + ":" + port).ConfigureAwait(true);
        }
        /// <summary>
        /// 「进大厅」：用当前选中的实例，连到档案里挑出来的最优地址。
        ///
        /// 大厅不需要任何 mod（纯净客户端也能进），所以不做 AutoModpack 安装。
        /// </summary>
        [RelayCommand]
        public async Task JoinProfileLobbyAsync(ServerProfile? profile)
        {
            if (profile == null)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _lobbyGate, 1, 0) != 0)
            {
                StatusText = "正在进入大厅，请稍候…";
                return;
            }

            try
            {
                await JoinProfileLobbyCoreAsync(profile).ConfigureAwait(true);
            }
            finally
            {
                Interlocked.Exchange(ref _lobbyGate, 0);
            }
        }

        private async Task JoinProfileLobbyCoreAsync(ServerProfile profile)
        {
            // 游戏已经启动中，别再挑入口；否则最后会被 LaunchCore 的闸门静默吞掉，
            // 状态却还停在“进入大厅”。
            if (IsLaunching)
            {
                StatusText = "游戏正在启动，请稍候…";
                return;
            }

            // 模组服正在挑入口/安装时，大厅入口不能插进来抢状态。
            // 真正的重复启动由 LaunchCoreAsync 的原子闸门兜底，这里只做交互层拦截。
            if (IsJoiningProfile)
            {
                StatusText = "正在进服，请稍候…";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在「世界库」里选一个版本，再进大厅";
                return;
            }

            if (_profilePicker == null)
            {
                StatusText = "服务器档案功能未初始化";
                return;
            }

            StatusText = "正在挑选入口：" + string.Join(" / ", profile.Addresses) + " …";

            try
            {
                JoinTrace("开始挑入口：" + string.Join(",", profile.Addresses));

                ProfileEndpoint chosen = await _profilePicker
                    .PickAsync(profile, CancellationToken.None).ConfigureAwait(true);

                if (!chosen.Online)
                {
                    StatusText = "进不了大厅：" + profile.Name + " 的地址都连不上（"
                        + (chosen.Error ?? "超时") + "）";
                    return;
                }

                // 挑入口期间用户可能从快捷入口/普通启动把游戏拉起来了。
                // 这时再进大厅只会被 LaunchCore 的闸门静默吞掉，明确停下。
                if (IsLaunching)
                {
                    StatusText = "游戏已开始启动，已停止进入大厅。";
                    return;
                }

                string target = chosen.Endpoint.Host + ":" + chosen.Endpoint.Port;

                StatusText = "进入大厅：" + target + "（" + chosen.Players + "/" + chosen.MaxPlayers + " 在线）";

                await LaunchCoreAsync(target, fromJoin: true).ConfigureAwait(true);

                RefreshJoinChecks();
            }
            catch (Exception ex)
            {
                StatusText = "进入大厅失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 「进模组服」：按档案把 MC + 加载器 + AutoModpack 装好，再启动直连。
        ///
        /// 不碰 AutoModpack 的同步协议：客户端 mod 装好、版本对，剩下交给它自己在登录阶段同步。
        /// </summary>
        [RelayCommand]
        public async Task JoinProfileModdedAsync(ServerProfile? profile)
        {
            JoinTrace("点击进服：profile=" + (profile == null ? "null" : profile.Name) + "，picker=" + (_profilePicker != null));
            if (profile == null || _profilePicker == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(profile.Minecraft))
            {
                StatusText = "档案里没写 MC 版本，无法自动安装";
                return;
            }

            if (IsJoiningProfile)
            {
                StatusText = "正在进服，请稍候…";
                return;
            }

            if (IsLaunching)
            {
                StatusText = "游戏正在启动，请稍候…";
                return;
            }

            if (Volatile.Read(ref _lobbyGate) != 0)
            {
                StatusText = "正在进入大厅，请稍候…";
                return;
            }

            IsJoiningProfile = true;

            try
            {
                try
                {
                    StatusText = "正在挑选入口：" + string.Join(" / ", profile.Addresses) + " …";

                    ProfileEndpoint chosen = await _profilePicker
                        .PickAsync(profile, CancellationToken.None).ConfigureAwait(true);

                JoinTrace("挑入口结果：" + chosen.Endpoint + " 在线=" + chosen.Online + " 错误=" + (chosen.Error ?? "-"));
                if (!chosen.Online)
                {
                    StatusText = "服务器连不上：" + profile.Name + "（" + (chosen.Error ?? "超时") + "）";
                    return;
                }

                // 挑入口/安装期间如果游戏已经被别的入口启动，别再把文件改下去。
                if (IsLaunching)
                {
                    StatusText = "游戏已开始启动，已停止进服准备。";
                    return;
                }

                string target = chosen.Endpoint.Host + ":" + chosen.Endpoint.Port;

                // 纯净档案（还原服等）：不装加载器、不装 AutoModpack，
                // 直接用原版实例进服——带 AutoModpack 的客户端会被验证服拒绝。
                if (!profile.RequiresMods)
                {
                    // 已有可用实例就直接用：重复跑安装器会让"点一下没反应"（安装要下载几百个文件）。
                    // 先刷新版本列表：刚启动时扫描可能还没完成，Versions 为空会误判成"没装"。
                    await RefreshAsync().ConfigureAwait(true);
                    VersionItem? existingVanilla = FindProfileInstance(profile);
                    JoinTrace("纯净分支：版本列表 " + Versions.Count + " 个，匹配到 " + (existingVanilla?.FolderName ?? "无"));
                    bool needInstall = existingVanilla == null || !HasClientJar(existingVanilla);

                    if (needInstall && _versionInstaller != null)
                    {
                        JoinTrace("纯净分支：开始安装 " + profile.Minecraft + " 原版");
                        StatusText = "安装 " + profile.Minecraft + " 原版 …";

                        Progress<InstallProgress> vanillaProgress = new Progress<InstallProgress>(
                            p => StatusText = p.Stage + " " + p.Percent.ToString("0", CultureInfo.InvariantCulture) + "%");

                        await _versionInstaller.InstallAsync(
                            new InstallRequest(CurrentGameDirectory, profile.Minecraft),
                            vanillaProgress,
                            CancellationToken.None).ConfigureAwait(true);
                    }

                    await RefreshAsync().ConfigureAwait(true);

                    JoinTrace("纯净分支：查找实例 minecraft=" + profile.Minecraft);

                    VersionItem? vanillaInstance = FindProfileInstance(profile);

                    if (vanillaInstance == null)
                    {
                        StatusText = "安装完成，但没找到 " + profile.Minecraft + " 原版实例";
                        return;
                    }

                    if (IsLaunching)
                    {
                        StatusText = "游戏已开始启动，已停止进服准备。";
                        return;
                    }

                    await SelectVersionForLaunchAsync(vanillaInstance).ConfigureAwait(true);
                    StatusText = "启动 " + vanillaInstance.Name + " 并连接 " + target + " …";
                    JoinTrace("纯净分支：启动 " + vanillaInstance.FolderName + " → " + target);
                    await LaunchCoreAsync(target, fromJoin: true).ConfigureAwait(true);

                RefreshJoinChecks();
                    return;
                }

                // Fabric 安装必须带版本号（不传会抛「必须指定加载器版本」）。
                // 档案里没写就查 Fabric meta 的推荐版本，并记回档案。
                string? loaderVersion = string.IsNullOrWhiteSpace(profile.LoaderVersion)
                    ? null
                    : profile.LoaderVersion;

                if (loaderVersion == null
                    && _fabricCatalog != null
                    && string.Equals(profile.Loader, "fabric", StringComparison.OrdinalIgnoreCase))
                {
                    JoinTrace("模组分支：查询 Fabric 加载器版本 " + profile.Minecraft);
                    StatusText = "查询 Fabric 加载器版本 …";

                    var loaderVersions = await _fabricCatalog
                        .GetLoaderVersionsAsync(profile.Minecraft, CancellationToken.None).ConfigureAwait(true);

                    loaderVersion = loaderVersions.FirstOrDefault();

                    if (string.IsNullOrWhiteSpace(loaderVersion))
                    {
                        StatusText = "拿不到 " + profile.Minecraft + " 的 Fabric 加载器版本";
                        return;
                    }

                    profile.LoaderVersion = loaderVersion!;
                }


                if (_versionInstaller != null)
                {
                    JoinTrace("模组分支：开始安装 " + profile.Minecraft + " " + profile.Loader + " " + (loaderVersion ?? ""));
                    StatusText = "安装 " + profile.Minecraft + " " + profile.Loader + " " + (loaderVersion ?? string.Empty) + " …";

                    Progress<InstallProgress> progress = new Progress<InstallProgress>(
                        p => StatusText = p.Stage + " " + p.Percent.ToString("0", CultureInfo.InvariantCulture) + "%");

                    await _versionInstaller.InstallAsync(
                        new InstallRequest(CurrentGameDirectory, profile.Minecraft)
                        {
                            Loader = string.IsNullOrWhiteSpace(profile.Loader) ? null : profile.Loader,
                            LoaderVersion = loaderVersion
                        },
                        progress,
                        CancellationToken.None).ConfigureAwait(true);
                }

                await RefreshAsync().ConfigureAwait(true);

                VersionItem? instance = FindProfileInstance(profile);

                if (instance == null)
                {
                    StatusText = "安装完成，但没找到对应实例；请到「世界库」手动选一个后重试";
                    return;
                }

                // 安装可能持续几分钟；期间用户可能从别处启动了游戏。
                if (IsLaunching)
                {
                    StatusText = "游戏已开始启动，已停止进服准备。";
                    return;
                }

                await EnsureAutoModpackAsync(profile, instance).ConfigureAwait(true);

                // 大型整合包给 2048 MB 会频繁 GC（表现为"一卡一卡"），按档案建议值写实例设置。
                if (profile.MemoryMb > 0 && _instanceSettings != null)
                {
                    InstanceSettings currentSettings = await _instanceSettings
                        .LoadAsync(CurrentGameDirectory, instance.FolderName, CancellationToken.None)
                        .ConfigureAwait(true);

                    if (currentSettings.MemoryMb != profile.MemoryMb)
                    {
                        currentSettings.MemoryMb = profile.MemoryMb;

                        await _instanceSettings
                            .SaveAsync(CurrentGameDirectory, instance.FolderName, currentSettings, CancellationToken.None)
                            .ConfigureAwait(true);
                    }
                }

                if (IsLaunching)
                {
                    StatusText = "游戏已开始启动，已停止进服准备。";
                    return;
                }

                await SelectVersionForLaunchAsync(instance).ConfigureAwait(true);

                JoinTrace("模组分支：启动 " + instance.FolderName + " → " + target);
                StatusText = "启动 " + instance.Name + " 并连接 " + target + " …";

                await LaunchCoreAsync(target, fromJoin: true).ConfigureAwait(true);

                RefreshJoinChecks();
            }
            catch (Exception ex)
            {
                JoinTrace("进服失败：" + ex.GetType().Name + " " + ex.Message);
                StatusText = "进模组服失败：" + ex.Message;
            }
            }
            finally
            {
                IsJoiningProfile = false;
            }
        }

        /// <summary>按档案找刚装好的实例：目录名同时包含加载器名与 MC 版本。</summary>
        private VersionItem? FindProfileInstance(ServerProfile profile)
        {
            string loader = (profile.Loader ?? string.Empty).ToLowerInvariant();
            string minecraft = profile.Minecraft ?? string.Empty;

            foreach (VersionItem item in Versions)
            {
                // 版本目录命名约定是 <加载器>-<加载器版本>-<MC 版本>；
                // 不能用 Contains，否则 1.21.1 会命中 1.21.10。
                if (ProfileInstanceMatcher.Matches(item.FolderName, minecraft, profile.Loader))
                {
                    return item;
                }

            }

            return null;
        }

        /// <summary>把 AutoModpack 客户端 mod 放进实例的 mods 目录（已有且哈希一致就跳过）。</summary>
        private async Task EnsureAutoModpackAsync(ServerProfile profile, VersionItem instance)
        {
            const string projectId = "k68glP2e";

            if (_resourceCatalog == null || _fileDownloader == null)
            {
                throw new InvalidOperationException(
                    "AutoModpack 下载服务未就绪，已停止启动。请重启启动器后重试。");
            }

            string mods = Path.Combine(CurrentGameDirectory, "versions", instance.FolderName, "mods");
            Directory.CreateDirectory(mods);

            JoinTrace("模组分支：查询 AutoModpack 版本");
            StatusText = "查询 AutoModpack 适配版本 …";

            var files = await _resourceCatalog
                .GetFilesAsync(projectId, ResourceKind.Mod, profile.Minecraft, profile.Loader, CancellationToken.None)
                .ConfigureAwait(true);

            // 服务端跑 v4.0.6 的话，装 v5.0.0-rc 会协议对不上（进服不同步）。
            ResourceFile? file = AutoModpackSelector.Pick(files, profile.AutoModpackVersion);

            if (file == null)
            {
                // 没有 AutoModpack 就启动，客户端登录后无法同步整合包，玩家只会看到
                // “进服失败/被踢”。这里直接中止，让错误停在启动器中。
                string wanted = string.IsNullOrWhiteSpace(profile.AutoModpackVersion)
                    ? "（档案未指定版本）"
                    : " " + profile.AutoModpackVersion!.Trim();
                string available = files.Count == 0
                    ? "源站没有返回可用版本"
                    : string.Join("、", files.Take(4).Select(f => f.VersionName ?? f.Name))
                      + (files.Count > 4 ? " 等" : string.Empty);

                throw new InvalidOperationException(
                    "没找到适配 " + profile.Minecraft + " / " + profile.Loader
                    + " 的 AutoModpack" + wanted + "，已停止启动。"
                    + "可用版本：" + available + "。请检查服务器档案中的 AutoModpack 版本。");
            }

            string target = Path.Combine(mods, file.Name);

            if (File.Exists(target) && !string.IsNullOrEmpty(file.Sha1) && HasSha1(target, file.Sha1!))
            {
                // 目标已正确也要清掉同目录的旧版本，否则两个 AutoModpack 会同时加载。
                DeleteStaleAutoModpackJars(mods, file.Name);
                return;
            }

            StatusText = "下载 AutoModpack " + (file.VersionName ?? file.Name) + " …";

            await _fileDownloader.DownloadAsync(file.Url, target, CancellationToken.None).ConfigureAwait(true);

            // 同目录里如果还留着别的 automodpack 版本（例如上次误装的 v5），一并删掉，
            // 否则两个 AutoModpack 同时加载会冲突。
            DeleteStaleAutoModpackJars(mods, file.Name);

            if (!string.IsNullOrEmpty(file.Sha1) && !HasSha1(target, file.Sha1!))
            {
                File.Delete(target);
                throw new InvalidOperationException("AutoModpack 校验失败（哈希不一致），已删除");
            }
        }

        internal static void DeleteStaleAutoModpackJars(string modsDirectory, string keepFileName)
        {
            foreach (string stale in Directory.GetFiles(modsDirectory, "automodpack*.jar"))
            {
                if (!string.Equals(Path.GetFileName(stale), keepFileName, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.Delete(stale);
                    }
                    catch (IOException)
                    {
                        // 被占用就算了，下一次再清
                    }
                }
            }
        }

        /// <summary>比较文件 SHA1（小写十六进制）。</summary>
        private static bool HasSha1(string path, string expected)
        {
            try
            {
                using FileStream stream = File.OpenRead(path);
                using var sha1 = System.Security.Cryptography.SHA1.Create();
                byte[] hash = sha1.ComputeHash(stream);
                string actual = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                return string.Equals(actual, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal);
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>重启游戏：停止当前进程后，用同样的版本与服务器再启动一次。</summary>
        [RelayCommand]
        public async Task RestartGameAsync()
        {
            if (IsLaunching)
            {
                return;
            }

            // 进服准备可能正握着目标实例/服务器地址；先停游戏再启动会被
            // LaunchCoreAsync 的 fromJoin 拦截，最后变成“游戏被关了但没有重开”。
            if (IsJoinPreparationInProgress())
            {
                return;
            }

            if (Console.IsRunning)
            {
                StatusText = "正在停止当前游戏 …";
                Console.Stop();

                // 不能固定延时：慢机器上旧进程没退干净就启动会双开，
                // 快机器上又白等。改成"最多等 10 秒，期间每 200ms 检查一次"。
                DateTime deadline = DateTime.UtcNow.AddSeconds(10);

                while (Console.IsRunning && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(200).ConfigureAwait(true);
                }

                if (Console.IsRunning)
                {
                    StatusText = "旧进程 10 秒内没有退出，请手动关闭游戏后再点「重启游戏」";
                    return;
                }
            }


            if (SelectedVersion == null)
            {
                StatusText = "没有可重启的实例";
                return;
            }

            StatusText = "重启游戏 …";
            await LaunchCoreAsync(_lastServerAddress).ConfigureAwait(true);
        }
        /// <summary>读取服务器档案（首次运行会写入内置的宝可梦服）。</summary>
        public async Task LoadServerProfilesAsync()
        {
            if (_profileStore == null)
            {
                return;
            }

            try
            {
                var profiles = await _profileStore.LoadAsync(CancellationToken.None).ConfigureAwait(true);

                ServerProfiles.Clear();

                foreach (ServerProfile profile in profiles)
                {
                    ServerProfiles.Add(profile);
                }
                RefreshJoinChecks();
            }
            catch (Exception ex)
            {
                StatusText = "读取服务器档案失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 刷新"进服前检查"：账号 / 客户端 / 模组 / 内存 / 整合包。
        /// 让玩家在点按钮之前就能看出缺什么。
        /// </summary>
        /// <summary>进服链路日志（排查"点了没反应"）：%APPDATA%\NLMCL\logs\join.log。</summary>
        /// <summary>实例目录里是否已有客户端 jar（判断"能否直接启动、不必重装"）。</summary>
        private bool HasClientJar(VersionItem instance)
        {
            try
            {
                string jar = Path.Combine(CurrentGameDirectory, "versions", instance.FolderName, instance.FolderName + ".jar");
                return File.Exists(jar) && new FileInfo(jar).Length > 1024 * 1024;
            }
            catch (IOException)
            {
                return false;
            }
        }


        private static void JoinTrace(string message)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NLMCL", "logs");

                Directory.CreateDirectory(dir);
                File.AppendAllText(
                    Path.Combine(dir, "join.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }


        public void RefreshJoinChecks()
        {
            Ningli.HasPrimaryProfile = PrimaryProfile != null;
            Ningli.HasVanillaProfile = VanillaProfile != null;
            Ningli.NotifyProfilesChanged();

            JoinChecks.Clear();

            ServerProfile? profile = PrimaryProfile;

            if (profile == null)
            {
                return;
            }

            // ① 账号
            JoinChecks.Add(Account != null
                ? new ServerJoinCheck("账号", Account.Name + "（" + Account.TypeLabel + "）", true)
                : new ServerJoinCheck("账号", "未登录（需要外置或正版账号）", false));

            // ② 客户端实例：直接扫磁盘，不依赖异步扫描的集合（启动初期集合为空会误报"未安装"）
            string? folder = FindInstanceOnDisk(profile);
            JoinChecks.Add(folder != null
                ? new ServerJoinCheck("客户端", folder + " 已就绪", true)
                : new ServerJoinCheck("客户端", "未安装（点按钮会自动装 " + profile.Minecraft + " + " + profile.Loader + "）", false));

            if (folder == null)
            {
                JoinChecks.Add(new ServerJoinCheck("模组", "待安装 AutoModpack " + profile.AutoModpackVersion, false));
                JoinChecks.Add(new ServerJoinCheck("整合包", "未同步（首次约 1 GB）", false));
                return;
            }

            string mods = Path.Combine(CurrentGameDirectory, "versions", folder, "mods");
            string[] autoModpackJars = Directory.Exists(mods)
                ? Directory.GetFiles(mods, "automodpack*.jar")
                : Array.Empty<string>();
            string wantedAutoModpack = (profile.AutoModpackVersion ?? string.Empty).Trim();
            bool hasAutoModpack = wantedAutoModpack.Length == 0
                ? autoModpackJars.Length > 0
                : autoModpackJars.Any(file =>
                    AutoModpackSelector.MatchesVersion(Path.GetFileName(file), wantedAutoModpack));

            JoinChecks.Add(hasAutoModpack
                ? new ServerJoinCheck("模组", "AutoModpack " + (wantedAutoModpack.Length == 0 ? "已就位" : wantedAutoModpack + " 已就位"), true)
                : wantedAutoModpack.Length > 0 && autoModpackJars.Length > 0
                    ? new ServerJoinCheck("模组", "AutoModpack 版本不匹配（需要 "
                        + wantedAutoModpack + "，点按钮会重装）", false)
                    : new ServerJoinCheck("模组", "缺 AutoModpack（点按钮会自动装）", false));

            // ③ 内存（100+ mod 的整合包 2 GB 会频繁 GC）
            int memory = GetInstanceMemory(folder);
            JoinChecks.Add(memory >= 4096
                ? new ServerJoinCheck("内存", memory + " MB", true)
                : new ServerJoinCheck("内存", memory + " MB（建议 6144，点按钮会自动改）", false));

            // ④ 整合包是否同步过
            string packs = Path.Combine(CurrentGameDirectory, "versions", folder, "automodpack", "modpacks");
            long size = 0;

            if (Directory.Exists(packs))
            {
                foreach (string file in Directory.GetFiles(packs, "*", SearchOption.AllDirectories))
                {
                    try { size += new FileInfo(file).Length; } catch (IOException) { }
                }
            }

            JoinChecks.Add(size > 100L * 1024 * 1024
                ? new ServerJoinCheck("整合包", "已同步（" + Math.Round(size / 1024d / 1024d) + " MB）", true)
                : new ServerJoinCheck("整合包", "未同步（首次约 1 GB，视网速 10–60 分钟）", false));
        }

        /// <summary>直接在 versions 目录里找匹配的实例目录名（不依赖异步扫描的集合）。</summary>
        private string? FindInstanceOnDisk(ServerProfile profile)
        {
            try
            {
                string versions = Path.Combine(CurrentGameDirectory, "versions");

                if (!Directory.Exists(versions))
                {
                    return null;
                }

                foreach (string dir in Directory.GetDirectories(versions))
                {
                    string name = Path.GetFileName(dir);

                    if (ProfileInstanceMatcher.Matches(name, profile.Minecraft, profile.Loader))
                    {
                        return name;
                    }
                }
            }
            catch (IOException)
            {
            }

            return null;
        }
        /// <summary>读实例设置里的内存；没有设置文件就返回全局默认。</summary>
        private int GetInstanceMemory(string folder)
        {
            try
            {
                string settings = Path.Combine(CurrentGameDirectory, "versions", folder, "nlmcl-settings.json");

                if (File.Exists(settings))
                {
                    string json = File.ReadAllText(settings);
                    var match = System.Text.RegularExpressions.Regex.Match(json, @"""memoryMb""\s*:\s*(\d+)");

                    if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
                    {
                        return value;
                    }
                }
            }
            catch (IOException)
            {
            }

            return 2048;
        }

        /// <summary>实例实际使用的游戏目录：优先问 PCL（版本隔离规则），失败退回版本目录。</summary>
        internal static string ResolveGameDirectory(
            IInstancePathResolver? resolver, string minecraftDirectory, string versionFolder)
        {
            try
            {
                if (resolver != null)
                {
                    return resolver.GetGameDirectory(minecraftDirectory, versionFolder);
                }
            }
            catch (Exception)
            {
                // 解析失败就按隔离处理，至少和"版本目录里有 mods"的常见情况一致
            }

            return Path.Combine(minecraftDirectory, "versions", versionFolder);
        }

        private void RefreshModsInstance()
        {
            if (_mods == null)
            {
                return;
            }

            if (SelectedVersion == null)
            {
                _mods.SetInstance(string.Empty, "未选择实例", string.Empty, string.Empty);
                return;
            }

            string minecraft = CurrentGameDirectory;
            string folder = SelectedVersion.FolderName;

            // 版本隔离由 PCL 决定（版本目录里有 mods/saves 就隔离）：必须问同一个解析器，
            // 否则「Mod 管理」看到的目录和游戏实际读取的目录会对不上。
            string modsDirectory = Path.Combine(
                ResolveGameDirectory(_instancePaths, minecraft, folder), "mods");
            string loader = folder.IndexOf("fabric", StringComparison.OrdinalIgnoreCase) >= 0 ? "fabric"
                : folder.IndexOf("forge", StringComparison.OrdinalIgnoreCase) >= 0 ? "forge"
                : folder.IndexOf("neoforge", StringComparison.OrdinalIgnoreCase) >= 0 ? "neoforge"
                : string.Empty;

            _mods.SetInstance(modsDirectory, folder, SelectedVersion.BasedOn ?? string.Empty, loader);
        }
        /// <summary>载入常用服务器（首次运行会写入「方块宁理」三条线路）。</summary>
        private async Task LoadServersAsync()
        {
            if (_bookmarks == null)
            {
                return;
            }

            try
            {
                IReadOnlyList<ServerBookmark> servers = await _bookmarks
                    .LoadAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                Servers.Clear();

                foreach (ServerBookmark server in servers)
                {
                    Servers.Add(server);
                }
            }
            catch (Exception)
            {
                // 读不到就空着，不影响联机页其它功能
            }
        }

        /// <summary>把输入的服务器加入列表并落盘。</summary>
        [RelayCommand]
        public async Task AddServerAsync()
        {
            string address = (NewServerAddress ?? string.Empty).Trim();

            if (!ServerBookmark.TryParse(address, out _, out _, out string? error))
            {
                StatusText = "地址不对：" + error;
                return;
            }

            if (Servers.Any(item => string.Equals(item.Address, address, StringComparison.OrdinalIgnoreCase)))
            {
                StatusText = "这个地址已经在列表里了";
                return;
            }

            string name = string.IsNullOrWhiteSpace(NewServerName) ? address : NewServerName.Trim();

            Servers.Add(new ServerBookmark(name, address));

            NewServerName = string.Empty;
            NewServerAddress = string.Empty;

            await PersistServersAsync().ConfigureAwait(true);

            StatusText = "已添加服务器：" + name;
        }

        /// <summary>从列表移除并落盘。</summary>
        [RelayCommand]
        public async Task RemoveServerAsync(ServerBookmark? server)
        {
            if (server == null)
            {
                return;
            }

            Servers.Remove(server);

            await PersistServersAsync().ConfigureAwait(true);

            StatusText = "已移除 " + server.Name;
        }

        private async Task PersistServersAsync()
        {
            if (_bookmarks == null)
            {
                return;
            }

            try
            {
                await _bookmarks.SaveAsync(Servers.ToList(), CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText = "保存服务器列表失败：" + ex.Message;
            }
        }
        /// <summary>把已保存的另一个账号设为当前。</summary>
        [RelayCommand]
        public async Task SwitchAccountAsync(Account? account)
        {
            if (account == null)
            {
                return;
            }

            // 已经是这个账号就不用切；注意退出登录后 Account 为 null，这里不能一起挡掉
            if (Account != null && string.Equals(account.Uuid, Account.Uuid, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool saved = true;
            string? saveError = null;

            try
            {
                await _accounts.SwitchAsync(account, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // 账号文件可能被只读目录、杀毒软件或另一个实例占用。
                // 切换本身不应被持久化失败挡住：先在内存里切过去，再明确提示未能保存。
                saved = false;
                saveError = ex.Message;
            }

            Account = account;
            Console.Account = account;
            IsAccountPopupOpen = false;

            await ReloadSavedAccountsAsync().ConfigureAwait(true);

            StatusText = saved
                ? "已切换到 " + account.Name
                : "已切换到 " + account.Name + "，但未能写入账号文件：" + saveError;
        }

        /// <summary>删除已保存的账号（当前账号被删时自动切到剩下的一个）。</summary>
        [RelayCommand]
        public async Task RemoveAccountAsync(Account? account)
        {
            if (account == null)
            {
                return;
            }

            if (_dialogs != null && !_dialogs.Confirm(
                    "移除账号",
                    "确定要从列表里移除「" + account.Name + "」吗？\n\n只删本地保存的登录信息，不影响账号本身。"))
            {
                return;
            }

            try
            {
                await _accounts.RemoveAsync(account, CancellationToken.None).ConfigureAwait(true);

                Account? current = await _accounts.LoadAsync(CancellationToken.None).ConfigureAwait(true);

                Account = current;
                Console.Account = current;

                await ReloadSavedAccountsAsync().ConfigureAwait(true);

                StatusText = current == null ? "已移除账号，当前未登录" : "已移除 " + account.Name;
            }
            catch (Exception ex)
            {
                StatusText = "移除账号失败：" + ex.Message;
            }
        }

        private async Task ReloadSavedAccountsAsync()
        {
            try
            {
                IReadOnlyList<Account> accounts = await _accounts
                    .LoadAllAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                SavedAccounts.Clear();

                foreach (Account item in accounts)
                {
                    SavedAccounts.Add(new AccountItemViewModel(item)
                    {
                        IsCurrent = Account != null &&
                            string.Equals(item.Uuid, Account.Uuid, StringComparison.OrdinalIgnoreCase)
                    });
                }

                RefreshSavedAccountSelection();

                // 头像各自异步拉：一个账号失败不影响其它行。
                foreach (AccountItemViewModel item in SavedAccounts)
                {
                    _ = LoadSavedAccountAvatarAsync(item);
                }
            }
            catch (Exception)
            {
                // 读不到就空着
            }
        }

        private void RefreshSavedAccountSelection()
        {
            foreach (AccountItemViewModel item in SavedAccounts)
            {
                item.IsCurrent = Account != null &&
                    string.Equals(item.Account.Uuid, Account.Uuid, StringComparison.OrdinalIgnoreCase);
            }
        }

        private async Task LoadSavedAccountAvatarAsync(AccountItemViewModel item)
        {
            if (_skins == null)
            {
                return;
            }

            try
            {
                item.AvatarPng = await _skins
                    .GetAvatarPngAsync(item.Account, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 头像失败就保留 "?"，不弹错误
            }
        }
        /// <summary>
        /// 进服准备期间不要再去解析地址/探测端口：真正启动会被 LaunchCore 拦下，
        /// 提前探测只会白等一轮网络超时。
        /// </summary>
        private bool IsJoinPreparationInProgress()
        {
            if (!IsJoiningProfile && Volatile.Read(ref _lobbyGate) == 0)
            {
                return false;
            }

            StatusText = "正在准备进入服务器，请稍候…";
            return true;
        }

        /// <summary>快捷入口：填入地址并直接启动（不探测端口——公网服务器常挡住端口探测）。</summary>
        [RelayCommand]
        public async Task QuickJoinAsync(ServerBookmark? server)
        {
            if (server == null || IsLaunching)
            {
                return;
            }

            if (IsJoinPreparationInProgress())
            {
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在「世界库」里选一个版本，再点快捷入口";
                return;
            }

            ServerAddress = server.DisplayAddress;
            StatusText = "正在启动并连接 " + server.DisplayAddress + " …";

            await LaunchCoreAsync(server.Endpoint).ConfigureAwait(true);
        }

        /// <summary>
        /// 连接对方的房间：解析地址 → 探测端口 → 带 --server 启动。
        /// 先探测是必要的：地址打错时立刻告诉用户，而不是让游戏卡在"连接中"。
        /// </summary>
        [RelayCommand]
        public async Task ConnectToLanAsync()
        {
            if (IsLaunching)
            {
                return;
            }

            if (IsJoinPreparationInProgress())
            {
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在「世界库」里选一个版本";
                return;
            }

            if (!LanAddress.TryParse(ServerAddress, out string host, out int port, out string? error))
            {
                StatusText = error ?? "联机地址格式不对";
                return;
            }

            string targetHost = host;
            int targetPort = port;

            // 域名可能把真实端口藏在 SRV 记录里；诊断页会查，直连入口也必须查，
            // 否则客户端进得去、启动器却先判“端口未开放”。
            if (_addressResolver != null)
            {
                try
                {
                    ServerEndpoint endpoint = await _addressResolver
                        .ResolveAsync(host, port, CancellationToken.None).ConfigureAwait(true);
                    targetHost = endpoint.Host;
                    targetPort = endpoint.Port;
                }
                catch (Exception ex)
                {
                    StatusText = "解析服务器地址失败：" + ex.Message;
                    return;
                }
            }

            StatusText = "正在探测 " + targetHost + ":" + targetPort + " …";

            bool open = await _network
                .IsPortOpenAsync(targetHost, targetPort, CancellationToken.None).ConfigureAwait(true);
            if (!open)
            {
                StatusText = "连不上 " + targetHost + ":" + targetPort +
                             " —— 对方可能没开房、端口填错，或不在同一网段（校园网跨 VLAN 常被拦）";
                return;
            }

            await LaunchCoreAsync(targetHost + ":" + targetPort).ConfigureAwait(true);
        }

        /// <summary>
        /// 没有登录账号时的兜底：用名字输入框造一个离线账号。
        /// 否则 --uuid / --accessToken 会生成空参数，把后面的参数顶位。
        /// </summary>
        private Account BuildImplicitOfflineAccount()
        {
            string name = string.IsNullOrWhiteSpace(PlayerNameInput) ? "Player" : PlayerNameInput.Trim();

            try
            {
                return OfflineAccountFactory.Create(name);
            }
            catch (ArgumentException)
            {
                return OfflineAccountFactory.Create("Player");
            }
        }

        /// <summary>把实例设置翻译成游戏参数：全局默认 + 实例额外参数（窗口尺寸/全屏另走设置）。</summary>
        internal IReadOnlyList<string> BuildExtraGameArguments()
        {
            List<string> args = new List<string>();

            string global = (DefaultGameArgs ?? string.Empty).Trim();

            if (global.Length > 0)
            {
                args.AddRange(global.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries));
            }

            string extra = (ExtraArguments ?? string.Empty).Trim();

            if (extra.Length > 0)
            {
                args.AddRange(extra.Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries));
            }

            // 窗口尺寸与全屏不走命令行：PCL 的参数管线读 LaunchArgumentWindow*，
            // 而且多份 --width/--height 会互相抵消（见厂商代码注释），所以改由启动器写设置。

            return args;
        }

        /// <summary>实例级 HTTP/HTTPS 代理，走 JVM 系统属性；不修改系统代理。</summary>
        internal string BuildProxyJvmArguments()
        {
            if (!UseProxy || string.IsNullOrWhiteSpace(ProxyHost) || ProxyPort <= 0 || ProxyPort > 65535)
            {
                return string.Empty;
            }

            string host = ProxyHost.Trim();
            if (host.Contains(" ") || host.Contains("\t"))
            {
                return string.Empty;
            }

            return string.Join(" ", new[]
            {
                "-Dhttp.proxyHost=" + host,
                "-Dhttp.proxyPort=" + ProxyPort,
                "-Dhttps.proxyHost=" + host,
                "-Dhttps.proxyPort=" + ProxyPort,
                "-Dhttp.nonProxyHosts=localhost|127.0.0.1"
            });
        }

        /// <summary>
        /// 图形兼容参数。Windows 没有可移植的 OpenGL/Vulkan 切换开关，
        /// 这里只提供 LWJGL2 的软件 OpenGL 回退，供旧显卡或驱动异常时使用。
        /// </summary>
        internal string BuildGraphicsJvmArguments()
        {
            return SoftwareOpenGl
                ? "-Dorg.lwjgl.opengl.Display.allowSoftwareOpenGL=true"
                : string.Empty;
        }


        /// <summary>读取当前实例的设置。</summary>
        public async Task LoadInstanceSettingsAsync(VersionItem item)
        {
            if (item == null)
            {
                return;
            }

            try
            {
                InstanceSettings settings = await _instanceSettings
                    .LoadAsync(CurrentGameDirectory, item.FolderName, CancellationToken.None)
                    .ConfigureAwait(true);

                // 用户可能在 IO 期间又点了别的实例；晚到的旧结果不能覆盖新选择。
                if (!ReferenceEquals(SelectedVersion, item))
                {
                    return;
                }

                // 新实例还没有设置文件：用设置页里的"默认内存"
                bool fresh = !_instanceSettings.Exists(CurrentGameDirectory, item.FolderName);

                ApplyInstanceSettings(settings, fresh);
            }
            catch (Exception ex)
            {
                // A 的设置读取失败也可能晚于用户切到 B；此时不能把 B 的界面重置成默认值。
                if (!ReferenceEquals(SelectedVersion, item))
                {
                    return;
                }

                // 设置文件损坏/不可读时不能把上一个实例的代理、内存等继续带过来。
                ApplyInstanceSettings(new InstanceSettings(), fresh: true);
                StatusText = "读取实例设置失败：" + ex.Message;
            }
        }

        private void ApplyInstanceSettings(InstanceSettings settings, bool fresh)
        {
            bool previousSuppress = _suppressProxyPreflightRefresh;
            _suppressProxyPreflightRefresh = true;
            try
            {
                MemoryMb = fresh && DefaultMemoryMb > 0 ? DefaultMemoryMb : settings.MemoryMb;
                JavaPath = settings.JavaPath ?? string.Empty;
                ExtraArguments = settings.ExtraArguments ?? string.Empty;
                WindowWidth = settings.WindowWidth;
                WindowHeight = settings.WindowHeight;
                Fullscreen = settings.Fullscreen;
                UseProxy = settings.UseProxy;
                ProxyHost = settings.ProxyHost ?? string.Empty;
                ProxyPort = settings.ProxyPort;
                SoftwareOpenGl = settings.SoftwareOpenGl;
            }
            finally
            {
                _suppressProxyPreflightRefresh = previousSuppress;
            }
        }

// ---------- Java 运行时管理 ----------

        /// <summary>探测到的 Java 列表（第一项固定是「自动」）。</summary>
        private static readonly ObservableCollection<JavaOption> EmptyJavaOptions
            = new ObservableCollection<JavaOption>();

        public ObservableCollection<JavaOption> JavaOptions => Settings?.JavaOptions ?? EmptyJavaOptions;

        public JavaOption? SelectedJava
        {
            get => Settings?.SelectedJava;
            set
            {
                if (Settings != null && !ReferenceEquals(Settings.SelectedJava, value))
                {
                    Settings.SelectedJava = value;
                }
            }
        }

        public bool IsScanningJava
        {
            get => Settings?.IsScanningJava ?? false;
            set
            {
                if (Settings != null)
                {
                    Settings.IsScanningJava = value;
                }
            }
        }

        public string JavaStatus
        {
            get => Settings?.JavaStatus ?? "尚未探测";
            set
            {
                if (Settings != null)
                {
                    Settings.JavaStatus = value;
                }
            }
        }

        /// <summary>兼容入口：真正的 Java 添加逻辑在 SettingsViewModel。</summary>
        public Task AddJavaAsync()
        {
            return Settings.AddJavaAsync();
        }

        /// <summary>兼容入口：真正的 Java 探测逻辑在 SettingsViewModel。</summary>
        public Task RefreshJavaAsync()
        {
            return Settings.RefreshJavaAsync();
        }

        /// <summary>让列表里的单选按钮与 SelectedJava 保持一致。</summary>
        private void SyncJavaSelection()
        {
            foreach (JavaOption option in JavaOptions)
            {
                option.IsSelected = ReferenceEquals(option, SelectedJava);
            }
        }

        /// <summary>选出真正要用的 Java 路径：默认 Java &gt; 实例手填 &gt; 自动挑。</summary>
        internal static string? PickConfiguredJava(
            string? defaultJavaPath, string? instanceJavaPath, IReadOnlyList<JavaRuntime> javas, int? required)
        {
            if (!string.IsNullOrWhiteSpace(defaultJavaPath) && File.Exists(defaultJavaPath))
            {
                return defaultJavaPath;
            }

            if (!string.IsNullOrWhiteSpace(instanceJavaPath) && File.Exists(instanceJavaPath))
            {
                return instanceJavaPath;
            }

            return JavaRequirements.Pick(javas, required)?.ExecutablePath;
        }

        public async Task SaveSettingsAsync()
        {
            if (SelectedVersion == null)
            {
                StatusText = "请先选择一个版本";
                return;
            }

            VersionItem version = SelectedVersion;

            try
            {
                await _instanceSettings.SaveAsync(
                    CurrentGameDirectory,
                    version.FolderName,
                    new InstanceSettings
                    {
                        MemoryMb = MemoryMb,
                        JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath,
                        ExtraArguments = string.IsNullOrWhiteSpace(ExtraArguments) ? null : ExtraArguments,
                        WindowWidth = WindowWidth,
                        WindowHeight = WindowHeight,
                        Fullscreen = Fullscreen,
                        UseProxy = UseProxy,
                        ProxyHost = string.IsNullOrWhiteSpace(ProxyHost) ? null : ProxyHost.Trim(),
                        ProxyPort = ProxyPort,
                        SoftwareOpenGl = SoftwareOpenGl
                    },
                    CancellationToken.None).ConfigureAwait(true);

                StatusText = "已保存 " + version.Name + " 的启动设置";

                // 保存后立即刷新；取消尚未触发的输入防抖，避免重复扫描。
                CancelProxyPreflightDebounce();
                if (ReferenceEquals(SelectedVersion, version))
                {
                    await RefreshLaunchPreflightAsync();
                }
            }
            catch (Exception ex)
            {
                StatusText = "保存实例设置失败：" + ex.Message;
            }
        }

        /// <summary>微软登录：拿设备码 → 打开浏览器 → 轮询到账号。</summary>
        [RelayCommand]
        public async Task MicrosoftSignInAsync()
        {
            if (IsMicrosoftSigningIn)
            {
                return;
            }

            string? clientId = string.IsNullOrWhiteSpace(MicrosoftClientId)
                ? _microsoft.ClientId
                : MicrosoftClientId.Trim();

            if (string.IsNullOrWhiteSpace(clientId))
            {
                StatusText = "微软登录需要 Azure 应用的 client id：请填在登录弹层下方（获取步骤见 docs/FIX-6-ACCOUNTS.md）";
                return;
            }

            _microsoft.ClientId = clientId;

            // 记住这次填的 id，下次不用再填（环境变量仍然优先）
            try
            {
                string remembered = clientId!;
                await _appSettings.UpdateAsync(
                    settings => settings.MicrosoftClientId = remembered,
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 存不上不影响登录本身
            }

            IsMicrosoftSigningIn = true;

            try
            {
                DeviceCodeChallenge challenge = await _microsoft
                    .StartAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                DeviceCodeText = challenge.UserCode;
                _deviceVerificationUri = string.IsNullOrWhiteSpace(challenge.VerificationUri)
                    ? "https://www.microsoft.com/link"
                    : challenge.VerificationUri;

                int minutes = Math.Max(1, (int)Math.Round((challenge.ExpiresAt - DateTimeOffset.Now).TotalMinutes));
                DeviceCodeHint = "有效约 " + minutes + " 分钟 · 浏览器输入后这里会自动继续，不用关窗口";
                StatusText = "请在浏览器里输入设备码：" + challenge.UserCode;

                // 打开授权页。失败不影响流程——用户也能手动访问地址
                try
                {
                    Process.Start(new ProcessStartInfo(challenge.VerificationUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    StatusText = "请手动打开 " + challenge.VerificationUri + "（" + ex.Message + "）";
                }

                Account account = await _microsoft
                    .WaitForAccountAsync(challenge, CancellationToken.None)
                    .ConfigureAwait(true);

                await _accounts.SaveAsync(account, CancellationToken.None).ConfigureAwait(true);
            await ReloadSavedAccountsAsync().ConfigureAwait(true);

                Account = account;
                Console.Account = account;
                StatusText = "已登录微软账号：" + account.Name;
                IsAccountPopupOpen = false;
            }
            catch (Exception ex)
            {
                StatusText = "微软登录失败：" + ex.Message;
            }
            finally
            {
                IsMicrosoftSigningIn = false;
                DeviceCodeText = string.Empty;
            }
        }

        /// <summary>
        /// 外置登录：Yggdrasil 账号密码登录（MUA 联合认证 / 皮肤站）。
        /// 密码只在本次调用里用掉，成功后立刻清空，不写进任何文件。
        /// </summary>
        [RelayCommand]
        public async Task ExternalSignInAsync()
        {
            if (IsExternalSigningIn)
            {
                return;
            }

            if (_externalAuthenticator == null)
            {
                StatusText = "外置登录不可用（未装配认证器）。";
                return;
            }

            IsExternalSigningIn = true;
            StatusText = "正在登录外置账号…";

            try
            {
                Account account = await _externalAuthenticator
                    .LoginAsync(ExternalServer, ExternalUserName, ExternalPassword, CancellationToken.None)
                    .ConfigureAwait(true);

                await _accounts.SaveAsync(account, CancellationToken.None).ConfigureAwait(true);
            await ReloadSavedAccountsAsync().ConfigureAwait(true);

                Account = account;
                Console.Account = account;
                ExternalPassword = string.Empty;
                IsAccountPopupOpen = false;

                StatusText = "已登录外置账号：" + account.Name + "（" + account.TypeLabel + "）";
            }
            catch (Exception ex)
            {
                StatusText = "外置登录失败：" + ex.Message;
            }
            finally
            {
                IsExternalSigningIn = false;
            }
        }

        /// <summary>打开快照面板，并加载当前版本的快照列表。</summary>
        [RelayCommand]
        /// <summary>启动前自动快照；失败不阻止启动。</summary>
        internal async Task CreateAutoSnapshotAsync()
        {
            if (!AutoSnapshotBeforeLaunch
                || _snapshotService == null
                || SelectedVersion == null)
            {
                return;
            }

            try
            {
                StatusText = "正在创建启动前自动快照…";

                await _snapshotService.CreateAsync(
                    CurrentGameDirectory,
                    SelectedVersion.FolderName,
                    "[自动] 启动前",
                    CancellationToken.None).ConfigureAwait(true);

                int keep = SnapshotRetention <= 0 ? 5 : SnapshotRetention;
                IReadOnlyList<SnapshotInfo> all = await _snapshotService
                    .ListAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                foreach (SnapshotInfo old in all
                    .Where(item => item.FolderName == SelectedVersion.FolderName
                        && item.Note.StartsWith("[自动]", StringComparison.Ordinal))
                    .OrderByDescending(item => item.CreatedAt)
                    .Skip(keep))
                {
                    await _snapshotService
                        .DeleteAsync(old.Id, CancellationToken.None)
                        .ConfigureAwait(true);
                }

                StatusText = "已创建启动前自动快照。";
            }
            catch (Exception ex)
            {
                // 快照失败不能阻止玩家进游戏。
                StatusText = "自动快照失败，继续启动：" + ex.Message;
            }
        }

        public async Task OpenSnapshotsAsync()
        {
            if (_snapshotService == null)
            {
                StatusText = "快照服务不可用。";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本，再查看它的快照。";
                return;
            }

            IsSnapshotPanelOpen = true;
            await ReloadSnapshotsAsync().ConfigureAwait(true);
        }

        [RelayCommand]
        public void CloseSnapshots()
        {
            IsSnapshotPanelOpen = false;
        }

        /// <summary>把当前实例的存档/配置/选项存一份。</summary>
        [RelayCommand]
        public async Task CreateSnapshotAsync()
        {
            if (_snapshotService == null || SelectedVersion == null)
            {
                return;
            }

            IsSnapshotBusy = true;
            StatusText = "正在创建快照…";

            try
            {
                SnapshotInfo info = await _snapshotService.CreateAsync(
                    CurrentGameDirectory, SelectedVersion.FolderName, SnapshotNote, CancellationToken.None)
                    .ConfigureAwait(true);

                SnapshotNote = string.Empty;
                await ReloadSnapshotsAsync().ConfigureAwait(true);

                StatusText = "已创建快照：" + info.Title + "（" + info.SizeText + "）";
            }
            catch (Exception ex)
            {
                StatusText = "创建快照失败：" + ex.Message;
            }
            finally
            {
                IsSnapshotBusy = false;
            }
        }

        /// <summary>回滚到某个快照（服务内部会先自动备份当前状态）。</summary>
        [RelayCommand]
        public async Task RestoreSnapshotAsync(SnapshotInfo? snapshot)
        {
            if (_snapshotService == null || snapshot == null || SelectedVersion == null)
            {
                return;
            }

            // 回滚会覆盖 saves/config/options.txt，属于不可逆操作；没有对话框端口时按确认处理（单测/降级）
            if (_dialogs != null && !_dialogs.Confirm(
                    "回滚快照",
                    "将用快照「" + snapshot.Title + "」覆盖当前实例的 saves、config、options.txt。\n\n"
                    + "回滚前会自动备份当前状态，但仍建议先确认这是正确的实例。"))
            {
                StatusText = "已取消回滚。";
                return;
            }

            IsSnapshotBusy = true;
            StatusText = "正在回滚到「" + snapshot.Title + "」…";

            try
            {
                await _snapshotService
                    .RestoreAsync(snapshot, CurrentGameDirectory, CancellationToken.None)
                    .ConfigureAwait(true);

                await ReloadSnapshotsAsync().ConfigureAwait(true);

                StatusText = "已回滚到「" + snapshot.Title + "」，回滚前的状态已自动备份。";
            }
            catch (Exception ex)
            {
                StatusText = "回滚失败：" + ex.Message;
            }
            finally
            {
                IsSnapshotBusy = false;
            }
        }

        [RelayCommand]
        public async Task DeleteSnapshotAsync(SnapshotInfo? snapshot)
        {
            if (_snapshotService == null || snapshot == null)
            {
                return;
            }

            // 删除不可撤销：先确认。没有对话框端口时按"确认"处理（单测/降级）
            if (_dialogs != null && !_dialogs.Confirm(
                    "删除快照",
                    "确定删除快照「" + snapshot.Title + "」吗？\n\n"
                    + snapshot.Summary + "\n删除后无法恢复（不影响游戏存档本身）。"))
            {
                StatusText = "已取消删除。";
                return;
            }

            try
            {
                await _snapshotService.DeleteAsync(snapshot.Id, CancellationToken.None).ConfigureAwait(true);
                await ReloadSnapshotsAsync().ConfigureAwait(true);

                StatusText = "已删除快照：" + snapshot.Title;
            }
            catch (Exception ex)
            {
                StatusText = "删除快照失败：" + ex.Message;
            }
        }

        /// <summary>进入「实例快照」页时加载数据（不弹覆盖层）。</summary>
        private async Task LoadSnapshotPageAsync()
        {
            if (_snapshotService == null)
            {
                StatusText = "快照服务不可用。";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本，再查看它的快照。";
                return;
            }

            await ReloadSnapshotsAsync().ConfigureAwait(true);
        }

        /// <summary>刷新「N 个 · X MB」统计（增删快照后调用）。</summary>
        /// <summary>顶栏「快照」按钮：跳到实例快照页（不再弹覆盖层）。</summary>
        [RelayCommand]
        private void GoToSnapshots()
        {
            foreach (NavigationGroup group in NavigationGroups)
            {
                foreach (NavigationItem item in group.Items)
                {
                    if (item.Section == ShellSection.Snapshot)
                    {
                        SelectSection(item);
                        return;
                    }
                }
            }
        }
        /// <summary>页面上点「刷新」：重新读快照列表。</summary>
        [RelayCommand]
        private async Task RefreshSnapshotsAsync()
        {
            await ReloadSnapshotsAsync().ConfigureAwait(true);
        }

        /// <summary>页面上点「清空全部」：逐个删除当前实例的快照。</summary>
        [RelayCommand]
        private async Task ClearSnapshotsAsync()
        {
            if (Snapshots.Count == 0)
            {
                StatusText = "这个版本还没有快照。";
                return;
            }

            int removed = 0;

            // 先复制一份，避免边遍历边删
            foreach (SnapshotInfo item in Snapshots.ToList())
            {
                try
                {
                    await DeleteSnapshotAsync(item).ConfigureAwait(true);
                    removed++;
                }
                catch (Exception ex)
                {
                    StatusText = "删除失败：" + ex.Message;
                    return;
                }
            }

            StatusText = "已清空 " + removed + " 个快照";
        }
        private void RefreshSnapshotTotals()
        {
            long bytes = 0;

            foreach (SnapshotInfo item in Snapshots)
            {
                bytes += item.SizeBytes;
            }

            SnapshotTotalText = Snapshots.Count + " 个 · "
                + (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        private async Task ReloadSnapshotsAsync()
        {
            if (_snapshotService == null)
            {
                return;
            }

            IReadOnlyList<SnapshotInfo> list = await _snapshotService
                .ListAsync(CancellationToken.None)
                .ConfigureAwait(true);

            Snapshots.Clear();

            foreach (SnapshotInfo item in list)
            {
                // 只显示当前实例的快照：别的版本的数据目录不同，回滚过来没有意义
                if (SelectedVersion == null
                    || string.Equals(item.FolderName, SelectedVersion.FolderName, StringComparison.OrdinalIgnoreCase))
                {
                    Snapshots.Add(item);
                }
            }

            RefreshSnapshotTotals();

            if (Snapshots.Count == 0)
            {
                StatusText = "这个版本还没有快照。";
            }
        }

        /// <summary>
        /// 启动时初始化：读回上次的游戏目录 → 读回账号 → 扫描版本。
        /// 顺序有意义：目录要先于扫描确定，否则会扫错地方。
        /// </summary>
        [RelayCommand]
        public async Task InitializeAsync()
        {
            try
            {
                AppSettings settings = await _appSettings.LoadAsync(CancellationToken.None).ConfigureAwait(true);

                _settingsFileReadOnly = settings.IsReadOnly;
                SettingsReadOnlyWarning = settings.IsReadOnly
                    ? settings.ReadOnlyReason ?? "设置文件来自更高版本，当前版本不会覆盖它。"
                    : string.Empty;

                if (!string.IsNullOrWhiteSpace(settings.GameDirectory) &&
                    System.IO.Directory.Exists(settings.GameDirectory))
                {
                    CurrentGameDirectory = settings.GameDirectory!;
                }

                LoadGameDirectories(settings);

                // 微软登录的 client id：设置里存过就带出来（环境变量仍优先，构造函数里已设）
                if (!string.IsNullOrWhiteSpace(settings.MicrosoftClientId))
                {
                    MicrosoftClientId = settings.MicrosoftClientId!;

                    if (string.IsNullOrWhiteSpace(_microsoft.ClientId))
                    {
                        _microsoft.ClientId = settings.MicrosoftClientId;
                    }
                }

                // 设置页的当前值 + 立刻应用一次外观（窗口可能已经显示，DynamicResource 会即时刷新）
                ApplySettingsToUi(settings);
                ApplyStartupPage(settings);
                _settingsLoaded = true;
            }
            catch (Exception ex)
            {
                StatusText = "读取设置失败：" + ex.Message;
            }

            await LoadAccountAsync().ConfigureAwait(true);
            await LoadServerProfilesAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        /// <summary>
        /// 把 AppSettings 应用到界面。Initialize 和设置导入共用，避免两套赋值逻辑漂移。
        /// 应用期间暂时关闭自动保存，避免每个属性触一次落盘。
        /// </summary>
        private void ApplySettingsToUi(AppSettings settings)
        {
            bool wasLoaded = _settingsLoaded;
            _settingsLoaded = false;

            _lastLaunched = new Dictionary<string, DateTimeOffset>(
                settings.LastLaunched ?? new Dictionary<string, DateTimeOffset>(),
                StringComparer.OrdinalIgnoreCase);

            try
            {
                SelectedTheme = ThemeToText(settings.Theme);
                ReduceMotion = settings.ReduceMotion;
                UiScalePercent = ClampPercent(settings.UiScale <= 0 ? 100 : settings.UiScale * 100);
                UiScaleMaximizedPercent = ClampMaximizedPercent(
                    settings.UiScaleMaximized <= 0 ? 150 : settings.UiScaleMaximized * 100);
                SelectedThreads = (settings.DownloadThreads <= 0 ? 6 : settings.DownloadThreads).ToString();
                SelectedSpeedLimit = SpeedLimitToText(settings.DownloadSpeedLimitMb);
                SelectedDefaultPage = PageNavigation.ToText(settings.DefaultPage);
                AutoSnapshotBeforeLaunch = settings.AutoSnapshotBeforeLaunch;
                SnapshotRetention = settings.SnapshotRetention <= 0 ? 5 : settings.SnapshotRetention;
                SelectedCloseBehavior = CloseBehaviorText(settings.CloseBehavior);

                _downloadTuning?.ApplyThreadCount(settings.DownloadThreads <= 0 ? 6 : settings.DownloadThreads);
                _downloadTuning?.ApplySpeedLimit(settings.DownloadSpeedLimitMb);
                Download.Parallelism = settings.DownloadThreads <= 0 ? 6 : settings.DownloadThreads;

                DefaultMemoryMb = settings.DefaultMemoryMb <= 0 ? 2048 : settings.DefaultMemoryMb;
                UseMirror = settings.UseMirror;
                DefaultJvmArgs = settings.DefaultJvmArgs ?? string.Empty;
                DefaultGameArgs = settings.DefaultGameArgs ?? string.Empty;
                DefaultJavaPath = settings.DefaultJavaPath;

                if (_mirror != null)
                {
                    _mirror.Enabled = settings.UseMirror;
                }

                _appearance?.ApplyTheme(ThemeFromText(SelectedTheme));
            }
            finally
            {
                _settingsLoaded = wasLoaded;
            }

            // 窗口最大化/还原时按对应档位重算（只订阅一次）
            if (_appearance != null && !_windowStateHooked)
            {
                _windowStateHooked = true;
                _appearance.WindowStateChanged += (_, __) => ApplyScaleForCurrentWindowState();
            }

            ApplyScaleForCurrentWindowState();
        }

        /// <summary>设置导入后重新读回并应用到界面。</summary>
        private async Task ApplyImportedSettingsAsync()
        {
            try
            {
                AppSettings settings = await _appSettings.LoadAsync(CancellationToken.None).ConfigureAwait(true);
                ApplySettingsToUi(settings);
                StatusText = "已重新载入导入的设置";
            }
            catch (Exception ex)
            {
                StatusText = "重新载入设置失败：" + ex.Message;
            }
        }

        private void LoadGameDirectories(AppSettings settings)
        {
            GameDirectories.Clear();

            foreach (string directory in settings.GameDirectories ?? new List<string>())
            {
                string normalized = NormalizeDirectory(directory);

                if (!string.IsNullOrEmpty(normalized)
                    && !GameDirectories.Any(item => string.Equals(
                        item, normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    GameDirectories.Add(normalized);
                }
            }

            EnsureCurrentDirectoryInList();
            SelectedGameDirectory = CurrentGameDirectory;
        }

        private void EnsureCurrentDirectoryInList()
        {
            string normalized = NormalizeDirectory(CurrentGameDirectory);

            if (string.IsNullOrEmpty(normalized))
            {
                return;
            }

            if (!GameDirectories.Any(item => string.Equals(
                    item, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                GameDirectories.Add(normalized);
            }

            SelectedGameDirectory = normalized;
        }

        private static string NormalizeDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return string.Empty;
            }

            try
            {
                string full = Path.GetFullPath(directory!);
                string root = Path.GetPathRoot(full) ?? string.Empty;

                return full.Length > root.Length
                    ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    : full;
            }
            catch (Exception)
            {
                return directory!.Trim();
            }
        }

        [RelayCommand]
        public async Task AddGameDirectoryAsync()
        {
            if (_folderPicker == null)
            {
                StatusText = "目录选择不可用。";
                return;
            }

            string? picked = _folderPicker.PickFolder(
                "选择 .minecraft 文件夹", CurrentGameDirectory);

            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            string directory = ResolveMinecraftDirectory(picked!);

            if (!Directory.Exists(directory))
            {
                StatusText = "目录不存在：" + picked;
                return;
            }

            if (!GameDirectories.Any(item => string.Equals(
                    item, directory, StringComparison.OrdinalIgnoreCase)))
            {
                GameDirectories.Add(directory);
            }

            CurrentGameDirectory = directory;
            SelectedGameDirectory = directory;
            await SaveGameDirectoryAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        [RelayCommand]
        public async Task ScanAllGameDirectoriesAsync()
        {
            if (_directoryScanner == null)
            {
                StatusText = "全盘扫描不可用。";
                return;
            }

            if (IsScanningDirectories)
            {
                return;
            }

            IsScanningDirectories = true;
            ScanProgressText = "正在准备扫描…";
            StatusText = "正在扫描整台电脑的 .minecraft 目录…";
            _scanFoundDuringRun.Clear();
            _scanDirectoriesCts?.Dispose();
            _scanDirectoriesCts = new CancellationTokenSource();
            CancellationToken token = _scanDirectoriesCts.Token;

            IProgress<GameDirectoryScanProgress> progress =
                new Progress<GameDirectoryScanProgress>(item =>
                {
                    ScanProgressText = item.Stage +
                        " · 已检查 " + item.ScannedDirectories +
                        " 个目录 · 找到 " + item.FoundCount + " 个" +
                        (string.IsNullOrWhiteSpace(item.CurrentDirectory)
                            ? string.Empty
                            : "\n" + item.CurrentDirectory);

                    if (!string.IsNullOrWhiteSpace(item.FoundDirectory))
                    {
                        _scanFoundDuringRun.Add(item.FoundDirectory!);
                    }
                });

            try
            {
                IReadOnlyList<string> found = await _directoryScanner
                    .ScanAsync(progress, token)
                    .ConfigureAwait(true);

                int added = await ApplyScannedDirectoriesAsync(found).ConfigureAwait(true);

                StatusText = "全盘扫描完成：发现 " + found.Count +
                             " 个 .minecraft，新增 " + added + " 个目录";
            }
            catch (OperationCanceledException)
            {
                int added = await ApplyScannedDirectoriesAsync(_scanFoundDuringRun)
                    .ConfigureAwait(true);

                StatusText = "全盘扫描已停止：已发现 " + _scanFoundDuringRun.Count +
                             " 个 .minecraft，新增 " + added + " 个目录";
            }
            catch (Exception ex)
            {
                StatusText = "全盘扫描失败：" + ex.Message;
            }
            finally
            {
                _scanDirectoriesCts?.Dispose();
                _scanDirectoriesCts = null;
                IsScanningDirectories = false;
                ScanProgressText = string.Empty;
            }
        }

        [RelayCommand]
        public void StopScanAllGameDirectories()
        {
            _scanDirectoriesCts?.Cancel();
        }

        private async Task<int> ApplyScannedDirectoriesAsync(IEnumerable<string> paths)
        {
            int added = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string path in paths)
            {
                string directory = ResolveMinecraftDirectory(path);

                if (!seen.Add(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                if (!GameDirectories.Any(item => string.Equals(
                        item, directory, StringComparison.OrdinalIgnoreCase)))
                {
                    GameDirectories.Add(directory);
                    added++;
                }
            }

            if (GameDirectories.Count > 0
                && (string.IsNullOrWhiteSpace(CurrentGameDirectory)
                    || !Directory.Exists(CurrentGameDirectory)))
            {
                CurrentGameDirectory = GameDirectories[0];
                SelectedGameDirectory = CurrentGameDirectory;
            }

            await SaveGameDirectoryAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
            return added;
        }

        private static string ResolveMinecraftDirectory(string path)
        {
            string normalized = NormalizeDirectory(path);

            if (string.IsNullOrEmpty(normalized))
            {
                return normalized;
            }

            // 用户可能选中了 .minecraft 的父目录（例如 E:\NLMCL-game）。
            if (!string.Equals(
                    Path.GetFileName(normalized), ".minecraft", StringComparison.OrdinalIgnoreCase))
            {
                string child = Path.Combine(normalized, ".minecraft");

                if (Directory.Exists(child)
                    && !Directory.Exists(Path.Combine(normalized, "versions")))
                {
                    return NormalizeDirectory(child);
                }
            }

            return normalized;
        }

        [RelayCommand]
        public async Task RemoveGameDirectoryAsync()
        {
            string directory = NormalizeDirectory(CurrentGameDirectory);

            if (GameDirectories.Count <= 1)
            {
                StatusText = "至少保留一个游戏目录。";
                return;
            }

            GameDirectories.Remove(directory);

            if (GameDirectories.Count > 0
                && string.Equals(CurrentGameDirectory, directory, StringComparison.OrdinalIgnoreCase))
            {
                CurrentGameDirectory = GameDirectories[0];
            }

            SelectedGameDirectory = CurrentGameDirectory;
            await SaveGameDirectoryAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        private async Task ApplyDownloadDirectoryAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            CurrentGameDirectory = path;
            EnsureCurrentDirectoryInList();
            await SaveGameDirectoryAsync().ConfigureAwait(true);
        }

        /// <summary>记住当前游戏目录。</summary>
        private async Task SaveGameDirectoryAsync()
        {
            try
            {
                // 只改目录：其它字段（主题、缩放、wizardCompleted、clientId）必须原样保留。
                string directory = CurrentGameDirectory;
                List<string> directories = GameDirectories.ToList();

                await _appSettings.UpdateAsync(settings =>
                {
                    settings.GameDirectory = directory;
                    settings.GameDirectories = directories;
                }, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText = "保存设置失败：" + ex.Message;
            }
        }


        /// <summary>离线登录：用输入的名字生成账号并持久化。</summary>
        [RelayCommand]
        public async Task SignInOfflineAsync()
        {
            try
            {
                Account account = OfflineAccountFactory.Create(PlayerNameInput);
                await _accounts.SaveAsync(account, CancellationToken.None).ConfigureAwait(true);
            await ReloadSavedAccountsAsync().ConfigureAwait(true);

                Account = account;
                Console.Account = account;
                StatusText = "已登录离线账号：" + account.Name;
                IsAccountPopupOpen = false;
            }
            catch (Exception ex)
            {
                StatusText = "登录失败：" + ex.Message;
            }
        }

        /// <summary>退出登录。</summary>
        [RelayCommand]
        public async Task SignOutAsync()
        {
            Account? leaving = Account;

            // 只登出，不删账号：账号仍留在列表里，随时可以再切回来
            await _accounts.SignOutAsync(CancellationToken.None).ConfigureAwait(true);

            Account = null;
            Console.Account = null;

            await ReloadSavedAccountsAsync().ConfigureAwait(true);

            StatusText = leaving == null
                ? "已退出登录"
                : "已退出 " + leaving.Name + "（账号仍保留在列表里，可随时切换回来）";

            IsAccountPopupOpen = false;
        }
        /// <summary>首次进入设置页时探测一次 Java（避免启动就扫盘拖慢）。</summary>
        public async Task EnsureJavaScannedAsync()
        {
            if (JavaOptions.Count > 0 || IsScanningJava)
            {
                return;
            }

            await RefreshJavaAsync().ConfigureAwait(true);
        }

        /// <summary>启动时读回上次的账号（只做一次）。</summary>
        public async Task LoadAccountAsync()
        {
            if (_accountLoaded)
            {
                return;
            }

            _accountLoaded = true;

            try
            {
                Account? account = await _accounts.LoadAsync(CancellationToken.None).ConfigureAwait(true);
                if (account != null)
                {
                    // 令牌会过期：启动时静默续期一次。
                    // 失败就保留旧账号（可能仍然有效），真正过期时启动会报错并提示重新登录。
                    if (account.CanRefresh)
                    {
                        try
                        {
                            Account refreshed = account.Type == NLMCL.Abstractions.AccountType.External
                                ? await ExternalRefreshAsync(account).ConfigureAwait(true)
                                : await _microsoft
                                    .RefreshAsync(account.RefreshToken!, CancellationToken.None)
                                    .ConfigureAwait(true);

                            await _accounts.SaveAsync(refreshed, CancellationToken.None).ConfigureAwait(true);
                            account = refreshed;
                        }
                        catch (Exception ex)
                        {
                            StatusText = "账号续期失败，可能需要重新登录（" + ex.Message + "）";
                        }
                    }

                    Account = account;
                    Console.Account = account;
                    PlayerNameInput = account.Name;
                }
            }
            catch (Exception ex)
            {
                StatusText = "读取账号失败：" + ex.Message;
            }

            // 当前账号读完后，把「已保存账号」列表也填上；
            // 否则首次启动时弹层只有离线/微软登录，看不到可切换的账号。
            await ReloadSavedAccountsAsync().ConfigureAwait(true);
        }

        /// <summary>外置账号续期；没装配认证器时抛异常，由调用方保留旧账号。</summary>
        private Task<Account> ExternalRefreshAsync(Account account)
        {
            if (_externalAuthenticator == null)
            {
                throw new InvalidOperationException("外置登录不可用（未装配认证器）。");
            }

            return _externalAuthenticator.RefreshAsync(account, CancellationToken.None);
        }

        /// <summary>点选列表里的一个版本。切换高亮并作为启动目标。</summary>
        [RelayCommand]
        public void SelectVersion(VersionItem item)
        {
            SelectVersion(item, loadSettings: true);
        }

        private void SelectVersion(VersionItem item, bool loadSettings)
        {
            if (item == null)
            {
                return;
            }

            foreach (VersionItem candidate in Versions)
            {
                candidate.IsSelected = ReferenceEquals(candidate, item);
            }

            if (!string.IsNullOrWhiteSpace(item.GameDirectory)
                && !string.Equals(item.GameDirectory, CurrentGameDirectory, StringComparison.OrdinalIgnoreCase))
            {
                CurrentGameDirectory = item.GameDirectory;
                SelectedGameDirectory = item.GameDirectory;
            }

            SelectedVersion = item;
            HasSelection = true;
            StatusText = "已选择 " + item.Name;

            // 旧实例的预检结果立刻作废。实例设置是异步读的，等它读完再刷新会让上一个
            // 实例的“代理不完整”等告警短暂挂在新实例上，看起来像新实例也有问题。
            ResetLaunchPreflightForSelection();

            // 切版本时先带出实例设置，再按最新代理/模组状态做预检，避免读到上一个实例的配置
            if (loadSettings)
            {
                _ = LoadInstanceSettingsAndPreflightAsync(item);
            }
        }

        /// <summary>“进模组服”会直接启动，必须等实例设置读完再启动，不能沿用上一个实例的代理/内存。</summary>
        internal async Task SelectVersionForLaunchAsync(VersionItem item)
        {
            SelectVersion(item, loadSettings: false);

            // 进服会在加载完设置后立刻启动；预检也要一起收尾，否则启动被取消
            // （代理不完整、用户拒绝模组告警、缺 JAR/Java）时世界页会一直停在“正在检查…”。
            await LoadInstanceSettingsAndPreflightAsync(item).ConfigureAwait(true);
        }

        private void ResetLaunchPreflightForSelection()
        {
            CancelProxyPreflightDebounce();
            _launchPreflightGeneration++;
            LaunchPreflightWarnings.Clear();
            OnPropertyChanged(nameof(HasLaunchPreflightWarnings));
            LaunchPreflightSummary = "正在检查启动前问题…";
            IsLaunchPreflightBusy = true;
        }

        private async Task LoadInstanceSettingsAndPreflightAsync(VersionItem item)
        {
            await LoadInstanceSettingsAsync(item);
            if (ReferenceEquals(SelectedVersion, item))
            {
                await RefreshLaunchPreflightAsync();
            }
        }

        /// <summary>启动前预检结果。只读扫描，不改 Mod 文件。</summary>
        public ObservableCollection<string> LaunchPreflightWarnings { get; } = new ObservableCollection<string>();

        public bool HasLaunchPreflightWarnings => LaunchPreflightWarnings.Count > 0;

        [ObservableProperty]
        private string _launchPreflightSummary = "尚未检查。";

        [ObservableProperty]
        private bool _isLaunchPreflightBusy;

        private int _launchPreflightGeneration;

        /// <summary>
        /// 在世界页展示启动前会遇到的模组/实例问题，避免玩家点启动后才看到弹框。
        /// </summary>
        [RelayCommand]
        public async Task RefreshLaunchPreflightAsync()
        {
            int generation = ++_launchPreflightGeneration;
            VersionItem? version = SelectedVersion;

            LaunchPreflightWarnings.Clear();
            OnPropertyChanged(nameof(HasLaunchPreflightWarnings));

            if (version == null)
            {
                IsLaunchPreflightBusy = false;
                LaunchPreflightSummary = "先选择一个实例。";
                return;
            }

            IsLaunchPreflightBusy = true;

            try
            {
                List<string> warnings = new List<string>();

                if (version.HasWarning)
                {
                    warnings.Add("实例缺少客户端 JAR，启动会被阻止。请先修复或重新安装。");
                }

                if (UseProxy && (string.IsNullOrWhiteSpace(ProxyHost)
                    || ProxyHost.Contains(" ") || ProxyHost.Contains("\t")
                    || ProxyPort <= 0 || ProxyPort > 65535))
                {
                    warnings.Add("代理设置不完整，启动会被阻止。");
                }

                if (_mods != null)
                {
                    string modsDirectory = Path.Combine(
                        ResolveGameDirectory(_instancePaths, CurrentGameDirectory, version.FolderName),
                        "mods");

                    warnings.AddRange(await _mods
                        .GetLaunchWarningsAsync(modsDirectory, CancellationToken.None)
                        .ConfigureAwait(true));
                }

                if (generation != _launchPreflightGeneration || !ReferenceEquals(SelectedVersion, version))
                {
                    return;
                }

                foreach (string warning in warnings)
                {
                    LaunchPreflightWarnings.Add(warning);
                }

                OnPropertyChanged(nameof(HasLaunchPreflightWarnings));
                LaunchPreflightSummary = warnings.Count == 0
                    ? "未发现启动前问题。"
                    : "发现 " + warnings.Count + " 项，启动时会再次确认。";
            }
            catch (Exception ex)
            {
                if (generation == _launchPreflightGeneration)
                {
                    LaunchPreflightSummary = "预检未完成：" + ex.Message;
                }
            }
            finally
            {
                if (generation == _launchPreflightGeneration)
                {
                    IsLaunchPreflightBusy = false;
                }
            }
        }

        partial void OnUseProxyChanged(bool value)
        {
            ScheduleProxyPreflightRefresh();
        }

        partial void OnProxyHostChanged(string value)
        {
            ScheduleProxyPreflightRefresh();
        }

        partial void OnProxyPortChanged(int value)
        {
            ScheduleProxyPreflightRefresh();
        }

        /// <summary>代理输入会连续触发；停顿后再扫描，避免每敲一个字符都读 mods 目录。</summary>
        private void ScheduleProxyPreflightRefresh()
        {
            if (_suppressProxyPreflightRefresh || SelectedVersion == null)
            {
                return;
            }

            CancelProxyPreflightDebounce();
            CancellationTokenSource cts = new CancellationTokenSource();
            _proxyPreflightDebounce = cts;
            _ = DebounceProxyPreflightAsync(cts);
        }

        private void CancelProxyPreflightDebounce()
        {
            _proxyPreflightDebounce?.Cancel();
            _proxyPreflightDebounce = null;
        }

        private async Task DebounceProxyPreflightAsync(CancellationTokenSource cts)
        {
            bool canceled = false;
            try
            {
                await Task.Delay(400, cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            finally
            {
                if (ReferenceEquals(_proxyPreflightDebounce, cts))
                {
                    _proxyPreflightDebounce = null;
                }

                cts.Dispose();
            }

            if (!canceled)
            {
                await RefreshLaunchPreflightAsync();
            }
        }

        /// <summary>打开某个版本的只读详情浮层。</summary>
        [RelayCommand]
        public void ShowVersionDetails(VersionItem item)
        {
            if (item == null)
            {
                return;
            }

            SelectVersion(item);

            VersionDetailsLines.Clear();
            VersionDetailsTitle = "版本详情 · " + item.Name;
            VersionDetailsSubtitle = item.GameDirectory;

            AddVersionDetails("版本名称", item.Name);
            AddVersionDetails("文件夹", item.FolderName);
            AddVersionDetails("基础版本", item.MinecraftVersion);
            AddVersionDetails("加载器", DescribeLoader(item.LoaderHint));
            AddVersionDetails("类型", item.KindLabel);
            AddVersionDetails("最后启动", item.LastLaunchLabel);
            AddVersionDetails("来源目录", item.GameDirectory);

            VersionDetailsData? data = null;

            try
            {
                data = _versionDetails?.Read(item.GameDirectory, item.FolderName);
            }
            catch (Exception ex)
            {
                AddVersionDetails("读取失败", ex.Message);
            }

            if (data != null)
            {
                AddVersionDetails("版本 JSON", data.VersionJsonExists
                    ? data.VersionJsonPath
                    : data.VersionJsonPath + "（缺失）");

                AddVersionDetails("客户端 JAR", data.ClientJarExists
                    ? data.ClientJarPath + "（" + FormatSize(data.ClientJarSize) + "）"
                    : data.ClientJarPath + "（缺失）");

                AddVersionDetails("版本 ID", data.VersionId ?? "未声明");
                AddVersionDetails("主类", data.MainClass ?? "未声明");
                AddVersionDetails("继承自", data.InheritsFrom ?? "无");
                AddVersionDetails("资源索引", data.Assets ?? "未声明");
                AddVersionDetails("Java 要求", data.JavaMajor.HasValue
                    ? "Java " + data.JavaMajor.Value
                    : "未声明");
                AddVersionDetails("库数量", data.LibraryCount.ToString());
                AddVersionDetails("模组数量", data.ModCount + " 个（mods 目录）");

                if (!string.IsNullOrWhiteSpace(data.Error))
                {
                    AddVersionDetails("读取警告", data.Error!);
                }
            }
            else
            {
                AddVersionDetails("详情读取", "详情服务不可用");
            }

            AddVersionDetails("实例内存", MemoryMb + " MB");
            AddVersionDetails("实例 Java", string.IsNullOrWhiteSpace(JavaPath) ? "自动选择" : JavaPath);
            AddVersionDetails("额外参数", string.IsNullOrWhiteSpace(ExtraArguments)
                ? "无"
                : ExtraArguments);

            IsVersionDetailsOpen = true;
        }

        [RelayCommand]
        public void CloseVersionDetails()
        {
            IsVersionDetailsOpen = false;
        }

        private void AddVersionDetails(string label, string value)
        {
            VersionDetailsLines.Add(new VersionDetailsLine(label, value));
        }

        private static string DescribeLoader(string loader)
        {
            if (string.IsNullOrWhiteSpace(loader))
            {
                return "原版";
            }

            switch (loader.ToLowerInvariant())
            {
                case "fabric":
                    return "Fabric";
                case "forge":
                    return "Forge";
                case "neoforge":
                    return "NeoForge";
                case "quilt":
                    return "Quilt";
                default:
                    return loader;
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            if (bytes < 1024L * 1024L)
            {
                return (bytes / 1024.0).ToString("0.0") + " KB";
            }

            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
        }

        private static string FormatDiskSize(long bytes)
        {
            double gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return gb.ToString("0.0") + " GB";
        }

        /// <summary>
        /// 启动选中的版本。顺序：选 Java → 组装请求 → 交给 <see cref="IGameLauncher"/>。
        /// Java 由调用方探测后传进请求，启动器不替用户猜版本。
        /// </summary>
        [RelayCommand]
        public Task LaunchSelectedAsync()
        {
            return LaunchCoreAsync(null);
        }

        /// <summary>启动的统一入口：普通启动传 null，联机启动传对方地址。</summary>
        private async Task LaunchCoreAsync(string? serverAddress, bool fromJoin = false)
        {
            // 进服准备（挑入口/安装）已经从用户手里拿走了这次操作；此时再点普通启动
            // 会把状态和游戏进程抢过去。进服流程自己的启动传 fromJoin=true。
            if (!fromJoin && (IsJoiningProfile || Volatile.Read(ref _lobbyGate) != 0))
            {
                StatusText = "正在准备进入服务器，请稍候…";
                return;
            }

            if (Interlocked.CompareExchange(ref _launchGate, 1, 0) != 0)
            {
                return;
            }

            try
            {
                _lastServerAddress = serverAddress;
                IsLaunching = true;

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本";
                return;
            }

            if (UseProxy && (string.IsNullOrWhiteSpace(ProxyHost)
                || ProxyHost.Contains(" ") || ProxyHost.Contains("\t")
                || ProxyPort <= 0 || ProxyPort > 65535))
            {
                StatusText = "代理设置不完整：请填写主机和 1–65535 的端口，或关闭代理后再启动。";
                return;
            }

            // 渲染器冲突不会阻止游戏启动，但通常会在进入世界后黑屏/崩溃。
            // 启动前给一次可取消的提醒，不篡改玩家装好的 Mod。
            if (_mods != null && _dialogs != null)
            {
                try
                {
                    string modsDirectory = Path.Combine(
                        ResolveGameDirectory(_instancePaths, CurrentGameDirectory, SelectedVersion.FolderName),
                        "mods");
                    IReadOnlyList<string> warnings = await _mods
                        .GetLaunchWarningsAsync(modsDirectory, CancellationToken.None)
                        .ConfigureAwait(true);

                    if (warnings.Count > 0
                        && !_dialogs.Confirm(
                            "启动前检查",
                            "检测到以下问题：\n\n- " + string.Join("\n- ", warnings)
                            + "\n\n仍要启动吗？"))
                    {
                        StatusText = "已取消启动：启动前检查发现模组问题。";
                        return;
                    }
                }
                catch (Exception)
                {
                    // 读取 mods 失败不应挡住启动。
                }
            }

            // 缺客户端 JAR 的实例直接拉起 JVM 只会弹「A Java Exception has occurred.」，
            // 对用户毫无信息量。这里提前拦住，并告诉他去哪儿修。
            if (SelectedVersion.HasWarning)
            {
                StatusText = "该实例缺少客户端 JAR，无法启动。请到「下载中心」安装对应原版，"
                    + "或重新安装这个实例。";
                return;
            }

            StatusText = "正在查找 Java…";
            VersionItem launchedVersion = SelectedVersion;

            try
            {
                IReadOnlyList<JavaRuntime> javas = await _javaLocator
                    .FindInstalledAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                // 优先级：设置里的全局默认 Java > 实例手填 > 按版本要求自动挑。
                string? javaExecutable = null;
                Version? javaVersion = null;

                if (!string.IsNullOrWhiteSpace(DefaultJavaPath) && File.Exists(DefaultJavaPath))
                {
                    javaExecutable = DefaultJavaPath;
                    javaVersion = JavaVersionProbe.TryRead(javaExecutable);

                    if (javaVersion == null)
                    {
                        StatusText = "全局默认 Java 读不到版本，已回退到自动选择";
                        javaExecutable = null;
                    }
                }

                if (javaExecutable == null && !string.IsNullOrWhiteSpace(JavaPath))
                {
                    javaExecutable = JavaPath;
                }

                if (javaExecutable == null)
                {
                    int? required = JavaRequirements.ReadRequiredMajor(
                        CurrentGameDirectory, SelectedVersion.FolderName);

                    JavaRuntime? picked = JavaRequirements.Pick(javas, required);

                    if (picked == null)
                    {
                        // 本机没有合适的 Java：按版本 JSON 声明的组件名自动下载 Mojang 运行时。
                        // 装 1.21 时用户机器上通常只有 17，没有这一步就只能干看着报错。
                        string? component = JavaRequirements.ReadComponent(
                            CurrentGameDirectory, SelectedVersion.FolderName);

                        if (_javaInstaller != null && !string.IsNullOrEmpty(component))
                        {
                            StatusText = "本机没有合适的 Java，正在下载 " + component + "…";

                            try
                            {
                                JavaRuntime installed = await _javaInstaller
                                    .InstallAsync(component!, null, CancellationToken.None)
                                    .ConfigureAwait(true);

                                picked = JavaRequirements.Pick(new[] { installed }, required)
                                    ?? installed;

                                StatusText = "已下载 Java " + picked.Major + "，正在启动…";
                            }
                            catch (Exception ex)
                            {
                                StatusText = "下载 Java 运行时失败：" + ex.Message;
                                return;
                            }
                        }
                    }

                    if (picked == null)
                    {
                        StatusText = required.HasValue
                            ? "该版本需要 Java " + required.Value
                                + "，本机只有 " + JavaRequirements.Describe(javas)
                                + "（不兼容）。请安装 Java " + required.Value + "。"
                            : "没有找到可用的 Java。请安装该版本所需的 Java 运行时后重试";
                        return;
                    }

                    javaExecutable = picked.ExecutablePath;
                    javaVersion = picked.Version;

                    if (required.HasValue && picked.Major != required.Value)
                    {
                        // 没有精确匹配时明确告知，而不是静默用一个可能不兼容的版本
                        StatusText = "未找到 Java " + required.Value + "，将使用 Java " + picked.Major + " 启动";
                    }
                }

                if (string.IsNullOrEmpty(javaExecutable))
                {
                    StatusText = "没有找到可用的 Java。请安装该版本所需的 Java 运行时后重试";
                    return;
                }

                await CreateAutoSnapshotAsync().ConfigureAwait(true);

                StatusText = "正在启动 " + SelectedVersion.Name + "（内存 " + MemoryMb + " MB）…";

                LaunchRequest request = new LaunchRequest(CurrentGameDirectory, SelectedVersion.FolderName)
                {
                    JavaExecutable = javaExecutable,
                    JavaVersion = javaVersion,
                    MaximumMemoryMb = MemoryMb,
                    ServerAddress = serverAddress
                }.WithAccount(Account ?? BuildImplicitOfflineAccount());

            request.ExtraGameArguments = BuildExtraGameArguments();
            request.WindowWidth = WindowWidth;
            request.WindowHeight = WindowHeight;
            request.Fullscreen = Fullscreen;

                // 外置登录：把 authlib-injector 挂到 JVM 上。
                // 失败就明确报错，不要带着空身份启动（服务器会直接踢人，日志还看不出原因）。
                try
                {
                    // 全局 JVM 参数（设置页）拼在外置登录注入的参数之前。
                    // 这里以前算出了 globalJvm 却直接覆盖，导致设置页的输入框完全不生效。
                    string globalJvm = string.IsNullOrWhiteSpace(DefaultJvmArgs)
                        ? string.Empty
                        : DefaultJvmArgs.Trim();

                    string? externalJvm = await ExternalLaunch
                        .BuildJvmArgumentsAsync(Account, _authlibInjector, CancellationToken.None)
                        .ConfigureAwait(true);

                    request.ExtraJvmArguments = string.Join(" ",
                        new[] { globalJvm, BuildProxyJvmArguments(), BuildGraphicsJvmArguments(), externalJvm ?? string.Empty }
                            .Where(part => !string.IsNullOrWhiteSpace(part)));
                }
                catch (Exception ex)
                {
                    StatusText = "外置登录准备失败：" + ex.Message;
                    return;
                }

                if (_sessionLauncher != null)
                {
                    // 统一走会话式启动：捕获输出并交给控制台，这样从「世界库」启动
                    // 也能在「启动与日志」里看到日志、也能停止（以前两条路径互不相干）。
                    IGameSession session = await _sessionLauncher
                        .StartAsync(request, CancellationToken.None)
                        .ConfigureAwait(true);

                    Console.Attach(session);
                    await RecordLastLaunchAsync(launchedVersion).ConfigureAwait(true);

                    StatusText = "已启动 " + SelectedVersion.Name
                        + "（PID " + session.ProcessId + "），日志见「启动与日志」";
                    return;
                }

                LaunchResult result = await _launcher
                    .LaunchAsync(request, CancellationToken.None)
                    .ConfigureAwait(true);

                if (result.Succeeded)
                {
                    await RecordLastLaunchAsync(launchedVersion).ConfigureAwait(true);
                }

                StatusText = result.Succeeded
                    ? "已启动 " + SelectedVersion.Name + "（PID " + result.ProcessId + "）"
                    : "启动失败：" + result.Message;
            }
            catch (Exception ex)
            {
                StatusText = "启动失败：" + ex.Message;
            }
            finally
            {
                IsLaunching = false;
            }
            }
            finally
            {
                IsLaunching = false;
                Interlocked.Exchange(ref _launchGate, 0);
            }
        }

        /// <summary>启动成功后记录实例的最后启动时间；写设置失败不影响启动结果。</summary>
        private async Task RecordLastLaunchAsync(VersionItem version)
        {
            if (string.IsNullOrEmpty(version.InstancePath))
            {
                return;
            }

            DateTimeOffset launchedAt = DateTimeOffset.Now;
            string key = version.InstancePath;

            _lastLaunched[key] = launchedAt;
            version.MarkLaunched(launchedAt);

            try
            {
                await _appSettings.UpdateAsync(settings =>
                {
                    if (settings.LastLaunched == null)
                    {
                        settings.LastLaunched = new Dictionary<string, DateTimeOffset>(
                            StringComparer.OrdinalIgnoreCase);
                    }

                    settings.LastLaunched[key] = launchedAt;
                }, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 只影响世界库的展示；不要把一次成功的启动改写成失败。
            }
        }

        /// <summary>
        /// 从已探测到的 Java 里挑一个：优先高版本（新版本 MC 需要更高的 Java）。
        /// 版本号未知的排在最后——宁可不用来路不明的那个。
        /// </summary>
        internal static JavaRuntime? PickJava(IReadOnlyList<JavaRuntime> javas)
        {
            if (javas == null || javas.Count == 0)
            {
                return null;
            }

            JavaRuntime? best = null;

            foreach (JavaRuntime java in javas)
            {
                if (best == null || java.Major > best.Major)
                {
                    best = java;
                }
            }

            return best;
        }

        /// <summary>修复选中实例：检查客户端 JAR 与库，缺哪个从镜像补哪个。</summary>
        [RelayCommand]
        public async Task RepairSelectedAsync()
        {
            if (_repairer == null)
            {
                StatusText = "修复功能不可用。";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本";
                return;
            }

            if (IsLaunching || IsBusy)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在检查文件完整性…";

            try
            {
                RepairReport inspect = await _repairer
                    .InspectAsync(CurrentGameDirectory, SelectedVersion.FolderName, CancellationToken.None)
                    .ConfigureAwait(true);

                if (inspect.Missing == 0)
                {
                    StatusText = "文件完整，无需修复。";
                    return;
                }

                StatusText = "缺失 " + inspect.Missing + " 个文件，开始修复…";

                RepairReport result = await _repairer
                    .RepairAsync(
                        CurrentGameDirectory,
                        SelectedVersion.FolderName,
                        new Progress<InstallProgress>(value => StatusText = value.Stage),
                        CancellationToken.None)
                    .ConfigureAwait(true);

                StatusText = result.Summary
                    + (result.Details.Count > 0 ? "（" + string.Join("；", result.Details.Take(3)) + "）" : string.Empty);

                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText = "修复失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
        /// <summary>重命名当前实例（目录 + JSON 里的 id）。</summary>
        [RelayCommand]
        public async Task RenameVersionAsync()
        {
            if (_versionManager == null || _dialogs == null)
            {
                StatusText = "实例管理不可用。";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本";
                return;
            }

            string current = SelectedVersion.FolderName;
            string? name = _dialogs.Prompt("重命名实例", "新名字（不能与现有实例重名）：", current);

            if (string.IsNullOrWhiteSpace(name) || string.Equals(name, current, StringComparison.Ordinal))
            {
                return;
            }

            if (!_versionManager.IsNameAvailable(CurrentGameDirectory, name!, current))
            {
                StatusText = "名字不可用：已有同名实例，或包含非法字符。";
                return;
            }

            try
            {
                await _versionManager.RenameAsync(CurrentGameDirectory, current, name!, CancellationToken.None)
                    .ConfigureAwait(true);

                StatusText = "已重命名为 " + name;
                await RefreshAsync().ConfigureAwait(true);
                SelectVersionByName(name!);
            }
            catch (Exception ex)
            {
                StatusText = "重命名失败：" + ex.Message;
            }
        }

        /// <summary>复制当前实例（只复制目录内容，不动共享的 libraries/assets）。</summary>
        [RelayCommand]
        public async Task CopyVersionAsync()
        {
            if (_versionManager == null || _dialogs == null)
            {
                StatusText = "实例管理不可用。";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本";
                return;
            }

            string current = SelectedVersion.FolderName;
            string suggestion = current + "-副本";
            string? name = _dialogs.Prompt("复制实例", "新实例名：", suggestion);

            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            if (!_versionManager.IsNameAvailable(CurrentGameDirectory, name!, current))
            {
                StatusText = "名字不可用：已有同名实例，或包含非法字符。";
                return;
            }

            StatusText = "正在复制实例（大实例可能要一会儿）…";

            try
            {
                string created = await _versionManager
                    .CopyAsync(CurrentGameDirectory, current, name!, CancellationToken.None)
                    .ConfigureAwait(true);

                StatusText = "已复制为 " + created;
                await RefreshAsync().ConfigureAwait(true);
                SelectVersionByName(created);
            }
            catch (Exception ex)
            {
                StatusText = "复制失败：" + ex.Message;
            }
        }

        /// <summary>删除当前实例（不可恢复，带确认）。</summary>
        [RelayCommand]
        public async Task DeleteVersionAsync()
        {
            if (_versionManager == null)
            {
                StatusText = "实例管理不可用。";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本";
                return;
            }

            string folder = SelectedVersion.FolderName;

            if (_dialogs != null && !_dialogs.Confirm(
                    "删除实例",
                    "确定要删除「" + folder + "」吗？\n\n整个实例目录（含存档、模组、配置）都会被删除，无法恢复。"))
            {
                return;
            }

            try
            {
                await _versionManager.DeleteAsync(CurrentGameDirectory, folder, CancellationToken.None)
                    .ConfigureAwait(true);

                StatusText = "已删除 " + folder;
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText = "删除失败：" + ex.Message;
            }
        }

        /// <summary>刷新世界页“实例内容”数量；目录不存在时显示 0。</summary>
        [RelayCommand]
        public void RefreshInstanceContent()
        {
            if (_instanceContent == null || string.IsNullOrWhiteSpace(CurrentGameDirectory))
            {
                SaveSlotCount = 0;
                ResourcePackCount = 0;
                ShaderPackCount = 0;
                return;
            }

            try
            {
                SaveSlotCount = _instanceContent.Count(CurrentGameDirectory, InstanceContentKind.Saves);
                ResourcePackCount = _instanceContent.Count(CurrentGameDirectory, InstanceContentKind.ResourcePacks);
                ShaderPackCount = _instanceContent.Count(CurrentGameDirectory, InstanceContentKind.ShaderPacks);
            }
            catch (Exception)
            {
                SaveSlotCount = 0;
                ResourcePackCount = 0;
                ShaderPackCount = 0;
            }
        }

        [RelayCommand]
        public Task ImportSaveAsync() => ImportInstanceContentAsync(
            InstanceContentKind.Saves,
            "导入存档",
            "存档压缩包 (*.zip)|*.zip");

        [RelayCommand]
        public Task ImportResourcePackAsync() => ImportInstanceContentAsync(
            InstanceContentKind.ResourcePacks,
            "导入资源包",
            "资源包 (*.zip)|*.zip");

        [RelayCommand]
        public Task ImportShaderPackAsync() => ImportInstanceContentAsync(
            InstanceContentKind.ShaderPacks,
            "导入光影包",
            "光影包 (*.zip)|*.zip");

        private async Task ImportInstanceContentAsync(
            InstanceContentKind kind,
            string title,
            string filter)
        {
            if (_instanceContent == null || _filePicker == null)
            {
                StatusText = "实例内容导入不可用。";
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentGameDirectory))
            {
                StatusText = "请先选择游戏目录。";
                return;
            }

            string? path = _filePicker.PickFile(title, filter);

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                StatusText = "正在导入" + title.Substring(2) + "…";
                string imported = await _instanceContent
                    .ImportAsync(CurrentGameDirectory, kind, path!, CancellationToken.None)
                    .ConfigureAwait(true);
                RefreshInstanceContent();
                StatusText = "已导入：" + Path.GetFileName(imported);
            }
            catch (Exception ex)
            {
                StatusText = "导入失败：" + ex.Message;
            }
        }

        /// <summary>把当前实例打包为可分享的 zip（不含 logs/crash-reports）。</summary>
        [RelayCommand]
        public async Task ExportInstanceAsync()
        {
            if (_instancePackages == null || _saveFilePicker == null)
            {
                StatusText = "实例导出不可用。";
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本";
                return;
            }

            string suggested = SelectedVersion.FolderName + ".zip";
            string? path = _saveFilePicker.PickFile(
                "导出实例",
                "NLMCL 实例包 (*.zip)|*.zip|所有文件 (*.*)|*.*",
                suggested);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                StatusText = "正在导出实例（大实例可能要一会儿）…";
                string exported = await _instancePackages
                    .ExportAsync(SelectedVersion.InstancePath, path!, CancellationToken.None)
                    .ConfigureAwait(true);
                StatusText = "已导出实例：" + exported;
            }
            catch (Exception ex)
            {
                StatusText = "导出失败：" + ex.Message;
            }
        }

        /// <summary>从 zip 导入一个实例包，目录重名时自动加后缀。</summary>
        [RelayCommand]
        public async Task ImportInstanceAsync()
        {
            if (_instancePackages == null || _filePicker == null)
            {
                StatusText = "实例导入不可用。";
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentGameDirectory))
            {
                StatusText = "请先选择游戏目录。";
                return;
            }

            string? path = _filePicker.PickFile("导入实例包", "NLMCL 实例包 (*.zip)|*.zip|所有文件 (*.*)|*.*");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                StatusText = "正在导入实例包…";
                string versions = Path.Combine(CurrentGameDirectory, "versions");
                string imported = await _instancePackages
                    .ImportAsync(path!, versions, CancellationToken.None)
                    .ConfigureAwait(true);

                await RefreshAsync().ConfigureAwait(true);
                SelectVersionByName(Path.GetFileName(imported));
                StatusText = "已导入实例：" + Path.GetFileName(imported);
            }
            catch (Exception ex)
            {
                StatusText = "导入失败：" + ex.Message;
            }
        }

        private void SelectVersionByName(string folder)
        {            VersionItem? match = Versions.FirstOrDefault(
                item => string.Equals(item.FolderName, folder, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                SelectVersion(match);
            }
        }
        /// <summary>手动重新扫描世界库（点顶部「重新扫描」徽标触发）。</summary>
        [RelayCommand]
        private async Task RescanAsync()
        {
            StatusText = "正在重新扫描世界库…";

            try
            {
                await RefreshAsync().ConfigureAwait(true);
                StatusText = "扫描完成：共 " + Versions.Count + " 个版本";
            }
            catch (Exception ex)
            {
                StatusText = "扫描失败：" + ex.Message;
            }
        }
        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在读取世界库…";

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                EnsureCurrentDirectoryInList();

                List<VersionItem> discovered = new List<VersionItem>();
                List<string> ids = new List<string>();
                int scanned = 0;

                foreach (string directory in GameDirectories.ToList())
                {
                    scanned++;

                    try
                    {
                        IReadOnlyList<MinecraftVersion> versions = await _catalog
                            .GetInstalledAsync(directory, _scanCts.Token)
                            .ConfigureAwait(true);

                        foreach (MinecraftVersion version in versions)
                        {
                            string instancePath = Path.GetFullPath(
                                Path.Combine(directory, "versions", version.FolderName));

                            _lastLaunched.TryGetValue(instancePath, out DateTimeOffset launchedAt);

                            discovered.Add(new VersionItem(
                                version, !version.HasClientJar, directory,
                                launchedAt == default ? (DateTimeOffset?)null : launchedAt));
                            ids.Add(version.Id);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        StatusText = "目录扫描失败（" + directory + "）：" + ex.Message;
                    }
                }

                Versions.Clear();

                foreach (VersionItem item in discovered)
                {
                    item.ShowDirectory = scanned > 1;
                    Versions.Add(item);
                }

                // 下载页的"已安装"标记跟着本地列表走（装完会自动重扫，这里顺带刷新标记）
                Download.MarkInstalled(ids.Distinct(StringComparer.OrdinalIgnoreCase));

                UpdateSummary();

                // 扫描完自动选中第一个版本：
                // 否则用户点「启动游戏」会被"请先选一个版本"挡住，而那句提示在最底部，很容易漏看。
                if (Versions.Count > 0)
                {
                    VersionItem? previous = SelectedVersion == null
                        ? null
                        : Versions.FirstOrDefault(item =>
                            string.Equals(item.FolderName, SelectedVersion.FolderName, StringComparison.Ordinal)
                            && string.Equals(item.GameDirectory, SelectedVersion.GameDirectory, StringComparison.OrdinalIgnoreCase));

                    // 刷新会重建 VersionItem；必须重新指向列表里的新对象，否则列表没有高亮。
                    SelectVersion(previous ?? Versions[0]);
                }
                else
                {
                    // 删除最后一个实例或目录被清空后，不能继续指向已不存在的版本，
                    // 也不能让“启动设置 / 当前实例”继续展开着上一个实例的残留值。
                    SelectedVersion = null;
                    HasSelection = false;
                    _ = RefreshLaunchPreflightAsync();
                }

                // 扫描成功说明目录列表可用，记下来下次直接用
                await SaveGameDirectoryAsync().ConfigureAwait(true);

                IsEmpty = Versions.Count == 0;
                string directorySuffix = scanned > 1 ? " · 扫描 " + scanned + " 个目录" : string.Empty;
                StatusText = IsEmpty
                    ? "未发现已安装版本" + directorySuffix
                    : "共 " + Versions.Count + " 个版本" + directorySuffix;
            }
            catch (OperationCanceledException)
            {
                StatusText = "扫描已取消";
            }
            catch (Exception ex)
            {
                StatusText = "扫描失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }

            RefreshJoinChecks();
        }

        private void UpdateSummary()
        {
            int release = 0;
            int snapshot = 0;
            int other = 0;

            foreach (VersionItem item in Versions)
            {
                switch (item.Kind)
                {
                    case VersionKind.Release:
                        release++;
                        break;
                    case VersionKind.Snapshot:
                        snapshot++;
                        break;
                    default:
                        other++;
                        break;
                }
            }

            VersionCountLabel = Versions.Count + " 个版本";
            ReleaseCountLabel = "正式版 " + release;
            SnapshotCountLabel = "快照 " + snapshot;

            OtherCountLabel = "其他 " + other;
            HasRelease = release > 0;
            HasSnapshot = snapshot > 0;
            HasOther = other > 0;
        }
    }
}
