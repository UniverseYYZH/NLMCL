using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NLMCL.App.ViewModels
{
    /// <summary>下载队列中的一项：等待中的任务可以取消，进行中的任务由原页面取消。</summary>
    public sealed partial class DownloadQueueItem : ObservableObject
    {
        private readonly Action _cancelWaiting;

        public DownloadQueueItem(string title, Action cancelWaiting)
        {
            Title = title ?? string.Empty;
            _cancelWaiting = cancelWaiting ?? throw new ArgumentNullException(nameof(cancelWaiting));
        }

        public string Title { get; }

        [ObservableProperty]
        private string _status = "等待中";

        [ObservableProperty]
        private bool _isActive;

        public bool CanCancel => !IsActive;

        partial void OnIsActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCancel));
        }

        [RelayCommand]
        private void Cancel()
        {
            if (!IsActive)
            {
                _cancelWaiting();
            }
        }
    }

    /// <summary>
    /// 全启动器共享的下载闸门：同一时间只跑一个安装任务，其余排队。
    /// 只负责并发与状态文本，不改变各页面自己的进度模型。
    /// </summary>
    public sealed partial class DownloadQueue : ObservableObject
    {
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private int _waiting;

        public DownloadQueue()
        {
            Items.CollectionChanged += (_, __) => OnPropertyChanged(nameof(HasItems));
        }

        public ObservableCollection<DownloadQueueItem> Items { get; } = new ObservableCollection<DownloadQueueItem>();

        public bool HasItems => Items.Count > 0;

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

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            DownloadQueueItem item = new DownloadQueueItem(title, linked.Cancel);
            Items.Add(item);

            Interlocked.Increment(ref _waiting);
            WaitingCount = _waiting;
            OnPropertyChanged(nameof(Summary));

            bool acquired = false;

            try
            {
                await _gate.WaitAsync(linked.Token).ConfigureAwait(true);
                acquired = true;
                Interlocked.Decrement(ref _waiting);
                WaitingCount = _waiting;
                item.IsActive = true;
                item.Status = "进行中";
                ActiveTitle = title ?? string.Empty;
                OnPropertyChanged(nameof(Summary));

                await work().ConfigureAwait(true);

                item.Status = "已完成";
            }
            catch (OperationCanceledException)
            {
                item.Status = "已取消";
                throw;
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

                Items.Remove(item);
                OnPropertyChanged(nameof(Summary));
            }
        }

        partial void OnActiveTitleChanged(string value) => OnPropertyChanged(nameof(Summary));

        partial void OnWaitingCountChanged(int value) => OnPropertyChanged(nameof(Summary));
    }
}
