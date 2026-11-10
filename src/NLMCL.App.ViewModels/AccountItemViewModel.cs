using CommunityToolkit.Mvvm.ComponentModel;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>账号切换列表的一行：账号 + 头像 + 是否为当前账号。</summary>
    public sealed partial class AccountItemViewModel : ObservableObject
    {
        public AccountItemViewModel(Account account)
        {
            Account = account ?? throw new System.ArgumentNullException(nameof(account));
        }

        public Account Account { get; }

        public string Name => Account.Name;

        public string TypeLabel => Account.TypeLabel;

        public string LastUsedLabel => Account.LastUsedLabel;

        [ObservableProperty]
        private string? _avatarPath;

        /// <summary>内存头像，避免账号列表再依赖临时/缓存文件。</summary>
        [ObservableProperty]
        private byte[]? _avatarPng;

        public bool HasAvatar => !string.IsNullOrEmpty(AvatarPath) ||
                                 (AvatarPng != null && AvatarPng.Length > 0);

        partial void OnAvatarPathChanged(string? value)
        {
            OnPropertyChanged(nameof(HasAvatar));
        }

        partial void OnAvatarPngChanged(byte[]? value)
        {
            OnPropertyChanged(nameof(HasAvatar));
        }

        [ObservableProperty]
        private bool _isCurrent;
    }
}
