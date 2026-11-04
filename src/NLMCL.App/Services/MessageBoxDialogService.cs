using System.Windows;
using System.Windows.Controls;
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

        /// <summary>输入一行文本（重命名/复制实例用）。取消返回 null。</summary>
        public string? Prompt(string title, string message, string initialValue)
        {
            Window window = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            StackPanel panel = new StackPanel { Margin = new Thickness(18) };

            panel.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            TextBox input = new TextBox
            {
                Text = initialValue ?? string.Empty,
                Padding = new Thickness(6, 4, 6, 4)
            };

            panel.Children.Add(input);

            StackPanel buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };

            Button ok = new Button
            {
                Content = "确定",
                IsDefault = true,
                MinWidth = 76,
                Padding = new Thickness(10, 4, 10, 4)
            };

            Button cancel = new Button
            {
                Content = "取消",
                IsCancel = true,
                MinWidth = 76,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 4, 10, 4)
            };

            ok.Click += (sender, args) =>
            {
                window.DialogResult = true;
                window.Close();
            };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            window.Content = panel;

            window.Loaded += (sender, args) =>
            {
                input.Focus();
                input.SelectAll();
            };

            bool? result = window.ShowDialog();

            string text = input.Text.Trim();

            return result == true && text.Length > 0 ? text : null;
        }
    }
}
