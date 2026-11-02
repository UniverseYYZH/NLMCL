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
        /// <summary>主题名：light / dark。</summary>
        void ApplyTheme(string theme);

        /// <summary>缩放倍数（1.0 = 100%）。</summary>
        void ApplyScale(double scale);
    }
}
