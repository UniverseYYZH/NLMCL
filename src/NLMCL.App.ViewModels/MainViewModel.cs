using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;
using NLMCL.Core.Accounts;
using NLMCL.Core.Installing;
using NLMCL.Core.Networking;
using NLMCL.Core.Settings;
using System.Diagnostics;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 主窗口的展示逻辑。刻意不引用任何 WPF 类型，因此可以在没有 UI 线程的情况下单测。
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject
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
        private readonly IAppSettingsStore _appSettings;

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
            ResourceViewModel? resource = null)
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
            _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
            _javaInstaller = javaInstaller;
            _sessionLauncher = sessionLauncher;
            _externalAuthenticator = externalAuthenticator;
            _authlibInjector = authlibInjector;
            _snapshotService = snapshotService;
            _dialogs = dialogs;
            _appearance = appearance;
            _downloadTuning = downloadTuning;
            _resource = resource;
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            Download = download ?? throw new ArgumentNullException(nameof(download));


            Console = console ?? throw new ArgumentNullException(nameof(console));

            CurrentGameDirectory = paths.DefaultMinecraftDirectory;

            NavigationItem versions = new NavigationItem("本地版本", "扫描本地 .minecraft", ShellSection.Versions);
            NavigationItem consoleNav = new NavigationItem("启动与日志", "多实例控制台", ShellSection.Launch);
            NavigationItem multiplayer = new NavigationItem("联机房间", "局域网与直连", ShellSection.Multiplayer);
            NavigationItem snapshot = new NavigationItem("实例快照", "备份存档与配置", ShellSection.Snapshot);
            NavigationItem lanScan = new NavigationItem("局域网扫描", "发现附近的房间", ShellSection.LanScan);
            NavigationItem settings = new NavigationItem("设置", "外观 / Java / 下载", ShellSection.Settings);
            NavigationItem downloadNav = new NavigationItem("版本下载", "原版与加载器", ShellSection.Download);
            NavigationItem import = new NavigationItem("导入资源", "mod 与整合包", ShellSection.Import);

            NavigationGroups = new ReadOnlyCollection<NavigationGroup>(new List<NavigationGroup>
            {
                new NavigationGroup("工作区", new[] { versions, consoleNav, multiplayer, snapshot, lanScan, settings }),
                new NavigationGroup("工具", new[] { downloadNav, import })
            });

            SelectSection(versions);

            // 装完一个版本后自动重扫本地列表，省掉"回版本页再点扫描"这一步。
            Download.InstallCompleted += OnDownloadInstallCompleted;
        }

        /// <summary>下载页的展示逻辑（由组合根注入）。</summary>
        public DownloadViewModel Download { get; }



        /// <summary>启动控制台的展示逻辑。</summary>
        public LaunchConsoleViewModel Console { get; }

        /// <summary>
        /// 游戏目录是"一个"状态：本地版本、启动控制台、下载页必须看到同一个。
        ///
        /// 之前三个页面各自记一份：本地版本扫的是 D 盘，控制台却去 %APPDATA%\.minecraft 找版本、
        /// 下载页也往默认目录装——表现就是"启动失败：未找到版本目录"和"装完列表里没有"。
        /// </summary>
        partial void OnCurrentGameDirectoryChanged(string value)
        {
            Console.GameDirectory = value;
            Download.TargetDirectory = value;
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

        public string StageLabel => "v1.0 · 全功能";

        public string StageHint => "版本 · 下载 · 启动 · 联机 · 快照 · 导入";

        public IReadOnlyList<NavigationGroup> NavigationGroups { get; }

        public ObservableCollection<VersionItem> Versions { get; } = new ObservableCollection<VersionItem>();

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

        /// <summary>展开/收起账号弹层。</summary>
        [RelayCommand]
        public void ToggleAccountPopup()
        {
            IsAccountPopupOpen = !IsAccountPopupOpen;
        }

        /// <summary>是否正在启动（防止重复点击）。</summary>
        [ObservableProperty]
        private bool _isLaunching;

        [ObservableProperty]
        private string _currentGameDirectory = string.Empty;

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

        /// <summary>当前显示「本地版本」页。</summary>
        [ObservableProperty]
        private bool _isVersionsSection = true;

        /// <summary>当前显示「版本下载」页。</summary>
        [ObservableProperty]
        private bool _isDownloadSection;


        /// <summary>当前显示「联机房间」页。</summary>
        [ObservableProperty]
        private bool _isMultiplayerSection;

        /// <summary>在线资源页的视图模型（可能为 null，例如单测未注入）。</summary>
        public ResourceViewModel? Resource => _resource;

        /// <summary>当前显示「导入资源」页。</summary>
        [ObservableProperty]
        private bool _isImportSection;

        /// <summary>当前显示「设置」页。</summary>
        [ObservableProperty]
        private bool _isSettingsSection;

        // ── 设置页的选项（界面用字符串，存盘时换算）─────────────────────────
        public IReadOnlyList<string> ThemeOptions { get; } = new[] { "浅色", "深色", "跟随系统" };

        [ObservableProperty]
        private string _selectedTheme = "浅色";

        /// <summary>普通窗口的缩放（百分比，滑块 80–150）。</summary>
        [ObservableProperty]
        private double _uiScalePercent = 100;

        /// <summary>最大化时的缩放（百分比，滑块 100–200）。</summary>
        [ObservableProperty]
        private double _uiScaleMaximizedPercent = 150;

        public string UiScaleText => (int)Math.Round(UiScalePercent) + "%";

        public string UiScaleMaximizedText => (int)Math.Round(UiScaleMaximizedPercent) + "%";

        public IReadOnlyList<string> ThreadOptions { get; } = new[] { "2", "4", "6", "8", "16" };

        [ObservableProperty]
        private string _selectedThreads = "6";

        partial void OnSelectedThreadsChanged(string value)
        {
            if (!_settingsLoaded)
            {
                return;
            }

            if (int.TryParse(value, out int threads))
            {
                _downloadTuning?.ApplyThreadCount(threads);
                Download.Parallelism = threads;
            }

            _ = SaveAppearanceAsync();
        }

        partial void OnSelectedThemeChanged(string value)
        {
            if (!_settingsLoaded)
            {
                return;
            }

            _appearance?.ApplyTheme(ThemeFromText(value));
            _ = SaveAppearanceAsync();
        }

        partial void OnUiScalePercentChanged(double value)
        {
            OnPropertyChanged(nameof(UiScaleText));

            if (!_settingsLoaded || _appearance?.IsMaximized == true)
            {
                return;
            }

            ScheduleScaleApply(ref _scaleDebounce);
        }

        partial void OnUiScaleMaximizedPercentChanged(double value)
        {
            OnPropertyChanged(nameof(UiScaleMaximizedText));

            if (!_settingsLoaded || _appearance?.IsMaximized != true)
            {
                return;
            }

            ScheduleScaleApply(ref _scaleMaximizedDebounce);
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

            return string.Equals(text, "跟随系统", StringComparison.Ordinal) ? "system" : "light";
        }

        private static string ThemeToText(string? theme)
        {
            if (string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                return "深色";
            }

            return string.Equals(theme, "system", StringComparison.OrdinalIgnoreCase) ? "跟随系统" : "浅色";
        }

        private static double ClampPercent(double percent)
            => Math.Max(80, Math.Min(150, Math.Round(percent)));

        private static double ClampMaximizedPercent(double percent)
            => Math.Max(100, Math.Min(200, Math.Round(percent)));

        private async Task SaveAppearanceAsync()
        {
            try
            {
                AppSettings settings = await _appSettings.LoadAsync(CancellationToken.None).ConfigureAwait(true);

                settings.Theme = ThemeFromText(SelectedTheme);
                settings.UiScale = ClampPercent(UiScalePercent) / 100.0;
                settings.UiScaleMaximized = ClampMaximizedPercent(UiScaleMaximizedPercent) / 100.0;
                settings.DownloadThreads = int.TryParse(SelectedThreads, out int threads) ? threads : 6;

                await _appSettings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);

                StatusText = "设置已保存";
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

            foreach (NavigationGroup group in NavigationGroups)
            {
                foreach (NavigationItem candidate in group.Items)
                {
                    candidate.IsActive = ReferenceEquals(candidate, item);
                }
            }

            SelectedNavigation = item;

            IsVersionsSection = item.Section == ShellSection.Versions;
            IsDownloadSection = item.Section == ShellSection.Download;

            IsLaunchSection = item.Section == ShellSection.Launch;
            IsMultiplayerSection = item.Section == ShellSection.Multiplayer;
            IsSettingsSection = item.Section == ShellSection.Settings;
            IsImportSection = item.Section == ShellSection.Import;

            if (IsImportSection && _resource != null)
            {
                _resource.SyncFromInstance(CurrentGameDirectory, SelectedVersion?.FolderName ?? string.Empty);
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

            if (SelectedVersion == null)
            {
                StatusText = "请先在「本地版本」里选一个版本";
                return;
            }

            if (!LanAddress.TryParse(ServerAddress, out string host, out int port, out string? error))
            {
                StatusText = error ?? "联机地址格式不对";
                return;
            }

            StatusText = "正在探测 " + host + ":" + port + " …";

            bool open = await _network.IsPortOpenAsync(host, port, CancellationToken.None).ConfigureAwait(true);
            if (!open)
            {
                StatusText = "连不上 " + host + ":" + port +
                             " —— 对方可能没开房、端口填错，或不在同一网段（校园网跨 VLAN 常被拦）";
                return;
            }

            await LaunchCoreAsync(host + ":" + port).ConfigureAwait(true);
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

                MemoryMb = settings.MemoryMb;
                JavaPath = settings.JavaPath ?? string.Empty;
                ExtraArguments = settings.ExtraArguments ?? string.Empty;
            }
            catch (Exception ex)
            {
                StatusText = "读取实例设置失败：" + ex.Message;
            }
        }

        /// <summary>把界面上的设置写回实例目录。</summary>
        [RelayCommand]
        public async Task SaveSettingsAsync()
        {
            if (SelectedVersion == null)
            {
                StatusText = "请先选择一个版本";
                return;
            }

            try
            {
                await _instanceSettings.SaveAsync(
                    CurrentGameDirectory,
                    SelectedVersion.FolderName,
                    new InstanceSettings
                    {
                        MemoryMb = MemoryMb,
                        JavaPath = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath,
                        ExtraArguments = string.IsNullOrWhiteSpace(ExtraArguments) ? null : ExtraArguments
                    },
                    CancellationToken.None).ConfigureAwait(true);

                StatusText = "已保存 " + SelectedVersion.Name + " 的启动设置";
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
                AppSettings settings = await _appSettings.LoadAsync(CancellationToken.None).ConfigureAwait(true);
                settings.MicrosoftClientId = clientId;
                await _appSettings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
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

                Account = account;
                Console.Account = account;
                StatusText = "已登录微软账号：" + account.Name;
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

                if (!string.IsNullOrWhiteSpace(settings.GameDirectory) &&
                    System.IO.Directory.Exists(settings.GameDirectory))
                {
                    CurrentGameDirectory = settings.GameDirectory!;
                }

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
                SelectedTheme = ThemeToText(settings.Theme);
                UiScalePercent = ClampPercent(settings.UiScale <= 0 ? 100 : settings.UiScale * 100);
                UiScaleMaximizedPercent = ClampMaximizedPercent(
                    settings.UiScaleMaximized <= 0 ? 150 : settings.UiScaleMaximized * 100);
                SelectedThreads = (settings.DownloadThreads <= 0 ? 6 : settings.DownloadThreads).ToString();

                _downloadTuning?.ApplyThreadCount(settings.DownloadThreads <= 0 ? 6 : settings.DownloadThreads);
                Download.Parallelism = settings.DownloadThreads <= 0 ? 6 : settings.DownloadThreads;

                _appearance?.ApplyTheme(ThemeFromText(SelectedTheme));

                // 窗口最大化/还原时按对应档位重算（只订阅一次）
                if (_appearance != null && !_windowStateHooked)
                {
                    _windowStateHooked = true;
                    _appearance.WindowStateChanged += (_, __) => ApplyScaleForCurrentWindowState();
                }

                ApplyScaleForCurrentWindowState();

                _settingsLoaded = true;
            }
            catch (Exception ex)
            {
                StatusText = "读取设置失败：" + ex.Message;
            }

            await LoadAccountAsync().ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }

        /// <summary>记住当前游戏目录。</summary>
        private async Task SaveGameDirectoryAsync()
        {
            try
            {
                await _appSettings.SaveAsync(
                    new AppSettings { GameDirectory = CurrentGameDirectory },
                    CancellationToken.None).ConfigureAwait(true);
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

                Account = account;
                Console.Account = account;
                StatusText = "已登录离线账号：" + account.Name;
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
            await _accounts.ClearAsync(CancellationToken.None).ConfigureAwait(true);
            Account = null;
            Console.Account = null;
            StatusText = "已退出账号";
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
            if (item == null)
            {
                return;
            }

            foreach (VersionItem candidate in Versions)
            {
                candidate.IsSelected = ReferenceEquals(candidate, item);
            }

            SelectedVersion = item;
            HasSelection = true;
            StatusText = "已选择 " + item.Name;

            // 切版本时把该实例的设置带出来
            _ = LoadInstanceSettingsAsync(item);
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
        private async Task LaunchCoreAsync(string? serverAddress)
        {
            if (IsLaunching)
            {
                return;
            }

            if (SelectedVersion == null)
            {
                StatusText = "请先在列表里点选一个版本";
                return;
            }

            IsLaunching = true;
            StatusText = "正在查找 Java…";

            try
            {
                IReadOnlyList<JavaRuntime> javas = await _javaLocator
                    .FindInstalledAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                // 指定了 Java 就用它；否则按「该版本要求的 Java」挑，而不是无脑挑最高。
                string? javaExecutable;
                Version? javaVersion = null;
                if (!string.IsNullOrWhiteSpace(JavaPath))
                {
                    javaExecutable = JavaPath;

                    // 手填路径时没有"探测"环节，版本号会留空 → PCL 按默认 Java 17 生成
                    // JVM 参数（给 Java 8 加 -XX:+UseZGC 之类的）→ JVM 拒绝启动，窗口一闪而过。
                    // 从 JDK 自带的 release 文件里读真实版本补上。
                    javaVersion = JavaVersionProbe.TryRead(javaExecutable);
                }
                else
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
                            : "没有找到可用的 Java，请先安装 JDK 17 或更高版本";
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
                    StatusText = "没有找到可用的 Java，请先安装 JDK 17 或更高版本";
                    return;
                }

                StatusText = "正在启动 " + SelectedVersion.Name + "（内存 " + MemoryMb + " MB）…";

                if (Console.IsRunning)
                {
                    StatusText = "游戏已经在运行，日志见「启动与日志」";
                    return;
                }

                LaunchRequest request = new LaunchRequest(CurrentGameDirectory, SelectedVersion.FolderName)
                {
                    JavaExecutable = javaExecutable,
                    JavaVersion = javaVersion,
                    MaximumMemoryMb = MemoryMb,
                    ServerAddress = serverAddress
                }.WithAccount(Account ?? BuildImplicitOfflineAccount());

                // 外置登录：把 authlib-injector 挂到 JVM 上。
                // 失败就明确报错，不要带着空身份启动（服务器会直接踢人，日志还看不出原因）。
                try
                {
                    request.ExtraJvmArguments = await ExternalLaunch
                        .BuildJvmArgumentsAsync(Account, _authlibInjector, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    StatusText = "外置登录准备失败：" + ex.Message;
                    return;
                }

                if (_sessionLauncher != null)
                {
                    // 统一走会话式启动：捕获输出并交给控制台，这样从「本地版本」启动
                    // 也能在「启动与日志」里看到日志、也能停止（以前两条路径互不相干）。
                    IGameSession session = await _sessionLauncher
                        .StartAsync(request, CancellationToken.None)
                        .ConfigureAwait(true);

                    Console.Attach(session);

                    StatusText = "已启动 " + SelectedVersion.Name
                        + "（PID " + session.ProcessId + "），日志见「启动与日志」";
                    return;
                }

                LaunchResult result = await _launcher
                    .LaunchAsync(request, CancellationToken.None)
                    .ConfigureAwait(true);

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

        /// <summary>扫描本地版本。UI 只通过 <see cref="RefreshCommand"/> 触发。</summary>
        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在读取本地版本…";

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                IReadOnlyList<MinecraftVersion> versions =
                    await _catalog.GetInstalledAsync(CurrentGameDirectory, _scanCts.Token).ConfigureAwait(true);

                Versions.Clear();
                foreach (MinecraftVersion version in versions)
                {
                    Versions.Add(new VersionItem(version, !version.HasClientJar));
                }

                // 下载页的"已安装"标记跟着本地列表走（装完会自动重扫，这里顺带刷新标记）
                Download.MarkInstalled(versions.Select(version => version.Id));

                UpdateSummary();

                // 扫描完自动选中第一个版本：
                // 否则用户点「启动游戏」会被"请先选一个版本"挡住，而那句提示在最底部，很容易漏看。
                if (Versions.Count > 0)
                {
                    bool previousStillExists = SelectedVersion != null &&
                        Versions.Any(item => item.FolderName == SelectedVersion.FolderName);

                    if (!previousStillExists)
                    {
                        SelectVersion(Versions[0]);
                    }
                }

                // 扫描成功说明这个目录可用，记下来下次直接用
                await SaveGameDirectoryAsync().ConfigureAwait(true);

                IsEmpty = Versions.Count == 0;
                StatusText = IsEmpty
                    ? "未发现已安装版本"
                    : "共 " + Versions.Count + " 个版本";
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
