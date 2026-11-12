using System.IO;
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

        /// <summary>当前实例里检测到的渲染器/优化 Mod。</summary>
        public ObservableCollection<RendererModInfo> RendererMods { get; } = new ObservableCollection<RendererModInfo>();

        [ObservableProperty]
        private string _rendererSummary = string.Empty;

        [ObservableProperty]
        private string _rendererWarning = string.Empty;

        public bool HasRendererMods => RendererMods.Count > 0;

        public bool HasRendererWarning => !string.IsNullOrWhiteSpace(RendererWarning);

        partial void OnRendererWarningChanged(string value)
            => OnPropertyChanged(nameof(HasRendererWarning));

        [ObservableProperty]
        private string _statusText = "先在「世界库」选一个实例。";

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
                StatusText = "先在「世界库」选一个实例。";
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
                RefreshRendererDiagnostics();

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
                RefreshRendererDiagnostics();
                StatusText = "已删除 " + entry.DisplayName;
            }
            catch (Exception ex)
            {
                StatusText = "删除失败：" + ex.Message;
            }
        }

        /// <summary>
        /// 重新扫描当前 Mods 列表里的渲染器/优化模组。只做识别与冲突提示，
        /// 不擅自禁用文件；是否保留由玩家决定。
        /// </summary>
        public void RefreshRendererDiagnostics()
        {
            RendererMods.Clear();

            foreach (ModEntry mod in Mods)
            {
                string? kind = DetectRenderer(mod);
                if (kind == null)
                {
                    continue;
                }

                RendererMods.Add(new RendererModInfo(
                    kind,
                    mod.DisplayName,
                    string.IsNullOrWhiteSpace(mod.Version) ? string.Empty : mod.Version.Trim(),
                    mod.Enabled,
                    mod.FileName));
            }

            OnPropertyChanged(nameof(HasRendererMods));

            RendererSummary = RendererMods.Count == 0
                ? "未检测到 Sodium / Embeddium / OptiFine / VulkanMod / Iris。"
                : "检测到：" + string.Join("、", RendererMods.Select(item => item.DisplayText));

            RendererWarning = BuildRendererWarning(Mods);
        }

        /// <summary>
        /// 启动前检查用：不依赖 Mod 管理页当前列表，直接扫描实例的 mods 目录。
        /// 读取失败不应阻止启动，由调用方决定如何处理空字符串。
        /// </summary>
        public Task<string> GetRendererWarningAsync(CancellationToken cancellationToken)
            => GetRendererWarningAsync(_modsDirectory, cancellationToken);

        /// <summary>指定 mods 目录的启动前检查，不修改 Mod 管理页当前状态。</summary>
        public async Task<string> GetRendererWarningAsync(string modsDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(modsDirectory))
            {
                return string.Empty;
            }

            IReadOnlyList<ModEntry> mods = await _manager
                .ListAsync(modsDirectory, cancellationToken)
                .ConfigureAwait(false);

            return BuildRendererWarning(mods);
        }

        /// <summary>
        /// 启动前预检：把缺失前置和渲染器冲突合成一次提示，避免连续弹多个框。
        /// 只读文件，不自动改 Mod。
        /// </summary>
        public async Task<IReadOnlyList<string>> GetLaunchWarningsAsync(
            string modsDirectory, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(modsDirectory))
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<ModEntry> mods = await _manager
                .ListAsync(modsDirectory, cancellationToken)
                .ConfigureAwait(false);

            List<string> warnings = new List<string>();

            List<ModEntry> missing = mods
                .Where(mod => mod.Enabled && mod.HasMissingDependencies)
                .Take(6)
                .ToList();

            if (missing.Count > 0)
            {
                warnings.Add("以下 Mod 缺少前置：" + string.Join("；", missing.Select(
                    mod => mod.DisplayName + "（缺 " + string.Join("、", mod.MissingDependencies) + "）")));
            }

            string renderer = BuildRendererWarning(mods);
            if (!string.IsNullOrWhiteSpace(renderer))
            {
                warnings.Add(renderer);
            }

            return warnings;
        }

        private static string BuildRendererWarning(IEnumerable<ModEntry> mods)
        {
            HashSet<string> enabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (ModEntry mod in mods)
            {
                if (!mod.Enabled)
                {
                    continue;
                }

                string? kind = DetectRenderer(mod);
                if (kind != null)
                {
                    enabled.Add(kind);
                }
            }

            bool sodium = enabled.Contains("Sodium");
            bool embeddium = enabled.Contains("Embeddium");
            bool optifine = enabled.Contains("OptiFine");
            bool vulkan = enabled.Contains("VulkanMod");
            bool iris = enabled.Contains("Iris");
            bool optifabric = enabled.Contains("OptiFabric");

            List<string> warnings = new List<string>();

            if (sodium && embeddium)
            {
                warnings.Add("Sodium 与 Embeddium 都是渲染器，通常只能保留一个。");
            }

            if (optifine && (sodium || embeddium || iris))
            {
                warnings.Add("OptiFine 与 Sodium / Embeddium / Iris 通常不兼容，请只保留一套。");
            }

            if (vulkan && (sodium || embeddium || optifine))
            {
                warnings.Add("VulkanMod 会替换渲染后端，不应再与 Sodium / Embeddium / OptiFine 同时启用。");
            }

            if (iris && !sodium)
            {
                warnings.Add("Iris 通常需要 Sodium 作为前置；当前未检测到已启用的 Sodium。");
            }

            if (optifabric && !optifine)
            {
                warnings.Add("OptiFabric 需要 OptiFine；当前未检测到已启用的 OptiFine。");
            }

            return warnings.Count == 0 ? string.Empty : string.Join(" ", warnings);
        }

        private static string? DetectRenderer(ModEntry mod)
        {
            string id = (mod.ModId ?? string.Empty).Trim().ToLowerInvariant();
            string file = (mod.FileName ?? string.Empty).ToLowerInvariant();

            if (id == "sodium" || file.Contains("sodium"))
            {
                return "Sodium";
            }

            if (id == "embeddium" || file.Contains("embeddium"))
            {
                return "Embeddium";
            }

            if (id == "optifine" || file.Contains("optifine"))
            {
                return "OptiFine";
            }

            if (id == "vulkanmod" || file.Contains("vulkanmod"))
            {
                return "VulkanMod";
            }

            if (id == "iris" || file.Contains("iris"))
            {
                return "Iris";
            }

            if (id == "optifabric" || file.Contains("optifabric"))
            {
                return "OptiFabric";
            }

            return null;
        }

        [ObservableProperty]
        private bool _isCheckingUpdates;

        [RelayCommand]
        public async Task CheckUpdatesAsync()
        {
            if (IsCheckingUpdates)
            {
                return;
            }

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

            IsCheckingUpdates = true;

            try
            {
            StatusText = "正在并发检查 Mod 更新…";

            List<ModEntry> snapshot = Mods.ToList();
            List<(ModEntry Mod, ModUpdateInfo? Update)> results = new List<(ModEntry, ModUpdateInfo?)>();
            int failures = 0;
            object gate = new object();
            using SemaphoreSlim slots = new SemaphoreSlim(4);

            Task[] tasks = snapshot.Select(mod => Task.Run(async () =>
            {
                await slots.WaitAsync().ConfigureAwait(false);

                try
                {
                    ModUpdateInfo? update = await _updates
                        .CheckAsync(mod, _gameVersion, _loader, CancellationToken.None)
                        .ConfigureAwait(false);

                    lock (gate)
                    {
                        results.Add((mod, update));
                    }
                }
                catch (Exception)
                {
                    // 单个 mod 查询失败不影响其它，但必须让用户知道这不是“没有更新”。
                    lock (gate)
                    {
                        failures++;
                    }
                }
                finally
                {
                    slots.Release();
                }
            })).ToArray();

            await Task.WhenAll(tasks).ConfigureAwait(true);

            int found = 0;

            // 结果统一回到 UI 线程写回，避免 ModEntry.PropertyChanged 跨线程。
            foreach ((ModEntry mod, ModUpdateInfo? update) in results)
            {
                if (update != null)
                {
                    mod.ProjectUrl = update.ProjectUrl;
                    mod.LatestVersion = update.LatestVersion;
                }

                if (mod.HasUpdate)
                {
                    found++;
                }
            }

            OnPropertyChanged(nameof(Mods));
            if (failures > 0)
            {
                StatusText = found == 0
                    ? "更新检查失败：" + failures + " 个模组未返回结果，请检查网络后重试。"
                    : "发现 " + found + " 个模组有新版本；另有 " + failures
                      + " 个模组检查失败，请稍后重试。";
            }
            else
            {
                StatusText = found == 0 ? "没有发现新版本。" : "发现 " + found + " 个模组有新版本。";
            }
            }
            finally
            {
                IsCheckingUpdates = false;
            }
        }
    }

    /// <summary>Mod 管理页检测到的一个渲染器/优化 Mod。</summary>
    public sealed class RendererModInfo
    {
        public RendererModInfo(string kind, string name, string version, bool enabled, string fileName)
        {
            Kind = kind;
            Name = name;
            Version = version;
            Enabled = enabled;
            FileName = fileName;
        }

        public string Kind { get; }

        public string Name { get; }

        public string Version { get; }

        public bool Enabled { get; }

        public string FileName { get; }

        public string StateText => Enabled ? "已启用" : "已禁用";

        public string DisplayText => Name + (Version.Length == 0 ? string.Empty : " " + Version)
            + "（" + StateText + "）";
    }
}
