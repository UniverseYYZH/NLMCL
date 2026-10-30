namespace NLCL.Abstractions
{
    /// <summary>
    /// 剪贴板端口。视图模型不引用 WPF，因此把复制动作抽象出去，
    /// 由 App 层用 <c>System.Windows.Clipboard</c> 实现。
    /// </summary>
    public interface IClipboard
    {
        void SetText(string text);
    }
}
