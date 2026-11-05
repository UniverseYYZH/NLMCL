using System.IO;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 「Mod 管理」页：列出当前实例 mods 目录，支持启用/禁用（改后缀）、删除、检查更新。
    /// </summary>
    public sealed partial class ModsViewModel : ObservableObject
    {
        private readonly IModManager _manager;
        private readonly IModUpdateChecker? _updates;

        private string _modsDirectory = string.Empty;
        private string _gameVersion = string.Empty;
        private string _loader = string.Empty;

        public ModsViewModel(IModManager manager, IModUpdateChecker? updates = null)
        {
            _manager = manager ?? throw new ArgumentNullException(nameof(manager));
            _updates = updates;
        }

        public ObservableCollection<ModEntry> Mods { get; } = new ObservableCollection<ModEntry>();

        [ObservableProperty]
        private string _statusText = "先在「本地版本」选一个实例。";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _instanceText = "未选择实例";

        public bool HasMods => Mods.Count > 0;

        /// <summary>当前模组目录（界面上显示，方便用户确认文件放对了没）。</summary>
        [ObservableProperty]
        private string _modsDirectoryText = string.Empty;

        /// <summary>打开模组目录。</summary>
        [RelayCommand]
        private void OpenModsFolder()
        {
            if (string.IsNullOrWhiteSpace(_modsDirectory))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(_modsDirectory);
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(_modsDirectory) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusText = "打不开模组目录：" + ex.Message;
            }
        }

        /// <summary>主窗口在切换实例时调用。</summary>
        public void SetInstance(string modsDirectory, string instanceName, string gameVersion, string loader)
        {
            _modsDirectory = modsDirectory ?? string.Empty;
            ModsDirectoryText = _modsDirectory;
            _gameVersion = gameVersion ?? string.Empty;
            _loader = loader ?? string.Empty;
            InstanceText = instanceName ?? "未选择实例";

            _ = RefreshAsync();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (string.IsNullOrEmpty(_modsDirectory))
            {
                StatusText = "先在「本地版本」选一个实例。";
                return;
            }

            IsBusy = true;

            try
            {
                var mods = await _manager.ListAsync(_modsDirectory, CancellationToken.None).ConfigureAwait(true);

                Mods.Clear();

                foreach (ModEntry mod in mods)
                {
                    Mods.Add(mod);
                }

                OnPropertyChanged(nameof(HasMods));

            int missing = System.Linq.Enumerable.Count(mods, item => item.HasMissingDependencies);

            StatusText = mods.Count == 0
                ? "这个实例还没有模组。可以从「导入资源」安装。"
                : "共 " + mods.Count + " 个模组"
                  + (missing > 0 ? "，其中 " + missing + " 个缺前置（见红色提示）" : string.Empty);
            }
            catch (Exception ex)
            {
                StatusText = "读取模组目录失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ToggleAsync(ModEntry? entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                if (entry.Enabled)
                {
                    await _manager.DisableAsync(entry, CancellationToken.None).ConfigureAwait(true);
                    StatusText = "已禁用 " + entry.DisplayName;
                }
                else
                {
                    await _manager.EnableAsync(entry, CancellationToken.None).ConfigureAwait(true);
                    StatusText = "已启用 " + entry.DisplayName;
                }

                // 文件名变了，重建列表（顺序按启用优先）
                await RefreshAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText = "操作失败：" + ex.Message;
            }
        }

        [RelayCommand]
        public async Task DeleteAsync(ModEntry? entry)
        {
            if (entry == null)
            {
                return;
            }

            try
            {
                await _manager.DeleteAsync(entry, CancellationToken.None).ConfigureAwait(true);
                Mods.Remove(entry);
                OnPropertyChanged(nameof(HasMods));
                StatusText = "已删除 " + entry.DisplayName;
            }
            catch (Exception ex)
            {
                StatusText = "删除失败：" + ex.Message;
            }
        }

        [RelayCommand]
        public async Task CheckUpdatesAsync()
        {
            if (_updates == null)
            {
                StatusText = "更新检查不可用。";
                return;
            }

            if (Mods.Count == 0)
            {
                StatusText = "没有可检查的模组。";
                return;
            }

            StatusText = "正在向 Modrinth 查询更新…";

            int found = 0;

            foreach (ModEntry mod in Mods)
            {
                try
                {
                    await _updates
                        .CheckAsync(mod, _gameVersion, _loader, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (Exception)
                {
                    // 不在 Modrinth 上 / 网络失败：跳过这一条，不影响其它
                }

                if (mod.HasUpdate)
                {
                    found++;
                }
            }

            OnPropertyChanged(nameof(Mods));
            StatusText = found == 0 ? "没有发现新版本。" : "发现 " + found + " 个模组有新版本。";
        }
    }
}
