using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 方块宁理页的展示状态：服档案与进服检查。
    /// 启动/安装仍由 MainViewModel 编排，这里只负责页面数据，避免继续把
    /// 所有页面的集合与属性堆进主视图模型。
    /// </summary>
    public sealed partial class NingliViewModel : ObservableObject
    {
        public ObservableCollection<ServerProfile> ServerProfiles { get; } = new();
        public ObservableCollection<ServerJoinCheck> JoinChecks { get; } = new();

        public ServerProfile? PrimaryProfile => ServerProfiles.FirstOrDefault(p => p.RequiresMods)
            ?? ServerProfiles.FirstOrDefault();

        public ServerProfile? VanillaProfile => ServerProfiles.FirstOrDefault(p => !p.RequiresMods);

        [ObservableProperty]
        private bool _hasPrimaryProfile;

        [ObservableProperty]
        private bool _hasVanillaProfile;

        public int VisibleServerProfileCount =>
            (HasPrimaryProfile ? 1 : 0) + (HasVanillaProfile ? 1 : 0);

        partial void OnHasPrimaryProfileChanged(bool value)
        {
            OnPropertyChanged(nameof(VisibleServerProfileCount));
        }

        partial void OnHasVanillaProfileChanged(bool value)
        {
            OnPropertyChanged(nameof(VisibleServerProfileCount));
        }

        /// <summary>替换档案列表，并刷新卡片可见性。</summary>
        public void SetProfiles(IEnumerable<ServerProfile> profiles)
        {
            ServerProfiles.Clear();
            foreach (ServerProfile profile in profiles)
            {
                ServerProfiles.Add(profile);
            }

            HasPrimaryProfile = PrimaryProfile != null;
            HasVanillaProfile = VanillaProfile != null;
            OnPropertyChanged(nameof(PrimaryProfile));
            OnPropertyChanged(nameof(VanillaProfile));
        }

        /// <summary>进服检查重算后，通知主/还原服引用也变了。</summary>
        public void NotifyProfilesChanged()
        {
            OnPropertyChanged(nameof(PrimaryProfile));
            OnPropertyChanged(nameof(VanillaProfile));
        }
    }
}
