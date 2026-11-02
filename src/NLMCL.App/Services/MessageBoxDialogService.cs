using System.Windows;
using NLMCL.Abstractions;

namespace NLMCL.App.Services
{
    /// <summary>
    /// <see cref="IDialogService"/> 的 WPF 实现。
    /// 只有确认框，默认按钮放在"否"上——避免连按回车误删。
    /// </summary>
    public sealed class MessageBoxDialogService : IDialogService
    {
        public bool Confirm(string title, string message)
        {
            MessageBoxResult result = MessageBox.Show(
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            return result == MessageBoxResult.Yes;
        }
    }
}
