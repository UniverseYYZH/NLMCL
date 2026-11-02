using System.Windows.Controls;

namespace NLMCL.App.Views
{
    /// <summary>
    /// 「导入资源」页的壳。
    ///
    /// 没有这个文件时 XAML 仍然能编译（WPF 会生成 partial 类），
    /// 但生成的构造函数不会调用 InitializeComponent，页面会是"可见但空白"。
    /// </summary>
    public partial class ResourcePage : UserControl
    {
        public ResourcePage()
        {
            InitializeComponent();
        }
    }
}
