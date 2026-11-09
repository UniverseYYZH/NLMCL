using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 设置页的 ViewModel 边界。
    /// 当前阶段先把设置页从主窗口的 DataContext 中独立出来；
    /// 状态仍由 MainViewModel 持有，后续再把状态与命令迁入本类。
    /// </summary>
    public sealed partial class SettingsViewModel : ObservableObject
    {
        private readonly MainViewModel _host;
        private readonly IJavaLocator? _javaLocator;
        private readonly IFilePicker? _filePicker;
        private readonly IAppPaths? _paths;
        private readonly CacheCleanupViewModel? _cache;
        private readonly SettingsTransferViewModel? _transfer;
        private readonly ISettingsCommandHost? _commands;

        public SettingsViewModel(
            MainViewModel host,
            IJavaLocator? javaLocator = null,
            IFilePicker? filePicker = null,
            IAppPaths? paths = null,
            CacheCleanupViewModel? cache = null,
            SettingsTransferViewModel? transfer = null,
            ISettingsCommandHost? commands = null)
        {
            _host = host;
            _javaLocator = javaLocator;
            _filePicker = filePicker;
            _paths = paths;
            _cache = cache;
            _transfer = transfer;
            _commands = commands;
            _host.PropertyChanged += OnHostPropertyChanged;
        }

        public event EventHandler? SaveRequested;

        [ObservableProperty]
        private string _settingsSearchText = string.Empty;

        [ObservableProperty]
        private string _searchInput = string.Empty;

        private CancellationTokenSource? _searchDebounce;

        private string _activeSettingsGroup = string.Empty;

        private bool _syncingGroup;

        private static readonly (string Key, string Keywords)[] SettingsGroups =
        {
            ("general", "通用 启动 页面 关闭 窗口 托盘 默认页 询问"),
            ("game", "游戏 目录 Java 运行时 内存 启动 参数 JVM .minecraft 路径 新实例 预设 游戏参数"),
            ("appearance", "外观 主题 缩放 窗口 动效 高对比度"),
            ("download", "下载 线程 限速 镜像 网络 BMCLAPI"),
            ("logs", "日志 诊断 目录 诊断包"),
            ("data", "数据 备份 导入 导出 缓存 清理 重置 自动 快照 保留 启动前 恢复 默认")
        };

        public bool HasSettingsSearchText => !string.IsNullOrEmpty(SettingsSearchText);

        public bool HasSearchInputText => !string.IsNullOrEmpty(SearchInput);

        public bool HasNoSettingsSearchResults =>
            !string.IsNullOrWhiteSpace(SettingsSearchText)
            && !SettingsGroups.Any(group =>
                group.Keywords.IndexOf(SettingsSearchText.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);

        public bool IsAllGroupActive =>
            string.IsNullOrEmpty(SettingsSearchText) && _activeSettingsGroup.Length == 0;

        public bool IsGeneralGroupActive => _activeSettingsGroup == "general";

        public bool IsGameGroupActive => _activeSettingsGroup == "game";

        public bool IsAppearanceGroupActive => _activeSettingsGroup == "appearance";

        public bool IsDownloadGroupActive => _activeSettingsGroup == "download";

        public bool IsLogsGroupActive => _activeSettingsGroup == "logs";

        public bool IsDataGroupActive => _activeSettingsGroup == "data";

        partial void OnSettingsSearchTextChanged(string value)
        {
            OnPropertyChanged(nameof(HasSettingsSearchText));
            OnPropertyChanged(nameof(HasNoSettingsSearchResults));

            if (!_syncingGroup && !string.IsNullOrEmpty(value))
            {
                _activeSettingsGroup = string.Empty;
                NotifyGroupFlags();
            }
        }

        partial void OnSearchInputChanged(string value)
        {
            OnPropertyChanged(nameof(HasSearchInputText));

            if (_syncingGroup)
            {
                return;
            }

            if (!string.IsNullOrEmpty(value))
            {
                _activeSettingsGroup = string.Empty;
                NotifyGroupFlags();
            }

            _searchDebounce?.Cancel();
            CancellationTokenSource source = new CancellationTokenSource();
            _searchDebounce = source;
            _ = ApplySearchDebouncedAsync(value, source.Token);
        }

        private async Task ApplySearchDebouncedAsync(string value, CancellationToken token)
        {
            try
            {
                await Task.Delay(150, token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!token.IsCancellationRequested)
            {
                SettingsSearchText = value;
            }
        }

        [RelayCommand]
        private void FilterSettings(string? keywords)
        {
            string value = keywords ?? string.Empty;
            _searchDebounce?.Cancel();
            _syncingGroup = true;

            try
            {
                SearchInput = value;
                SettingsSearchText = value;
            }
            finally
            {
                _syncingGroup = false;
            }
        }

        [RelayCommand]
        private void SelectSettingsGroup(string? group)
        {
            string key = group ?? string.Empty;
            (string Key, string Keywords) match = SettingsGroups
                .FirstOrDefault(item => item.Key == key);

            _activeSettingsGroup = match.Key ?? string.Empty;
            _searchDebounce?.Cancel();
            _syncingGroup = true;

            try
            {
                string keywords = string.IsNullOrEmpty(_activeSettingsGroup)
                    ? string.Empty
                    : match.Keywords;
                SearchInput = keywords;
                SettingsSearchText = keywords;
            }
            finally
            {
                _syncingGroup = false;
            }

            NotifyGroupFlags();
        }

        [RelayCommand]
        private void ClearSettingsSearch()
        {
            _activeSettingsGroup = string.Empty;
            _searchDebounce?.Cancel();
            _syncingGroup = true;

            try
            {
                SearchInput = string.Empty;
                SettingsSearchText = string.Empty;
            }
            finally
            {
                _syncingGroup = false;
            }

            NotifyGroupFlags();
        }

        private void NotifyGroupFlags()
        {
            OnPropertyChanged(nameof(IsAllGroupActive));
            OnPropertyChanged(nameof(IsGeneralGroupActive));
            OnPropertyChanged(nameof(IsGameGroupActive));
            OnPropertyChanged(nameof(IsAppearanceGroupActive));
            OnPropertyChanged(nameof(IsDownloadGroupActive));
            OnPropertyChanged(nameof(IsLogsGroupActive));
            OnPropertyChanged(nameof(IsDataGroupActive));
        }

        private void OnHostPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.PropertyName))
            {
                OnPropertyChanged(e.PropertyName);
            }
        }

        public bool HasSettingsReadOnlyWarning => _host.HasSettingsReadOnlyWarning;

        public string SettingsReadOnlyWarning => _host.SettingsReadOnlyWarning;

        public string CurrentGameDirectory => _host.CurrentGameDirectory;

        public string GameDirectoryStatusText => _host.GameDirectoryStatusText;

        public string GameDirectoryDiskText => _host.GameDirectoryDiskText;

        public string EffectiveJavaText => SelectedJava == null
            || string.IsNullOrWhiteSpace(SelectedJava.Path)
                ? "自动（按版本要求）"
                : SelectedJava.Title;

        public string EffectiveJavaDetailText => SelectedJava == null
            || string.IsNullOrWhiteSpace(SelectedJava.Path)
                ? "自动：由启动器按每个实例需要的 Java 版本挑选"
                : "固定路径：" + SelectedJava.Path;

        public bool IsPortableMode => _paths?.IsPortable ?? false;

        public string DataRoot => _paths?.DataRoot ?? string.Empty;

        public string LogDirectory => _paths != null
            ? Path.Combine(_paths.DataRoot, "logs")
            : _host.LogDirectory;

        public string DownloadSourceText => UseMirror ? "镜像（元数据）+ 官方 CDN" : "官方源";

        public string DownloadRetryText => DownloadRetryPolicy.Describe();

        public ObservableCollection<JavaOption> JavaOptions { get; }
            = new ObservableCollection<JavaOption>();

        [ObservableProperty]
        private JavaOption? _selectedJava;

        [ObservableProperty]
        private string _javaStatus = "尚未探测";

        [ObservableProperty]
        private bool _isScanningJava;

        partial void OnSelectedJavaChanged(JavaOption? value)
        {
            DefaultJavaPath = value?.Path;
            OnPropertyChanged(nameof(EffectiveJavaText));
            OnPropertyChanged(nameof(EffectiveJavaDetailText));
            SyncJavaSelection();
        }

        private void SyncJavaSelection()
        {
            foreach (JavaOption option in JavaOptions)
            {
                option.IsSelected = ReferenceEquals(option, SelectedJava);
            }
        }

        public IReadOnlyList<string> ThemeOptions => _host.ThemeOptions;

        public IReadOnlyList<string> ThreadOptions => _host.ThreadOptions;

        public IReadOnlyList<string> SpeedLimitOptions => _host.SpeedLimitOptions;

        public IReadOnlyList<string> DefaultPageOptions => _host.DefaultPageOptions;

        public IReadOnlyList<string> CloseBehaviorOptions => _host.CloseBehaviorOptions;

        public IReadOnlyList<int> SnapshotRetentionOptions => _host.SnapshotRetentionOptions;

        public IReadOnlyList<string> MemoryPresets => _host.MemoryPresets;

        public CacheCleanupViewModel Cache => _cache ?? _host.Cache;

        public SettingsTransferViewModel Transfer => _transfer ?? _host.Transfer;

        [ObservableProperty]
        private string _selectedTheme = "浅色";

        [ObservableProperty]
        private double _uiScalePercent = 100;

        [ObservableProperty]
        private double _uiScaleMaximizedPercent = 150;

        public string UiScaleText => (int)System.Math.Round(UiScalePercent) + "%";

        public string UiScaleMaximizedText => (int)System.Math.Round(UiScaleMaximizedPercent) + "%";

        public event EventHandler? ThemeChanged;

        public event EventHandler? ScaleChanged;

        partial void OnSelectedThemeChanged(string value)
        {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnUiScalePercentChanged(double value)
        {
            OnPropertyChanged(nameof(UiScaleText));
            ScaleChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnUiScaleMaximizedPercentChanged(double value)
        {
            OnPropertyChanged(nameof(UiScaleMaximizedText));
            ScaleChanged?.Invoke(this, EventArgs.Empty);
        }

        [ObservableProperty]
        private string _selectedThreads = "6";

        public event EventHandler? ThreadsChanged;

        partial void OnSelectedThreadsChanged(string value)
        {
            ThreadsChanged?.Invoke(this, EventArgs.Empty);
        }

        [ObservableProperty]
        private string _selectedSpeedLimit = "不限速";

        public event EventHandler? SpeedLimitChanged;

        partial void OnSelectedSpeedLimitChanged(string value)
        {
            SpeedLimitChanged?.Invoke(this, EventArgs.Empty);
        }

        [ObservableProperty]
        private bool _useMirror = true;

        public event EventHandler? MirrorChanged;

        partial void OnUseMirrorChanged(bool value)
        {
            OnPropertyChanged(nameof(DownloadSourceText));
            MirrorChanged?.Invoke(this, EventArgs.Empty);
        }

        [ObservableProperty]
        private int _defaultMemoryMb = 2048;

        [ObservableProperty]
        private string _defaultJvmArgs = string.Empty;

        [ObservableProperty]
        private string _defaultGameArgs = string.Empty;

        public event EventHandler? MemoryChanged;

        public event EventHandler? LaunchArgsChanged;

        partial void OnDefaultMemoryMbChanged(int value)
        {
            MemoryChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnDefaultJvmArgsChanged(string value)
        {
            LaunchArgsChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnDefaultGameArgsChanged(string value)
        {
            LaunchArgsChanged?.Invoke(this, EventArgs.Empty);
        }

        [ObservableProperty]
        private string _selectedDefaultPage = "本地版本";

        [ObservableProperty]
        private bool _autoSnapshotBeforeLaunch;

        [ObservableProperty]
        private int _snapshotRetention = 5;

        [ObservableProperty]
        private string? _defaultJavaPath;

        [ObservableProperty]
        private string _selectedCloseBehavior = "退出";

        [ObservableProperty]
        private bool _reduceMotion;

        partial void OnReduceMotionChanged(bool value)
        {
            GeneralChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? DefaultJavaPathChanged;

        partial void OnDefaultJavaPathChanged(string? value)
        {
            DefaultJavaPathChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnSelectedCloseBehaviorChanged(string value)
        {
            GeneralChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? GeneralChanged;

        partial void OnSelectedDefaultPageChanged(string value)
        {
            GeneralChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnAutoSnapshotBeforeLaunchChanged(bool value)
        {
            GeneralChanged?.Invoke(this, EventArgs.Empty);
        }

        partial void OnSnapshotRetentionChanged(int value)
        {
            GeneralChanged?.Invoke(this, EventArgs.Empty);
        }

        public IRelayCommand UseMemoryPresetCommand => _host.UseMemoryPresetCommand;

        [RelayCommand]
        private Task PickGameDirectoryAsync()
        {
            return _commands?.PickGameDirectoryAsync() ?? Task.CompletedTask;
        }

        [RelayCommand]
        private void OpenGameDirectory()
        {
            _commands?.OpenGameDirectory();
        }

        [RelayCommand]
        private void OpenLogsFolder()
        {
            _commands?.OpenLogsFolder();
        }

        [RelayCommand]
        private void CreateDiagnosticBundle()
        {
            _commands?.CreateDiagnosticBundle();
        }

        [RelayCommand]
        private Task ResetAppearanceDefaultsAsync()
        {
            return _commands?.ResetAppearanceDefaultsAsync() ?? Task.CompletedTask;
        }

        [RelayCommand]
        private Task ResetDownloadDefaultsAsync()
        {
            return _commands?.ResetDownloadDefaultsAsync() ?? Task.CompletedTask;
        }

        [RelayCommand]
        private Task ResetLaunchDefaultsAsync()
        {
            return _commands?.ResetLaunchDefaultsAsync() ?? Task.CompletedTask;
        }

        [RelayCommand]
        private Task ResetAllDefaultsAsync()
        {
            return _commands?.ResetAllDefaultsAsync() ?? Task.CompletedTask;
        }

        [RelayCommand]
        public async Task AddJavaAsync()
        {
            if (_filePicker == null)
            {
                JavaStatus = "文件选择不可用。";
                return;
            }

            string? picked = _filePicker.PickFile(
                "选择 Java 可执行文件", "Java 可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*");

            if (string.IsNullOrWhiteSpace(picked))
            {
                return;
            }

            string path;

            try
            {
                path = Path.GetFullPath(picked);
            }
            catch (Exception ex)
            {
                JavaStatus = "Java 路径无效：" + ex.Message;
                return;
            }

            if (!File.Exists(path))
            {
                JavaStatus = "Java 文件不存在：" + path;
                return;
            }

            if (!JavaOptions.Any(option => option.IsAuto))
            {
                JavaOptions.Insert(0, new JavaOption(
                    null, "自动（按版本要求）", "由启动器按每个实例需要的 Java 版本挑选"));
            }

            JavaOption? option = JavaOptions.FirstOrDefault(item =>
                item.Path != null
                && string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));

            if (option == null)
            {
                option = new JavaOption(
                    path, "自定义 Java · " + Path.GetFileName(path), path);
                JavaOptions.Add(option);
            }

            SelectedJava = option;
            JavaStatus = "已添加自定义 Java：" + path;
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        public async Task RefreshJavaAsync()
        {
            if (IsScanningJava)
            {
                return;
            }

            if (_javaLocator == null)
            {
                JavaStatus = "Java 探测不可用。";
                return;
            }

            IsScanningJava = true;
            JavaStatus = "正在探测 Java…";

            try
            {
                IReadOnlyList<JavaRuntime> javas = await _javaLocator
                    .FindInstalledAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                JavaOptions.Clear();
                JavaOptions.Add(new JavaOption(
                    null, "自动（按版本要求）", "由启动器按每个实例需要的 Java 版本挑选"));

                foreach (JavaRuntime java in javas.OrderByDescending(item => item.Major))
                {
                    JavaOptions.Add(new JavaOption(
                        java.ExecutablePath,
                        "Java " + (java.Major > 0 ? java.Major.ToString() : "?") + " · " + java.Home,
                        java.Exists ? java.ExecutablePath : java.ExecutablePath + "（文件不存在）"));
                }

                if (!string.IsNullOrWhiteSpace(DefaultJavaPath)
                    && File.Exists(DefaultJavaPath)
                    && !JavaOptions.Any(option => option.Path != null
                        && string.Equals(option.Path, DefaultJavaPath, StringComparison.OrdinalIgnoreCase)))
                {
                    JavaOptions.Add(new JavaOption(
                        DefaultJavaPath,
                        "自定义 Java · " + Path.GetFileName(DefaultJavaPath),
                        DefaultJavaPath));
                }

                string? configured = DefaultJavaPath;
                JavaOption? match = JavaOptions.FirstOrDefault(option =>
                    option.Path != null
                    && string.Equals(option.Path, configured, StringComparison.OrdinalIgnoreCase));

                SelectedJava = match ?? JavaOptions[0];
                SyncJavaSelection();

                JavaStatus = javas.Count == 0
                    ? "没有探测到 Java；启动时会尝试自动下载 Mojang 运行时"
                    : "探测到 " + javas.Count + " 个 Java"
                      + (match == null ? "（当前为自动）" : "（默认：" + match.Title + "）");
            }
            catch (Exception ex)
            {
                JavaStatus = "探测失败：" + ex.Message;
            }
            finally
            {
                IsScanningJava = false;
            }
        }
    }
}

