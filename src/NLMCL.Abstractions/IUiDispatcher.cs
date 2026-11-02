using System;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 把工作封送（marshal）到 UI 线程的端口。
    ///
    /// 为什么需要它：游戏进程的 stdout/stderr 回调、下载进度、网络续期都在后台线程上触发。
    /// WPF 的 <c>ObservableCollection</c> 一旦绑定到界面，就禁止跨线程修改——修改会抛
    /// <c>NotSupportedException</c>，而该异常发生在非 UI 线程上，没有任何人接得住，
    /// .NET Framework 会直接终止进程（用户看到的是"启动器整个消失"）。
    ///
    /// ViewModel 不引用 WPF，所以把"怎么封送"这件事交给组合根实现。
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>当前是否已在 UI 线程上；没有界面（例如单测）时返回 true。</summary>
        bool IsOnUiThread { get; }

        /// <summary>
        /// 投递到 UI 线程执行。已在 UI 线程（或没有 UI）时同步执行，
        /// 这样测试里不需要消息泵。
        /// </summary>
        void Post(Action action);
    }
}
