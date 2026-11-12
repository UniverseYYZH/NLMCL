using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 全启动器共享的下载闸门：同一时间只跑一个安装任务，其余排队。
    /// 只负责并发与状态文本，不改变各页面自己的进度模型。
    /// </summary>
    public sealed partial class DownloadQueue : ObservableObject
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private int _waiting;

        [ObservableProperty]
        private string _activeTitle = string.Empty;

        [ObservableProperty]
        private int _waitingCount;

        public string Summary
        {
            get
            {
                if (!string.IsNullOrEmpty(ActiveTitle) && WaitingCount > 0)
                {
                    return ActiveTitle + " · 排队 " + WaitingCount;
                }

                if (!string.IsNullOrEmpty(ActiveTitle))
                {
                    return ActiveTitle;
                }

                return WaitingCount > 0 ? "排队 " + WaitingCount + " 个任务" : string.Empty;
            }
        }

        public async Task RunAsync(string title, Func<Task> work, CancellationToken cancellationToken)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            Interlocked.Increment(ref _waiting);
            WaitingCount = _waiting;
            OnPropertyChanged(nameof(Summary));

            bool acquired = false;

            try
            {
                await _gate.WaitAsync(cancellationToken).ConfigureAwait(true);
                acquired = true;
                Interlocked.Decrement(ref _waiting);
                WaitingCount = _waiting;
                ActiveTitle = title ?? string.Empty;
                OnPropertyChanged(nameof(Summary));

                await work().ConfigureAwait(true);
            }
            finally
            {
                if (!acquired)
                {
                    Interlocked.Decrement(ref _waiting);
                    WaitingCount = _waiting;
                }

                if (acquired)
                {
                    ActiveTitle = string.Empty;
                    _gate.Release();
                }

                OnPropertyChanged(nameof(Summary));
            }
        }

        partial void OnActiveTitleChanged(string value) => OnPropertyChanged(nameof(Summary));

        partial void OnWaitingCountChanged(int value) => OnPropertyChanged(nameof(Summary));
    }
}
