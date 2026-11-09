using System.IO;
using System.Windows.Forms;
using NLMCL.Abstractions;

namespace NLMCL.App.Services
{
    /// <summary>导出设置用的保存文件对话框。</summary>
    public sealed class WinFormsSaveFilePicker : IFileSavePicker
    {
        public string? PickFile(string title, string filter, string defaultFileName)
        {
            using (SaveFileDialog dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = defaultFileName,
                AddExtension = true,
                DefaultExt = "json",
                CheckPathExists = true,
                OverwritePrompt = true
            })
            {
                return dialog.ShowDialog() == DialogResult.OK &&
                       !string.IsNullOrWhiteSpace(dialog.FileName)
                    ? dialog.FileName
                    : null;
            }
        }
    }
}
