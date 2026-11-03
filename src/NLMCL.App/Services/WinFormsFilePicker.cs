using System;
using System.IO;
using System.Windows.Forms;
using NLMCL.Abstractions;

namespace NLMCL.App.Services
{
    /// <summary>选文件（导入 .mrpack 用）。和文件夹选择一样走 WinForms 对话框。</summary>
    public sealed class WinFormsFilePicker : IFilePicker
    {
        public string? PickFile(string title, string filter)
        {
            using (OpenFileDialog dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true,
                Multiselect = false
            })
            {
                return dialog.ShowDialog() == DialogResult.OK && File.Exists(dialog.FileName)
                    ? dialog.FileName
                    : null;
            }
        }
    }
}
