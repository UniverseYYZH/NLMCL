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

        public DownloadViewModel(IVersionListProvider provider, IVersionInstaller installer, IAppPaths paths)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _installer = installer ?? throw new ArgumentNullException(nameof(installer));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));

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


        public IReadOnlyList<string> Loaders { get; } = new[] { "原版", "Fabric" };



        [ObservableProperty]
        private string _selectedLoader = "原版";



        /// <summary>Fabric 加载器版本（原版安装时忽略）。</summary>


        [ObservableProperty]
        private string _loaderVersion = "0.20.1";



        public bool IsFabricSelected => string.Equals(SelectedLoader, "Fabric", StringComparison.OrdinalIgnoreCase);



        partial void OnSelectedLoaderChanged(string value)


        {
        OnPropertyChanged(nameof(IsFabricSelected));


        }

        [ObservableProperty]
        private string _statusText = "尚未加载版本列表";

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private bool _isLoading;

        /// <summary>安装进度 0–100；未开始时为 0。</summary>
        [ObservableProperty]
        private double _progress;

        /// <summary>是否已开始安装（决定进度条是否显示）。</summary>
        [ObservableProperty]
        private bool _hasProgress;

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

                // 默认选第一个正式版：清单按发布时间倒序，第一项通常是最新快照
                SelectedVersion = Versions.FirstOrDefault(version => version.Type == "release")
                    ?? (Versions.Count > 0 ? Versions[0] : null);
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

        /// <summary>安装当前选中的版本。</summary>
        [RelayCommand]
        public async Task InstallAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (SelectedVersion == null)
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
            HasProgress = true;
            Progress = 0;
            StatusText = "准备安装 " + SelectedVersion.Id;

            try
            {
                InstallRequest request = new InstallRequest(TargetDirectory, SelectedVersion.Id);
                if (IsFabricSelected)
                {
                    request.Loader = "fabric";
                    request.LoaderVersion = LoaderVersion;
                }

                await _installer
                    .InstallAsync(request, new InlineProgress(ApplyProgress), CancellationToken.None)
                    .ConfigureAwait(true);

                Progress = 100;
                StatusText = "已安装 " + SelectedVersion.Id + " 到 " + TargetDirectory;

                InstallCompleted?.Invoke(this, EventArgs.Empty);
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
