using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 「皮肤」页：显示当前账号由认证服务器下发的皮肤，并渲染成 3D 立绘。
    ///
    /// 皮肤本身**不能在这里改**——它属于账号，由皮肤站/Mojang 下发；
    /// 游戏里生效靠 authlib-injector（外置登录）或正版会话。
    /// </summary>
    public sealed partial class SkinViewModel : ObservableObject
    {
        private readonly ISkinService _skins;
        private readonly IFilePicker? _filePicker;
        private readonly IMuaSkinLibrary? _library;
        private readonly IProfileTextureUploader? _uploader;
        private readonly IDialogService? _dialogs;
        private readonly IAppPaths? _paths;

        private Account? _account;
        private SkinInfo? _current;
        private string? _skinPng;
        private bool _slim;
        private string _libraryRoot = "https://skin.mualliance.ltd";

        public SkinViewModel(
            ISkinService skins,
            IFilePicker? filePicker = null,
            IMuaSkinLibrary? library = null,
            IProfileTextureUploader? uploader = null,
            IDialogService? dialogs = null,
            IAppPaths? paths = null)
        {
            _skins = skins ?? throw new ArgumentNullException(nameof(skins));
            _filePicker = filePicker;
            _library = library;
            _uploader = uploader;
            _dialogs = dialogs;
            _paths = paths;
            LocalSkins.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasLocalSkins));
            RefreshLocalSkins();
            LibrarySkins.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasLibrarySkins));
        }

        [ObservableProperty]
        private string _statusText = "未登录。登录账号后这里会显示皮肤。";

        /// <summary>3D 渲染结果（本地 PNG 路径）。</summary>
        [ObservableProperty]
        private string? _previewPath;

        [ObservableProperty]
        private string _accountText = "未登录";

        [ObservableProperty]
        private string _modelText = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        /// <summary>MUA / Blessing Skin 皮肤库列表。</summary>
        public ObservableCollection<MuaSkinItemViewModel> LibrarySkins { get; }
            = new ObservableCollection<MuaSkinItemViewModel>();

        /// <summary>本机皮肤目录里的 PNG，供 F 皮肤页的「本地皮肤库」直接选用。</summary>
        public ObservableCollection<LocalSkinItem> LocalSkins { get; }
            = new ObservableCollection<LocalSkinItem>();

        [ObservableProperty]
        private string _localSkinStatusText = "还没有本地皮肤；点「打开本地皮肤」导入一张。";

        public bool HasLocalSkins => LocalSkins.Count > 0;

        /// <summary>扫描本机皮肤目录；不联网，失败时只显示空库。</summary>
        [RelayCommand]
        public void RefreshLocalSkins()
        {
            LocalSkins.Clear();

            if (_paths == null)
            {
                LocalSkinStatusText = "本地皮肤目录不可用。";
                return;
            }

            try
            {
                if (!Directory.Exists(_paths.SkinDirectory))
                {
                    LocalSkinStatusText = "还没有本地皮肤；点「打开本地皮肤」导入一张。";
                    return;
                }

                foreach (string file in Directory.GetFiles(_paths.SkinDirectory, "*.png")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(24))
                {
                    LocalSkins.Add(new LocalSkinItem(
                        file,
                        !string.IsNullOrEmpty(_skinPng)
                        && string.Equals(Path.GetFullPath(file), Path.GetFullPath(_skinPng), StringComparison.OrdinalIgnoreCase)));
                }

                LocalSkinStatusText = LocalSkins.Count == 0
                    ? "还没有本地皮肤；点「打开本地皮肤」导入一张。"
                    : "共 " + LocalSkins.Count + " 张本地皮肤，点击卡片预览并使用。";
            }
            catch (Exception ex)
            {
                LocalSkinStatusText = "读取本地皮肤失败：" + ex.Message;
            }
        }

        /// <summary>本地皮肤：直接预览并设为当前账号的本地皮肤。</summary>
        [RelayCommand]
        public async Task UseLocalSkinAsync(LocalSkinItem? item)
        {
            if (item == null || !File.Exists(item.Path))
            {
                return;
            }

            try
            {
                string saved = _account != null
                    ? await _skins.ImportLocalAsync(_account, item.Path, CancellationToken.None)
                        .ConfigureAwait(true)
                    : await _skins.ImportLocalAsync(item.Path, "player", CancellationToken.None)
                        .ConfigureAwait(true);

                _current = null;
                _skinPng = saved;
                _slim = false;

                await RenderAsync().ConfigureAwait(true);

                ModelText = "本地皮肤";
                StatusText = "已使用本地皮肤：" + item.Name;
                RefreshLocalSkins();
            }
            catch (Exception ex)
            {
                StatusText = "使用本地皮肤失败：" + ex.Message;
            }
        }
        [ObservableProperty]
        private bool _isLibraryBusy;

        [ObservableProperty]
        private string _libraryStatusText = "点击「刷新皮肤库」加载 MUA 用户中心的公开皮肤。";

        [ObservableProperty]
        private int _libraryPage = 1;

        [ObservableProperty]
        private int _libraryLastPage = 1;

        [ObservableProperty]
        private bool _isExternalAccount;

        public bool CanApplyLibrarySkins => _uploader != null
            && _account != null
            && (_account.Type == AccountType.External || _account.Type == AccountType.Microsoft);

        public bool HasLibrarySkins => LibrarySkins.Count > 0;

        partial void OnIsExternalAccountChanged(bool value)
        {
            OnPropertyChanged(nameof(CanApplyLibrarySkins));
        }

        /// <summary>水平转角（度）。</summary>
        [ObservableProperty]
        private double _yaw = 28;

        /// <summary>是否轻微自动旋转。</summary>
        [ObservableProperty]
        private bool _isAutoRotating = true;

        private bool _rendering;
        private bool _renderPending;
        private int _refreshGeneration;

        /// <summary>俯仰角（度）。</summary>
        [ObservableProperty]
        private double _pitch = 8;

        public bool HasPreview => !string.IsNullOrEmpty(PreviewPath);

        partial void OnPreviewPathChanged(string? value)
        {
            OnPropertyChanged(nameof(HasPreview));
        }

        /// <summary>主窗口在账号变化时调用。</summary>
        public void SetAccount(Account? account)
        {
            _account = account;
            IsOfflineAccount = account != null && account.Type == AccountType.Offline;
            IsExternalAccount = account != null && account.Type == AccountType.External;
            AccountText = account == null ? "未登录" : account.Name + "（" + account.TypeLabel + "）";

            _libraryRoot = NLMCL.Core.Resources.MuaSkinLibraryService
                .ResolveSiteRoot(account?.AuthServer);
            LibrarySkins.Clear();
            LibraryPage = 1;
            LibraryLastPage = 1;
            LibraryStatusText = account == null
                ? "登录后可以浏览 MUA 皮肤库。"
                : "点击「刷新皮肤库」加载 MUA 用户中心的公开皮肤。";
            OnPropertyChanged(nameof(CanApplyLibrarySkins));

            _ = RefreshAsync();
        }

        /// <summary>进入皮肤页时调用：等皮肤刷新完再加载当前站点皮肤库。</summary>
        public void ActivateLibrary()
        {
            if (_library != null && LibrarySkins.Count == 0 && !IsBusy)
            {
                _ = LoadLibraryAsync();
            }
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            int generation = ++_refreshGeneration;
            Account? account = _account;

            if (account == null)
            {
                _current = null;
                _skinPng = null;
                PreviewPath = null;
                ModelText = string.Empty;
                StatusText = "未登录。登录账号后这里会显示皮肤。";
                IsBusy = false;
                return;
            }

            IsBusy = true;
            StatusText = "正在读取皮肤…";

            try
            {
                // 离线账号先看本地保存的皮肤；正版/外置账号仍以服务端皮肤为准。
                string? localSkin = account.Type == AccountType.Offline
                    ? await _skins
                        .GetLocalSkinPathAsync(account, CancellationToken.None)
                        .ConfigureAwait(true)
                    : null;

                if (generation != _refreshGeneration)
                {
                    return;
                }

                if (localSkin != null)
                {
                    _current = null;
                    _skinPng = localSkin;
                    _slim = false;

                    await RenderAsync().ConfigureAwait(true);

                    if (generation != _refreshGeneration)
                    {
                        return;
                    }

                    ModelText = "本地皮肤";
                    StatusText = "本地皮肤：" + localSkin + " · 拖动图片可旋转";
                    return;
                }

                SkinInfo? skin = await _skins.GetAsync(account, CancellationToken.None).ConfigureAwait(true);

                if (generation != _refreshGeneration)
                {
                    return;
                }

                _current = skin;
                ModelText = DescribeSkin(skin);

                string source = Path.Combine(
                    Path.GetTempPath(), "nlmcl-skin-src-" + DateTime.Now.Ticks + ".png");

                if (skin?.SkinUrl == null)
                {
                    // 没有自定义皮肤也要能预览：用内置默认皮肤兜底，
                    // 否则玩家登录后看到的是一片空白，不知道是"没有皮肤"还是"功能坏了"。
                    string fallback = await _skins.GetDefaultSkinAsync(CancellationToken.None).ConfigureAwait(true);

                    if (generation != _refreshGeneration)
                    {
                        return;
                    }

                    File.Copy(fallback, source, overwrite: true);

                    _skinPng = source;
                    _slim = false;

                    await RenderAsync().ConfigureAwait(true);

                    if (generation != _refreshGeneration)
                    {
                        return;
                    }

                    StatusText = account.Type == AccountType.Offline
                        ? "离线账号 · 显示的是默认皮肤（经典 Steve）"
                        : "该账号没有自定义皮肤 · 显示的是默认皮肤（经典 Steve）";

                    return;
                }

                try
                {
                    await _skins.SavePreviewAsync(skin.SkinUrl, source, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (Exception downloadError)
                {
                    // 官方贴图下载失败时不要留空白，回退默认皮肤并提示重试。
                    string fallback = await _skins
                        .GetDefaultSkinAsync(CancellationToken.None)
                        .ConfigureAwait(true);

                    if (generation != _refreshGeneration)
                    {
                        return;
                    }

                    File.Copy(fallback, source, overwrite: true);

                    _current = null;
                    ModelText = "默认皮肤（经典 Steve）";
                    _skinPng = source;
                    _slim = false;

                    await RenderAsync().ConfigureAwait(true);

                    if (generation != _refreshGeneration)
                    {
                        return;
                    }

                    StatusText = "官方皮肤下载失败，已显示默认皮肤；点「刷新」重试：" +
                                 downloadError.Message;
                    return;
                }

                if (generation != _refreshGeneration)
                {
                    return;
                }

                _skinPng = source;
                _slim = string.Equals(skin.Model, "slim", StringComparison.OrdinalIgnoreCase);

                await RenderAsync().ConfigureAwait(true);

                if (generation != _refreshGeneration)
                {
                    return;
                }

                StatusText = "皮肤来源：" + skin.Source + " · 拖动图片可旋转";
            }
            catch (Exception ex)
            {
                if (generation == _refreshGeneration)
                {
                    PreviewPath = null;
                    StatusText = "读取皮肤失败：" + ex.Message;
                }
            }
            finally
            {
                // 旧请求不能把新请求的 busy 状态提前关掉。
                if (generation == _refreshGeneration)
                {
                    IsBusy = false;

                    // 皮肤请求完成后才拉皮肤库，避免和皮肤下载并发抢 PCL 传输。
                    if (_library != null && LibrarySkins.Count == 0 && _account != null)
                    {
                        _ = LoadLibraryAsync();
                    }
                }
            }
        }

        /// <summary>加载当前账号所属站点的皮肤库。</summary>
        [RelayCommand]
        public async Task LoadLibraryAsync()
        {
            if (_library == null)
            {
                LibraryStatusText = "皮肤库服务不可用。";
                return;
            }

            if (IsLibraryBusy)
            {
                return;
            }

            IsLibraryBusy = true;
            LibraryStatusText = "正在加载 MUA 皮肤库…";

            try
            {
                MuaSkinPage page = await _library
                    .ListAsync(_libraryRoot, LibraryPage, CancellationToken.None)
                    .ConfigureAwait(true);

                LibrarySkins.Clear();

                foreach (MuaSkin skin in page.Items)
                {
                    LibrarySkins.Add(new MuaSkinItemViewModel(skin));
                }

                LibraryPage = page.Page;
                LibraryLastPage = page.LastPage;
                LibraryStatusText = page.Items.Count == 0
                    ? "这一页没有公开皮肤。"
                    : "共 " + page.Total + " 个公开皮肤 · 第 " + page.Page + "/" + page.LastPage + " 页";
            }
            catch (Exception ex)
            {
                LibraryStatusText = "加载皮肤库失败：" + ex.Message;
            }
            finally
            {
                IsLibraryBusy = false;
            }
        }

        [RelayCommand]
        public async Task RefreshLibraryAsync()
        {
            LibraryPage = 1;
            await LoadLibraryAsync().ConfigureAwait(true);
        }

        [RelayCommand]
        public async Task PrevLibraryPageAsync()
        {
            if (LibraryPage <= 1 || IsLibraryBusy)
            {
                return;
            }

            LibraryPage--;
            await LoadLibraryAsync().ConfigureAwait(true);
        }

        [RelayCommand]
        public async Task NextLibraryPageAsync()
        {
            if (LibraryPage >= LibraryLastPage || IsLibraryBusy)
            {
                return;
            }

            LibraryPage++;
            await LoadLibraryAsync().ConfigureAwait(true);
        }

        /// <summary>只下载到主预览区做 3D 预览，不修改账号皮肤。</summary>
        [RelayCommand]
        public async Task PreviewLibrarySkinAsync(MuaSkinItemViewModel? item)
        {
            if (item == null || _account == null)
            {
                LibraryStatusText = "请先登录账号。";
                return;
            }

            if (_library == null || IsLibraryBusy)
            {
                return;
            }

            IsLibraryBusy = true;
            LibraryStatusText = "正在下载「" + item.Name + "」用于 3D 预览…";

            try
            {
                string path = Path.Combine(
                    Path.GetTempPath(),
                    "nlmcl-mua-preview-" + item.Skin.Tid + "-" +
                    Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");

                await _library
                    .DownloadAsync(item.Skin, path, CancellationToken.None)
                    .ConfigureAwait(true);

                _current = new SkinInfo(
                    _account.Name,
                    item.Skin.DownloadUrl,
                    item.Skin.Model,
                    "MUA 皮肤库（预览）");

                _skinPng = path;
                _slim = item.Skin.Model == "slim";
                ModelText = DescribeSkin(_current);

                await RenderAsync().ConfigureAwait(true);

                StatusText = "正在 3D 预览「" + item.Name + "」；点「装配」才会写入账号";
                LibraryStatusText = "3D 预览：" + item.Name + "（未装配）";
            }
            catch (Exception ex)
            {
                LibraryStatusText = "预览失败：" + ex.Message;
            }
            finally
            {
                IsLibraryBusy = false;
            }
        }

        /// <summary>下载并预览皮肤；外置/MUA/正版账号可一键装配到当前角色。</summary>
        [RelayCommand]
        public async Task ApplyLibrarySkinAsync(MuaSkinItemViewModel? item)
        {
            if (item == null || _account == null)
            {
                LibraryStatusText = "请先登录账号。";
                return;
            }

            if (_library == null)
            {
                LibraryStatusText = "皮肤库服务不可用。";
                return;
            }

            bool canApply = CanApplyLibrarySkins;

            if (canApply
                && _dialogs != null
                && !_dialogs.Confirm(
                    "装配皮肤",
                    "确定把「" + item.Name + "」装配到当前账号吗？\n\n" +
                    "这会替换该角色当前皮肤。" +
                    (_account.Type == AccountType.Microsoft
                        ? "（上传到 Minecraft Services）"
                        : "（上传到 " + _libraryRoot + "）")))
            {
                return;
            }

            IsLibraryBusy = true;
            LibraryStatusText = "正在下载「" + item.Name + "」…";

            try
            {
                string path = Path.Combine(
                    Path.GetTempPath(),
                    "nlmcl-mua-" + item.Skin.Tid + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");

                await _library
                    .DownloadAsync(item.Skin, path, CancellationToken.None)
                    .ConfigureAwait(true);

                _current = new SkinInfo(
                    _account.Name,
                    item.Skin.DownloadUrl,
                    item.Skin.Model,
                    "MUA 皮肤库");

                _skinPng = path;
                _slim = item.Skin.Model == "slim";
                ModelText = DescribeSkin(_current);

                await RenderAsync().ConfigureAwait(true);

                if (!canApply)
                {
                    LibraryStatusText = "已下载到本地预览；离线账号不能直接装配。";
                    StatusText = "已下载「" + item.Name + "」到本地预览";
                    return;
                }

                LibraryStatusText = "正在装配到 " + _account.Name + "…";
                await _uploader!
                    .UploadSkinAsync(_account, path, item.Skin.Model, CancellationToken.None)
                    .ConfigureAwait(true);

                await RefreshAsync().ConfigureAwait(true);
                StatusText = "已装配「" + item.Name + "」到 " + _account.Name;
                LibraryStatusText = "装配成功：" + item.Name;
            }
            catch (Exception ex)
            {
                LibraryStatusText = "装配失败：" + ex.Message;
                StatusText = "装配失败：" + ex.Message;
            }
            finally
            {
                IsLibraryBusy = false;
            }
        }

        /// <summary>是否离线账号（决定「查同名正版皮肤」按钮是否显示）。</summary>
        [ObservableProperty]
        private bool _isOfflineAccount;

        /// <summary>
        /// 离线账号的手动操作：按名字去 Mojang 查同名正版皮肤。
        /// 不自动执行——自动匹配会冒用他人皮肤，容易引起误会。
        /// </summary>
        [RelayCommand]
        public async Task QueryByNameAsync()
        {
            if (_account == null || IsBusy)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在查询同名正版账号…";

            try
            {
                SkinInfo? skin = await _skins
                    .LookupByNameAsync(_account.Name, CancellationToken.None)
                    .ConfigureAwait(true);

                if (skin?.SkinUrl == null)
                {
                    StatusText = "没有找到同名正版账号（" + _account.Name + "），继续使用默认皮肤";
                    return;
                }

                _current = skin;
                ModelText = DescribeSkin(skin);

                string source = Path.Combine(
                    Path.GetTempPath(), "nlmcl-skin-src-" + DateTime.Now.Ticks + ".png");

                await _skins.SavePreviewAsync(skin.SkinUrl, source, CancellationToken.None)
                    .ConfigureAwait(true);

                _skinPng = source;
                _slim = string.Equals(skin.Model, "slim", StringComparison.OrdinalIgnoreCase);

                await RenderAsync().ConfigureAwait(true);

                StatusText = "已找到同名正版账号的皮肤（" + skin.Source + "），仅本机预览";
            }
            catch (Exception ex)
            {
                StatusText = "查询失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>离线账号的出口：选本地 PNG，复制进皮肤目录并预览。</summary>
        [RelayCommand]
        public async Task PickLocalAsync()
        {
            if (_filePicker == null)
            {
                StatusText = "文件选择不可用。";
                return;
            }

            string? path = _filePicker.PickFile("选择皮肤 PNG", "PNG 皮肤 (*.png)|*.png|所有文件 (*.*)|*.*");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                string saved = _account != null
                    ? await _skins
                        .ImportLocalAsync(_account, path!, CancellationToken.None)
                        .ConfigureAwait(true)
                    : await _skins
                        .ImportLocalAsync(path!, "player", CancellationToken.None)
                        .ConfigureAwait(true);

                _current = null;
                _skinPng = saved;
                _slim = false;

                await RenderAsync().ConfigureAwait(true);

                ModelText = "本地皮肤";
                StatusText = "本地皮肤：" + saved + " · 已记住，切换账号后仍会显示";
                RefreshLocalSkins();
            }
            catch (Exception ex)
            {
                StatusText = "导入失败：" + ex.Message;
            }
        }

        [RelayCommand]
        public async Task SaveAsync()
        {
            if (_current == null || string.IsNullOrEmpty(_current.SkinUrl))
            {
                StatusText = "没有可保存的皮肤。";
                return;
            }

            try
            {
                string path = await _skins.SaveAsync(_current, CancellationToken.None).ConfigureAwait(true);
                StatusText = "已保存到 " + path;
            }
            catch (Exception ex)
            {
                StatusText = "保存失败：" + ex.Message;
            }
        }

        [RelayCommand]
        public void ToggleAutoRotate() => IsAutoRotating = !IsAutoRotating;

        /// <summary>自动旋转的一小步（由页面计时器驱动，约 6°/秒）。</summary>
        public void AutoRotateStep()
        {
            if (!IsAutoRotating)
            {
                return;
            }

            Yaw = Normalize(Yaw + 0.8);
            _ = RenderAsync();
        }

        [RelayCommand]
        public void RotateLeft() => RotateBy(-20);

        [RelayCommand]
        public void RotateRight() => RotateBy(20);

        [RelayCommand]
        public async Task ResetView()
        {
            Yaw = 28;
            Pitch = 8;
            await RenderAsync().ConfigureAwait(true);
        }

        /// <summary>拖动预览图时由页面调用（正数向右转）。</summary>
        public void RotateBy(double degrees)
        {
            Yaw = Normalize(Yaw + degrees);
            _ = RenderAsync();
        }

        private async Task RenderAsync()
        {
            if (string.IsNullOrEmpty(_skinPng))
            {
                return;
            }

            if (_rendering)
            {
                _renderPending = true;
                return;
            }

            do
            {
                _renderPending = false;

                string skin = _skinPng!;
                bool slim = _slim;

                _rendering = true;

                try
                {
                    string? path = await _skins
                        .Create3DPreviewAsync(skin, Yaw, Pitch, slim, CancellationToken.None)
                        .ConfigureAwait(true);

                    // 渲染期间切了账号/换了皮肤：旧结果不能覆盖新预览。
                    if (string.Equals(_skinPng, skin, StringComparison.Ordinal) && _slim == slim)
                    {
                        PreviewPath = path;
                    }
                }
                catch (Exception ex)
                {
                    if (string.Equals(_skinPng, skin, StringComparison.Ordinal))
                    {
                        StatusText = "渲染失败：" + ex.Message;
                    }
                }
                finally
                {
                    _rendering = false;
                }
            }
            while (_renderPending && !string.IsNullOrEmpty(_skinPng));
        }

        private static string DescribeSkin(SkinInfo? skin)
        {
            if (skin?.SkinUrl == null)
            {
                return "默认皮肤（经典 Steve）";
            }

            string model = string.Equals(skin.Model, "slim", StringComparison.OrdinalIgnoreCase)
                ? "纤细"
                : "经典";

            // 认证接口只给皮肤 URL / 模型，不给皮肤名字；
            // 这里不再把“经典（Steve）”冒充成皮肤名，统一显示为自定义皮肤。
            return "自定义皮肤 · " + model;
        }

        private static double Normalize(double degrees)
        {
            double value = degrees % 360;

            return value < 0 ? value + 360 : value;
        }
    }
/// <summary>本地皮肤库中的一张 PNG。</summary>
    public sealed class LocalSkinItem
    {
        public LocalSkinItem(string path, bool isCurrent = false)
        {
            Path = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
            IsCurrent = isCurrent;
        }

        public string Path { get; }

        public string Name { get; }

        public bool IsCurrent { get; }
    }}
