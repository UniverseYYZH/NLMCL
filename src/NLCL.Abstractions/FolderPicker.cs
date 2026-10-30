namespace NLCL.Abstractions
{
    /// <summary>
    /// 选择文件夹。视图模型不引用 WPF/WinForms，因此把对话框抽象出去，
    /// 由 App 层用 WinForms 的 FolderBrowserDialog 实现。
    /// </summary>
    public interface IFolderPicker
    {
        /// <summary>弹出选择框；用户取消返回 null。</summary>
        string? PickFolder(string description, string? initialDirectory);
    }
}
