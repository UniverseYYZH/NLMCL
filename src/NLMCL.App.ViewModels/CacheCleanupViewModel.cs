using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>缓存清理页的一行。</summary>
    public sealed partial class CacheCategoryItem : ObservableObject
    {
        public CacheCategoryItem(CacheCategory category)
        {
            Category = category;
        }

        public CacheCategory Category { get; }

        public string Id => Category.Id;

        public string Title => Category.Title;

        public string Description => Category.Description;

        public string SizeText => CacheCleanupViewModel.FormatBytes(Category.SizeBytes);

        public string CountText => Category.ItemCount + " 项";

        [ObservableProperty]
        private bool _isSelected = true;
    }

    /// <summary>
    /// 设置页的缓存清理。逻辑放在独立 ViewModel 里，避免继续把 MainViewModel 撑大。
    /// </summary>
    public sealed partial class CacheCleanupViewModel : ObservableObject
    {
        private readonly ICacheCleaner _cleaner;
        private readonly IDialogService? _dialogs;

        public CacheCleanupViewModel(ICacheCleaner cleaner, IDialogService? dialogs)
        {
            _cleaner = cleaner ?? throw new ArgumentNullException(nameof(cleaner));
            _dialogs = dialogs;
        }

        /// <summary>由 MainViewModel 在进入设置页时设置为当前游戏目录。</summary>
        public string GameDirectory { get; set; } = string.Empty;

        public ObservableCollection<CacheCategoryItem> Categories { get; } = new ObservableCollection<CacheCategoryItem>();

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = "进入设置页时会自动扫描。";

        [RelayCommand]
        public async Task RefreshAsync()
        {
            if (IsBusy)
            {
                return;
            }

            IsBusy = true;

            try
            {
                IReadOnlyList<CacheCategory> categories = await _cleaner
                    .ScanAsync(GameDirectory, CancellationToken.None)
                    .ConfigureAwait(true);

                Categories.Clear();

                foreach (CacheCategory category in categories)
                {
                    Categories.Add(new CacheCategoryItem(category));
                }

                long total = categories.Sum(item => item.SizeBytes);

                StatusText = categories.Count == 0
                    ? "没有发现可清理的缓存。"
                    : "发现 " + categories.Count + " 类，共 " + FormatBytes(total) + "。";
            }
            catch (Exception ex)
            {
                StatusText = "扫描缓存失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task CleanSelectedAsync()
        {
            List<CacheCategoryItem> selected = Categories.Where(item => item.IsSelected).ToList();

            if (selected.Count == 0)
            {
                StatusText = "请先勾选要清理的项。";
                return;
            }

            long total = selected.Sum(item => item.Category.SizeBytes);
            string names = string.Join("、", selected.Select(item => item.Title));

            if (_dialogs != null && !_dialogs.Confirm(
                    "清理缓存",
                    "将清理以下内容（共 " + FormatBytes(total) + "）：\n\n" + names +
                    "\n\n不会删除存档、mods、版本或资源库。继续吗？"))
            {
                return;
            }

            IsBusy = true;

            try
            {
                CacheCleanupResult result = await _cleaner
                    .CleanAsync(
                        GameDirectory,
                        selected.Select(item => item.Id).ToList(),
                        CancellationToken.None)
                    .ConfigureAwait(true);

                string message = "已清理 " + result.DeletedCount + " 项，释放 " + FormatBytes(result.FreedBytes) + "。";

                if (result.Errors.Count > 0)
                {
                    message += " 有 " + result.Errors.Count + " 项失败（可能被占用）。";
                }

                // RefreshAsync 自带 IsBusy 守卫，先释放再刷新。
                IsBusy = false;
                await RefreshAsync().ConfigureAwait(true);
                StatusText = message;
            }
            catch (Exception ex)
            {
                StatusText = "清理缓存失败：" + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
            {
                return bytes + " B";
            }

            double kb = bytes / 1024.0;

            if (kb < 1024)
            {
                return kb.ToString("0.0") + " KB";
            }

            double mb = kb / 1024.0;

            if (mb < 1024)
            {
                return mb.ToString("0.0") + " MB";
            }

            return (mb / 1024.0).ToString("0.00") + " GB";
        }
    }
}
