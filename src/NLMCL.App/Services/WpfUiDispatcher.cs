using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NLMCL.Abstractions;

namespace NLMCL.App.Services
{
    /// <summary>
    /// <see cref="IUiDispatcher"/> 的 WPF 实现：投递到主窗口的 Dispatcher。
    ///
    /// 用 <c>BeginInvoke</c>（异步）而不是 <c>Invoke</c>：事件来自进程输出泵，
    /// 如果 UI 线程正好在等这把锁就会互等——异步投递不会往回阻塞读取线程。
    /// </summary>
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        public bool IsOnUiThread
        {
            get
            {
                Dispatcher? dispatcher = Application.Current?.Dispatcher;
                return dispatcher == null || dispatcher.CheckAccess();
            }
        }

        public void Post(Action action)
        {
            if (action == null)
            {
                return;
            }

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            try
            {
                dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
            }
            catch (InvalidOperationException)
            {
                // 窗口正在关闭、Dispatcher 已停：这条消息没人要了，丢弃比抛到读取线程上好。
            }
            catch (TaskCanceledException)
            {
            }
        }
    }
}
