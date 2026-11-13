using System;
using System.Collections.ObjectModel;
using System.Linq;
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
        private readonly Action? _dismiss;

        public DownloadQueueItem(string title, Action cancelWaiting, Action? dismiss = null)
        {
            Title = title ?? string.Empty;
            _cancelWaiting = cancelWaiting ?? throw new ArgumentNullException(nameof(cancelWaiting));
            _dismiss = dismiss;
        }

        public string Title { get; }

        [ObservableProperty]
        private string _status = "等待中";

        [ObservableProperty]
        private bool _isActive;

        [ObservableProperty]
        private bool _isPaused;

        [ObservableProperty]
        private bool _isFailed;

        public bool CanCancel => !IsActive && !IsPaused && !IsFailed;

        public bool CanDismiss => IsPaused || IsFailed;

        partial void OnIsActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCancel));
        }

        partial void OnIsPausedChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanDismiss));
        }

        partial void OnIsFailedChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCancel));
            OnPropertyChanged(nameof(CanDismiss));
        }

        public void MarkPaused()
        {
            IsActive = false;
            IsPaused = true;
            Status = "已暂停";
        }

        public void MarkFailed(string message)
        {
            IsActive = false;
            IsFailed = true;
            Status = string.IsNullOrWhiteSpace(message) ? "失败" : "失败：" + message;
        }

        [RelayCommand]
        private void Cancel()
        {
            if (CanCancel)
            {
                _cancelWaiting();
            }
        }

        [RelayCommand]
        private void Dismiss() => _dismiss?.Invoke();
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

                int paused = Items.Count(item => item.IsPaused);
                int failed = Items.Count(item => item.IsFailed);

                if (!string.IsNullOrEmpty(ActiveTitle))
                {
                    return ActiveTitle
                        + (paused > 0 ? " · " + paused + " 个已暂停" : string.Empty)
                        + (failed > 0 ? " · " + failed + " 个失败" : string.Empty);
                }

                if (paused > 0)
                {
                    return "已暂停 " + paused + " 个任务";
                }

                if (failed > 0)
                {
                    return failed + " 个任务失败";
                }

                return WaitingCount > 0 ? "排队 " + WaitingCount + " 个任务" : string.Empty;
            }
        }

        public async Task RunAsync(
            string title,
            Func<Task> work,
            CancellationToken cancellationToken,
            Action<DownloadQueueItem>? started = null,
            Action? dismiss = null,
            Func<bool>? isPaused = null)
        {
            if (work == null)
            {
                throw new ArgumentNullException(nameof(work));
            }

            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            DownloadQueueItem item = new DownloadQueueItem(title, linked.Cancel, dismiss);
            Items.Add(item);

            // 拥有者需要从排队阶段就能清除/恢复这一项；若等到真正开始才回调，
            // “排队中暂停”会把 item 留在面板里，而 ViewModel 拿不到引用。
            started?.Invoke(item);

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
                if (isPaused?.Invoke() == true)
                {
                    item.MarkPaused();
                }
                else
                {
                    item.Status = "已取消";
                }

                throw;
            }
            catch (Exception ex)
            {
                item.MarkFailed(ex.Message);
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

                // 有清除入口时，暂停/失败的任务留在队列里，用户明确“清除”后才移除。
                bool keep = item.IsPaused || (item.IsFailed && dismiss != null);
                if (!keep)
                {
                    Items.Remove(item);
                }

                OnPropertyChanged(nameof(Summary));
            }
        }

        /// <summary>移除一个已暂停/失败的任务；由拥有它的视图模型在清理后调用。</summary>
        public void Dismiss(DownloadQueueItem? item)
        {
            if (item != null)
            {
                Items.Remove(item);
                OnPropertyChanged(nameof(Summary));
            }
        }

        partial void OnActiveTitleChanged(string value) => OnPropertyChanged(nameof(Summary));

        partial void OnWaitingCountChanged(int value) => OnPropertyChanged(nameof(Summary));
    }
}
