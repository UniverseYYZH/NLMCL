using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using NLMCL.Abstractions;

namespace NLMCL.App.Services
{
    /// <summary>
    /// 主题与缩放的 WPF 实现。
    ///
    /// 主题：Design.xaml 提供浅色默认值；深色时把 Dark.xaml 追加到窗口的资源字典末尾
    /// （后加入的同名键生效）。界面里所有颜色都用 DynamicResource 引用，所以能即时刷新。
    /// "system" 读注册表的 AppsUseLightTheme，并监听系统的主题切换事件实时跟随。
    /// 缩放：直接给窗口内容加 LayoutTransform，不改窗口尺寸。
    /// </summary>
    public sealed class AppearanceService : IAppearance
    {
        private const string DarkDictionary = "Themes/Dark.xaml";

        private const string HighContrastDictionary = "Themes/HighContrast.xaml";

        private const string PersonalizeKey =
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        private string _theme = "light";
        private Window? _hooked;

        public AppearanceService()
        {
            // 系统切换浅色/深色时，如果当前是"跟随系统"就立刻跟上
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }

        public void ApplyTheme(string theme)
        {
            _theme = string.IsNullOrWhiteSpace(theme) ? "light" : theme.Trim();

            bool dark = string.Equals(_theme, "dark", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(_theme, "system", StringComparison.OrdinalIgnoreCase) && IsSystemDark());

            bool highContrast = string.Equals(_theme, "highcontrast", StringComparison.OrdinalIgnoreCase);

            Window? window = Application.Current?.MainWindow;
            if (window == null)
            {
                return;
            }

            HookWindow();

            string? overlay = highContrast
                ? HighContrastDictionary
                : dark ? DarkDictionary : null;

            ApplyToResources(window.Resources, overlay);
            ApplyToLogicalTree(window, overlay);
        }

        /// <summary>
        /// 页面各自合并了 Design.xaml；只在窗口级加主题字典会被页面本地字典遮住。
        /// 因此把主题覆盖层同样追加到每个带 Design.xaml 的页面资源里。
        /// </summary>
        private static void ApplyToLogicalTree(object node, string? overlay)
        {
            if (node is not FrameworkElement element)
            {
                return;
            }

            ApplyToResources(element.Resources, overlay);

            foreach (object child in LogicalTreeHelper.GetChildren(element))
            {
                ApplyToLogicalTree(child, overlay);
            }
        }

        private static void ApplyToResources(ResourceDictionary resources, string? overlay)
        {
            var merged = resources.MergedDictionaries;

            for (int i = merged.Count - 1; i >= 0; i--)
            {
                string? source = merged[i].Source?.OriginalString;
                if (source != null
                    && (source.IndexOf("Dark.xaml", StringComparison.OrdinalIgnoreCase) >= 0
                        || source.IndexOf("HighContrast.xaml", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    merged.RemoveAt(i);
                }
            }

            if (overlay == null)
            {
                return;
            }

            bool hasDesign = merged.Any(dictionary =>
                dictionary.Source?.OriginalString?.IndexOf(
                    "Design.xaml", StringComparison.OrdinalIgnoreCase) >= 0);

            if (hasDesign)
            {
                merged.Add(new ResourceDictionary
                {
                    Source = new Uri(overlay, UriKind.Relative)
                });
            }
        }

        public bool IsMaximized
        {
            get
            {
                HookWindow();
                return _hooked?.WindowState == WindowState.Maximized;
            }
        }

        public event EventHandler? WindowStateChanged;

        /// <summary>第一次用到外观时把窗口的 StateChanged 接出来（此时窗口已创建）。</summary>
        private void HookWindow()
        {
            Window? window = Application.Current?.MainWindow;
            if (window == null || ReferenceEquals(window, _hooked))
            {
                return;
            }

            _hooked = window;
            window.StateChanged += (_, __) => WindowStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ApplyScale(double scale)
        {
            Window? window = Application.Current?.MainWindow;
            if (window?.Content is not FrameworkElement root)
            {
                return;
            }

            double clamped = Math.Max(0.7, Math.Min(1.6, scale));

            if (Math.Abs(clamped - 1.0) < 0.001)
            {
                root.LayoutTransform = null;
                return;
            }

            root.LayoutTransform = new ScaleTransform(clamped, clamped);
        }

        private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
        {
            if (!string.Equals(_theme, "system", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Application? app = Application.Current;
            if (app == null)
            {
                return;
            }

            app.Dispatcher.BeginInvoke(new Action(() => ApplyTheme("system")));
        }

        /// <summary>Windows 的"应用模式"是否为深色（0 = 深色）。</summary>
        internal static bool IsSystemDark()
        {
            try
            {
                object? value = Registry.GetValue(PersonalizeKey, "AppsUseLightTheme", 1);
                return value is int i && i == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}