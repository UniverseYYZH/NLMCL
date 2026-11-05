using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NLMCL.App
{
    /// <summary>布尔取反，用于「忙碌时禁用按钮」这类绑定。</summary>
    public sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : true;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b ? !b : true;
        }
    }

    /// <summary>布尔取反后转 Visibility。</summary>
    public sealed class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool visible = value is bool b && !b;
            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// 输入框为空时显示占位文字（WPF 的 TextBox 没有 placeholder）。
    /// 拿到非字符串或非空字符串时隐藏。
    /// </summary>
    public sealed class EmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value as string ?? string.Empty;
            return string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>非 null 显示、null 隐藏（用于"有档案才显示卡片"）。</summary>
    public sealed class NullToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value == null ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotSupportedException();
        }
    }

    /// <summary>
    /// 把资源图标 URL 换成 ImageSource（带内存缓存，同一 URL 只解码一次）。
    /// 下载失败返回 null，界面自动退回占位图标。
    /// </summary>
    public sealed class ThumbnailConverter : System.Windows.Data.IValueConverter
    {
        private static readonly System.Collections.Generic.Dictionary<string, System.Windows.Media.Imaging.BitmapImage> Cache
            = new System.Collections.Generic.Dictionary<string, System.Windows.Media.Imaging.BitmapImage>();

        public object? Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string? url = value as string;

            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            if (Cache.TryGetValue(url, out System.Windows.Media.Imaging.BitmapImage? cached))
            {
                return cached;
            }

            try
            {
                System.Windows.Media.Imaging.BitmapImage bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new System.Uri(url, System.UriKind.Absolute);
                bitmap.DecodePixelWidth = 96;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                Cache[url] = bitmap;
                return bitmap;
            }
            catch (System.NotSupportedException)
            {
                return null;
            }
            catch (System.IO.IOException)
            {
                return null;
            }
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new System.NotSupportedException();
        }
    }
}