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

                if (skin?.SkinUrl == null)
                {
                    PreviewPath = null;
                    StatusText = skin == null
                        ? "离线账号没有服务器皮肤。可以点「打开本地皮肤」预览自己的 PNG。"
                        : "该账号还没有设置皮肤。";
                    return;
                }

                string source = Path.Combine(
                    Path.GetTempPath(), "nlmcl-skin-src-" + DateTime.Now.Ticks + ".png");

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
