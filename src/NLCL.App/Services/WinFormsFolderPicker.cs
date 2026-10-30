using System;
using System.IO;
using System.Windows.Forms;

namespace NLCL.App.Services
{
    /// <summary>
    /// <see cref="NLCL.Abstractions.IFolderPicker"/> 的实现。
    ///
    /// 用 WinForms 的 FolderBrowserDialog：WPF 在 net48 上没有原生文件夹选择框，
    /// 引第三方包不如直接用系统自带的这个。
    /// </summary>
    public sealed class WinFormsFolderPicker : NLCL.Abstractions.IFolderPicker
    {
        public string? PickFolder(string description, string? initialDirectory)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = description ?? "选择文件夹";
                dialog.ShowNewFolderButton = false;

                if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
                {
                    dialog.SelectedPath = initialDirectory;
                }

                DialogResult result = dialog.ShowDialog();
                return result == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }
    }
}
