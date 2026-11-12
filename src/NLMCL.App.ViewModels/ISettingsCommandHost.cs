using System.Threading.Tasks;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// 设置页需要由宿主执行的操作。
    /// SettingsViewModel 通过该接口触发目录、诊断、重置等跨页面行为，
    /// 不再直接依赖 MainViewModel 的具体实现。
    /// </summary>
    public interface ISettingsCommandHost
    {
        Task PickGameDirectoryAsync();

        Task RemoveGameDirectoryAsync();

        void OpenGameDirectory();

        void OpenLogsFolder();

        void CreateDiagnosticBundle();

        Task ResetAppearanceDefaultsAsync();

        Task ResetDownloadDefaultsAsync();

        Task ResetLaunchDefaultsAsync();

        Task ResetAllDefaultsAsync();
    }
}
