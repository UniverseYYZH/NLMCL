using System;

namespace NLMCL.Abstractions
{
    /// <summary>
    /// 需要用户确认的操作（删除快照这类不可撤销的动作）。
    /// 抽成端口是为了让 ViewModel 可单测——WPF 的 MessageBox 不能出现在 ViewModel 里。
    /// </summary>
    public interface IDialogService
    {
        /// <summary>是 → 继续；否 → 取消。</summary>
        bool Confirm(string title, string message);

        /// <summary>输入一行文本；取消返回 null。</summary>
        string? Prompt(string title, string message, string initialValue);
    }
}
