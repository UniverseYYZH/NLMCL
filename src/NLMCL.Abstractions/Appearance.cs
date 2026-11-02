using System;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 外观：主题与界面缩放。
    ///
    /// 抽成端口是因为 ViewModel 不能碰 WPF 的 ResourceDictionary / LayoutTransform；
    /// 实现放在 App 层（组合根），初始化时按设置应用一次。
    /// </summary>
    public interface IAppearance
    {
        /// <summary>主题名：light / dark / system。</summary>
        void ApplyTheme(string theme);

        /// <summary>缩放倍数（1.0 = 100%）。</summary>
        void ApplyScale(double scale);

        /// <summary>窗口当前是否最大化。缩放按窗口状态取不同档位。</summary>
        bool IsMaximized { get; }

        /// <summary>窗口最大化/还原时触发（实现负责把它从 WPF 线程转发出来）。</summary>
        event EventHandler? WindowStateChanged;
    }
}
