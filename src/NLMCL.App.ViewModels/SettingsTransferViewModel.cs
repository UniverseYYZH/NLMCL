using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 设置导入导出。独立 ViewModel，避免继续把文件对话框和 JSON 逻辑塞进 MainViewModel。
    /// </summary>
    public sealed partial class SettingsTransferViewModel : ObservableObject
    {
        private readonly ISettingsTransferService _transfer;
        private readonly IFilePicker? _openPicker;
        private readonly IFileSavePicker? _savePicker;
        private readonly IDialogService? _dialogs;

        public SettingsTransferViewModel(
            ISettingsTransferService transfer,
            IFilePicker? openPicker,
            IFileSavePicker? savePicker,
            IDialogService? dialogs)
        {
            _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
            _openPicker = openPicker;
            _savePicker = savePicker;
            _dialogs = dialogs;
        }

        /// <summary>导入成功后通知 MainViewModel 重新把设置应用到界面。</summary>
        public event Action? Imported;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "导出不含游戏目录、账号令牌和 Java 路径。";

        [RelayCommand]
        public async Task ExportAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (_savePicker == null)
            {
                StatusText = "当前环境没有文件保存对话框，无法导出。";
                return;
            }

            string? path = _savePicker.PickFile(
                "导出 NLMCL 设置",
                "NLMCL 设置 (*.json)|*.json|所有文件 (*.*)|*.*",
                "NLMCL-settings-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            IsBusy = true;

            try
            {
                await _transfer.ExportAsync(path!, CancellationToken.None).ConfigureAwait(true);
                StatusText = "已导出设置：" + path;
            }
            catch (Exception ex)
            {
                StatusText = "导出设置失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ImportAsync()
        {
            if (IsBusy)
            {
                return;
            }

            if (_openPicker == null)
            {
                StatusText = "当前环境没有文件选择对话框，无法导入。";
                return;
            }

            string? path = _openPicker.PickFile(
                "导入 NLMCL 设置",
                "NLMCL 设置 (*.json)|*.json|所有文件 (*.*)|*.*");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            if (_dialogs != null && !_dialogs.Confirm(
                    "导入设置",
                    "将覆盖当前的主题、缩放、下载、默认内存和启动参数。\n\n" +
                    "游戏目录、账号令牌、默认 Java 路径会保留。继续吗？"))
            {
                return;
            }

            IsBusy = true;

            try
            {
                SettingsImportResult result = await _transfer
                    .ImportAsync(path!, CancellationToken.None)
                    .ConfigureAwait(true);

                string message = result.AppliedFields.Count == 0
                    ? "没有导入任何设置。"
                    : "已导入 " + result.AppliedFields.Count + " 项设置：" +
                      string.Join("、", result.AppliedFields) + "。";

                if (result.Warnings.Count > 0)
                {
                    message += " " + string.Join(" ", result.Warnings);
                }

                StatusText = message;
                Imported?.Invoke();
            }
            catch (Exception ex)
            {
                StatusText = "导入设置失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
