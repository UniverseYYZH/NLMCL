using System.Windows;

namespace NLMCL.App.Services
{
    /// <summary><see cref="NLMCL.Abstractions.IClipboard"/> 的 WPF 实现。</summary>
    public sealed class WpfClipboard : NLMCL.Abstractions.IClipboard
    {
        public void SetText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // 剪贴板被别的进程占着时会抛，属于可忽略的偶发失败
            }
        }
    }
}
