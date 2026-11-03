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
    /// 「皮肤」页：显示当前账号由认证服务器下发的皮肤（预览 + 保存到本地）。
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

        public SkinViewModel(ISkinService skins, IFilePicker? filePicker = null)
        {
            _skins = skins ?? throw new ArgumentNullException(nameof(skins));
            _filePicker = filePicker;
        }

        [ObservableProperty]
        private string _statusText = "未登录。登录账号后这里会显示皮肤。";

        /// <summary>预览图（本地临时 PNG 路径）。</summary>
        [ObservableProperty]
        private string? _previewPath;

        [ObservableProperty]
        private string _accountText = "未登录";

        [ObservableProperty]
        private string _modelText = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

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

                string preview = Path.Combine(Path.GetTempPath(), "nlmcl-skin-preview.png");

                if (File.Exists(preview))
                {
                    File.Delete(preview);
                }

                await DownloadPreviewAsync(skin.SkinUrl, preview).ConfigureAwait(true);

                // 同一个路径重复设置时 WPF 不刷新，换个名字绕过图片缓存
                string unique = Path.Combine(
                    Path.GetTempPath(), "nlmcl-skin-" + DateTime.Now.Ticks + ".png");

                File.Move(preview, unique);
                PreviewPath = unique;

                StatusText = "皮肤来源：" + skin.Source;
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
                PreviewPath = saved;
                ModelText = "本地皮肤";
                StatusText = "本地皮肤：" + saved;
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

        /// <summary>预览下载走皮肤服务自己的下载实现（避免和账号令牌逻辑耦合）。</summary>
        private async Task DownloadPreviewAsync(string url, string target)
        {
            // SkinService 的 SaveAsync 会落到皮肤目录；这里只要能拿到图，复用它的下载端口不方便，
            // 因此直接用一个小工具方法：通过 ISkinService 保存到临时目录。
            await _skins.SavePreviewAsync(url, target, CancellationToken.None).ConfigureAwait(true);
        }
    }
}
