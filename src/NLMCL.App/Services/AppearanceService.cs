using System;
using System.Windows;
using System.Windows.Media;
using NLMCL.Abstractions;

namespace NLMCL.App.Services
{
    /// <summary>
    /// 主题与缩放的 WPF 实现。
    ///
    /// 主题：Design.xaml 提供浅色默认值；深色时把 Dark.xaml 追加到窗口的资源字典末尾
    /// （后加入的同名键生效）。界面里所有颜色都用 DynamicResource 引用，所以能即时刷新。
    /// 缩放：直接给窗口内容加 LayoutTransform，不改窗口尺寸。
    /// </summary>
    public sealed class AppearanceService : IAppearance
    {
        private const string DarkDictionary = "Themes/Dark.xaml";

        public void ApplyTheme(string theme)
        {
            Window? window = Application.Current?.MainWindow;
            if (window == null)
            {
                return;
            }

            var dictionaries = window.Resources.MergedDictionaries;

            // 先摘掉旧的深色字典（可能还没有）
            for (int i = dictionaries.Count - 1; i >= 0; i--)
            {
                string? source = dictionaries[i].Source?.OriginalString;
                if (source != null && source.IndexOf("Dark.xaml", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    dictionaries.RemoveAt(i);
                }
            }

            if (!string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            dictionaries.Add(new ResourceDictionary
            {
                Source = new Uri(DarkDictionary, UriKind.Relative)
            });
        }

        public void ApplyScale(double scale)
        {
            Window? window = Application.Current?.MainWindow;
            if (window?.Content is not FrameworkElement root)
            {
                return;
            }

            double clamped = Math.Max(0.7, Math.Min(1.5, scale));

            if (Math.Abs(clamped - 1.0) < 0.001)
            {
                root.LayoutTransform = null;
                return;
            }

            root.LayoutTransform = new ScaleTransform(clamped, clamped);
        }
    }
}
