using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;
using NLMCL.Core.Accounts;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 首次启动引导：选游戏目录 → 检测 Java → 登录（可跳过）。
    ///
    /// 目标是把"新机器上第一次打开启动器要做什么"讲清楚。之前的痛点是：
    /// 用户不知道要选 .minecraft、不知道机器上没有 Java，点启动才看到一句技术错误。
    /// </summary>
    public sealed partial class WizardViewModel : ObservableObject
    {
        private readonly IAppPaths _paths;
        private readonly IAppSettingsStore _settings;
        private readonly IFolderPicker _picker;
        private readonly IJavaLocator _javaLocator;
        private readonly IAccountStore _accounts;

        public WizardViewModel(
            IAppPaths paths,
            IAppSettingsStore settings,
            IFolderPicker picker,
            IJavaLocator javaLocator,
            IAccountStore accounts)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _picker = picker ?? throw new ArgumentNullException(nameof(picker));
            _javaLocator = javaLocator ?? throw new ArgumentNullException(nameof(javaLocator));
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));

            GameDirectory = paths.DefaultMinecraftDirectory;
        }

        /// <summary>0=选目录 1=Java 2=登录。</summary>
        [ObservableProperty]
        private int _step;

        [ObservableProperty]
        private string _gameDirectory = string.Empty;

        [ObservableProperty]
        private string _playerName = string.Empty;

        [ObservableProperty]
        private string _statusText = "选择你的 Minecraft 游戏目录（.minecraft）。";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _javaFound;

        public ObservableCollection<string> Javas { get; } = new ObservableCollection<string>();

        public bool IsStep1 => Step == 0;

        public bool IsStep2 => Step == 1;

        public bool IsStep3 => Step == 2;

        public bool CanGoBack => Step > 0;

        public bool IsLastStep => Step == 2;

        /// <summary>完成或跳过时触发，窗口据此关闭。</summary>
        public event Action? Completed;

        partial void OnStepChanged(int value)
        {
            OnPropertyChanged(nameof(IsStep1));
            OnPropertyChanged(nameof(IsStep2));
            OnPropertyChanged(nameof(IsStep3));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(IsLastStep));
        }

        /// <summary>打开时先读一次已有设置，实现"重装后还能接着用"。不带 .minecraft 的用户也能改。</summary>
        public async Task InitializeAsync()
        {
            try
            {
                AppSettings existing = await _settings.LoadAsync(CancellationToken.None).ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(existing.GameDirectory))
                {
                    GameDirectory = existing.GameDirectory!;
                }
            }
            catch (Exception)
            {
                // 读不到就用默认路径
            }
        }

        [RelayCommand]
        public void PickDirectory()
        {
            string? picked = _picker.PickFolder("选择 .minecraft 目录", GameDirectory);

            if (!string.IsNullOrWhiteSpace(picked))
            {
                GameDirectory = picked!;
                StatusText = "已选择：" + picked;
            }
        }

        [RelayCommand]
        public async Task NextAsync()
        {
            if (Step == 0)
            {
                if (!ValidateDirectory(out string? error))
                {
                    StatusText = error!;
                    return;
                }

                Step = 1;
                StatusText = "检测本机 Java…";
                await DetectJavaAsync().ConfigureAwait(true);
                return;
            }

            if (Step == 1)
            {
                Step = 2;
                StatusText = "登录可选：现在不登也能进游戏（离线模式）。";
            }
        }

        [RelayCommand]
        public void Back()
        {
            if (Step > 0)
            {
                Step--;
                StatusText = "上一步";
            }
        }

        [RelayCommand]
        public async Task DetectJavaAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;

            try
            {
                var javas = await _javaLocator.FindInstalledAsync(CancellationToken.None).ConfigureAwait(true);

                Javas.Clear();

                foreach (JavaRuntime java in javas)
                {
                    Javas.Add("Java " + java.Major + " — " + java.ExecutablePath);
                }

                JavaFound = javas.Count > 0;

                StatusText = javas.Count == 0
                    ? "没有检测到 Java。启动游戏时会自动下载（需要联网），也可以在「设置」里手动指定。"
                    : "检测到 " + javas.Count + " 个 Java，启动时会自动选合适的。";
            }
            catch (Exception ex)
            {
                StatusText = "检测 Java 失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>完成：落盘设置（含完成标记），可选创建离线账号。</summary>
        [RelayCommand]
        public async Task FinishAsync()
        {
            if (!ValidateDirectory(out string? error))
            {
                StatusText = error!;
                Step = 0;
                return;
            }

            try
            {
                if (!string.IsNullOrWhiteSpace(PlayerName))
                {
                    Account offline = OfflineAccountFactory.Create(PlayerName.Trim());
                    await _accounts.SaveAsync(offline, CancellationToken.None).ConfigureAwait(true);
                }

                AppSettings settings = await _settings.LoadAsync(CancellationToken.None).ConfigureAwait(true);

                settings.GameDirectory = GameDirectory.Trim();
                settings.WizardCompleted = true;

                await _settings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);

                Completed?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = "保存失败：" + ex.Message;
            }
        }

        /// <summary>"先跳过"：也要标记完成，避免每次启动都弹。</summary>
        [RelayCommand]
        public async Task SkipAsync()
        {
            try
            {
                AppSettings settings = await _settings.LoadAsync(CancellationToken.None).ConfigureAwait(true);
                settings.WizardCompleted = true;
                await _settings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // 跳过失败也无所谓，下次还会弹
            }

            Completed?.Invoke();
        }

        private bool ValidateDirectory(out string? error)
        {
            error = null;
            string path = (GameDirectory ?? string.Empty).Trim();

            if (path.Length == 0)
            {
                error = "请先选择游戏目录。";
                return false;
            }

            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
            }
            catch (Exception)
            {
                error = "这个目录不可用（可能没有写入权限）：" + path;
                return false;
            }

            string versions = Path.Combine(path, "versions");

            if (!Directory.Exists(versions))
            {
                // 空目录是合法起点：之后可以用「下载中心」装原版
                StatusText = "注意：目录里还没有 versions，可以在「下载中心」里安装游戏。";
            }

            return true;
        }
    }
}
