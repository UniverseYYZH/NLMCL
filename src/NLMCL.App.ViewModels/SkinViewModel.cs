using System.Collections.ObjectModel;
using System;
using System.IO;
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

        private Account? _account;
        private SkinInfo? _current;
        private string? _skinPng;
        private bool _slim;

        public SkinViewModel(ISkinService skins, IFilePicker? filePicker = null)
        {
            _skins = skins ?? throw new ArgumentNullException(nameof(skins));
            _filePicker = filePicker;
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

        /// <summary>水平转角（度）。</summary>
        [ObservableProperty]
        private double _yaw = 28;

        /// <summary>是否轻微自动旋转。</summary>
        [ObservableProperty]
        private bool _isAutoRotating = true;

        private bool _rendering;

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
            AccountText = account == null ? "未登录" : account.Name + "（" + account.TypeLabel + "）";
            _ = RefreshAsync();
        }

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (_account == null)
            {
                _current = null;
                _skinPng = null;
                PreviewPath = null;
                ModelText = string.Empty;
                StatusText = "未登录。登录账号后这里会显示皮肤。";
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在读取皮肤…";

            try
            {
                SkinInfo? skin = await _skins.GetAsync(_account, CancellationToken.None).ConfigureAwait(true);

                _current = skin;
                ModelText = skin?.ModelLabel ?? string.Empty;

                string source = Path.Combine(
                    Path.GetTempPath(), "nlmcl-skin-src-" + DateTime.Now.Ticks + ".png");

                if (skin?.SkinUrl == null)
                {
                    // 没有自定义皮肤也要能预览：用内置默认皮肤兜底，
                    // 否则玩家登录后看到的是一片空白，不知道是"没有皮肤"还是"功能坏了"。
                    string fallback = _skins.GetDefaultSkinPath();
                    File.Copy(fallback, source, overwrite: true);

                    _skinPng = source;
                    _slim = false;

                    await RenderAsync().ConfigureAwait(true);

                    StatusText = _account?.Type == AccountType.Offline
                        ? "离线账号 · 显示的是默认皮肤（经典 Steve）"
                        : "该账号没有自定义皮肤 · 显示的是默认皮肤（经典 Steve）";

                    return;
                }

                await _skins.SavePreviewAsync(skin.SkinUrl, source, CancellationToken.None)
                    .ConfigureAwait(true);

                _skinPng = source;
                _slim = string.Equals(skin.Model, "slim", StringComparison.OrdinalIgnoreCase);

                await RenderAsync().ConfigureAwait(true);

                StatusText = "皮肤来源：" + skin.Source + " · 拖动图片可旋转";
            }
            catch (Exception ex)
            {
                PreviewPath = null;
                StatusText = "读取皮肤失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }



        // ---------- 预设皮肤（离线玩家挑一个换外观） ----------

        /// <summary>内置预设皮肤。</summary>
        public ObservableCollection<SkinPreset> Presets { get; } = new ObservableCollection<SkinPreset>();

        /// <summary>是否已加载过预设（避免重复填充）。</summary>
        private bool _presetsLoaded;

        /// <summary>应用一个预设皮肤：渲染预览并复制到本地皮肤目录。</summary>
        [RelayCommand]
        public async Task ApplyPresetAsync(SkinPreset? preset)
        {
            if (preset == null)
            {
                return;
            }

            if (IsBusy)
            {
                return;
            }

            IsBusy = true;
            StatusText = "正在应用预设：" + preset.Name;

            try
            {
                string source = _skins.GetPresetPath(preset.Id);

                _skinPng = source;
                _slim = string.Equals(preset.Model, "slim", StringComparison.OrdinalIgnoreCase);

                await RenderAsync().ConfigureAwait(true);

                string saved = await _skins
                    .SavePresetAsync(preset.Id, _account?.Name ?? "player", CancellationToken.None)
                    .ConfigureAwait(true);

                StatusText = _account == null
                    ? "已应用 " + preset.Name + "（仅本机预览，保存到 " + saved + "）"
                    : "已应用 " + preset.Name + "：本机预览已更新，副本保存在 " + saved
                      + "（服务器上的皮肤不会因此改变）";
            }
            catch (Exception ex)
            {
                StatusText = "应用预设失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>填充预设列表（只做一次）。</summary>
        public void EnsurePresets()
        {
            if (_presetsLoaded)
            {
                return;
            }

            _presetsLoaded = true;

            foreach (SkinPreset preset in _skins.GetPresets())
            {
                Presets.Add(preset);
            }
        }
        // ---------- 皮肤自检：定位"我的皮肤为什么和别人看到的不一样" ----------

        /// <summary>账号类型（微软 / 外置 / 离线）。</summary>
        [ObservableProperty]
        private string _checkAccount = "—";

        /// <summary>authlib-injector 是否会挂上（外置登录才需要）。</summary>
        [ObservableProperty]
        private string _checkInjector = "—";

        /// <summary>皮肤来源（Mojang / 认证服务器 / 默认）。</summary>
        [ObservableProperty]
        private string _checkSource = "—";

        /// <summary>皮肤 PNG 能否下载（字节数或失败原因）。</summary>
        [ObservableProperty]
        private string _checkPng = "—";

        /// <summary>自检结论（一句话）。</summary>
        [ObservableProperty]
        private string _checkVerdict = string.Empty;

        [ObservableProperty]
        private bool _isChecking;

        /// <summary>
        /// 跑一次自检：把"账号类型 / injector / 皮肤来源 / PNG 可下载性"列清楚。
        /// 皮肤不一致时，靠这四项就能区分是启动器没挂 injector、还是服务端没转发。
        /// </summary>
        [RelayCommand]
        public async Task SelfCheckAsync()
        {
            if (_account == null)
            {
                CheckAccount = "未登录";
                CheckInjector = "—";
                CheckSource = "—";
                CheckPng = "—";
                CheckVerdict = "先登录一个账号再自检";
                return;
            }

            IsChecking = true;

            try
            {
                CheckAccount = _account.Name + "（" + _account.TypeLabel + "）";

                if (_account.Type != AccountType.External)
                {
                    CheckInjector = "不需要（只有外置登录才挂 authlib-injector）";
                }
                else if (string.IsNullOrWhiteSpace(_account.AuthServer))
                {
                    CheckInjector = "缺少认证服务器地址";
                }
                else
                {
                    CheckInjector = "应加载 · apiRoot = " + _account.AuthServer;
                }

                SkinInfo? skin;

                try
                {
                    skin = await _skins.GetAsync(_account, CancellationToken.None).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    CheckSource = "读取失败：" + ex.Message;
                    CheckPng = "—";
                    CheckVerdict = "认证接口不可用，游戏内可能因此只显示默认皮肤";
                    return;
                }

                CheckSource = skin == null
                    ? "没有（认证服务器没返回 textures）"
                    : skin.Source + (string.IsNullOrEmpty(skin.ModelLabel) ? string.Empty : " · " + skin.ModelLabel);

                if (skin?.SkinUrl == null)
                {
                    CheckPng = "无皮肤地址";

                    CheckVerdict = _account.Type == AccountType.External
                        ? "外置账号没有皮肤：去皮肤站上传后再点刷新"
                        : "该账号没有自定义皮肤";
                    return;
                }

                try
                {
                    string probe = Path.Combine(
                        Path.GetTempPath(), "nlmcl-skin-probe-" + DateTime.Now.Ticks + ".png");

                    await _skins.SavePreviewAsync(skin.SkinUrl, probe, CancellationToken.None)
                        .ConfigureAwait(true);

                    long size = new FileInfo(probe).Length;
                    CheckPng = "可下载（" + Math.Round(size / 1024.0, 1) + " KB）";

                    CheckVerdict = _account.Type == AccountType.External
                        ? "本机可正常取到皮肤；若游戏内别人看到默认，多半是服务端没转发 textures（找服主查 Velocity 转发 / 服务端 authlib）"
                        : "本机可正常取到皮肤；游戏内不一致由服务端皮肤来源决定";
                }
                catch (Exception ex)
                {
                    CheckPng = "下载失败：" + ex.Message;
                    CheckVerdict = "皮肤地址拉不下来：检查网络或皮肤站是否可达";
                }
            }
            finally
            {
                IsChecking = false;
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
                string saved = await _skins
                    .ImportLocalAsync(path!, _account?.Name ?? "player", CancellationToken.None)
                    .ConfigureAwait(true);

                _current = null;
                _skinPng = saved;
                _slim = false;

                await RenderAsync().ConfigureAwait(true);

                ModelText = "本地皮肤";
                StatusText = "本地皮肤：" + saved + " · 拖动图片可旋转";
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
            if (string.IsNullOrEmpty(_skinPng) || _rendering)
            {
                return;
            }

            _rendering = true;

            try
            {
                PreviewPath = await _skins
                    .Create3DPreviewAsync(_skinPng!, Yaw, Pitch, _slim, CancellationToken.None)
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusText = "渲染失败：" + ex.Message;
            }
            finally
            {
                _rendering = false;
            }
        }

        private static double Normalize(double degrees)
        {
            double value = degrees % 360;

            return value < 0 ? value + 360 : value;
        }
    }
}
