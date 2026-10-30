' 垫片：PCL 的虚拟化列表控件（纯 UI）。
'
' 上游 `McInstance.ToListItem()` 与 `ResourceVersion.ToListItem()` 会为每条记录构造一个
' WPF 列表项供 PCL 的虚拟化列表显示。NLCL 的前端是自研 WPF，不需要这套控件，
' 但这两个方法是公开 API，编译时必须让类型可见。
'
' 本文件给出**最小可编译**实现：不含虚拟化、渲染或交互逻辑。
' 运行时若真的调用 ToListItem，只会得到空壳对象——M3-B 的安装流程不会走这条路。
'
' 注意 `ClickEventHandler`：VB 会为 `Public Event Click(...)` 自动生成同名嵌套委托，
' 因此上游 `MyListItem.ClickEventHandler` / `MyIconButton.ClickEventHandler` 无需显式声明。
'
' 上游：Plain Craft Launcher 2\Controls\MyListItem.xaml.vb
'       Plain Craft Launcher 2\Controls\MyIconButton.xaml.vb
'       Plain Craft Launcher 2\Controls\MyVirtualizingElement.vb

Imports System.Windows
Imports System.Windows.Documents

''' <summary>图标按钮的最小占位实现。</summary>
Public Class MyIconButton
    Inherits FrameworkElement

    Public Property Logo As String

    ' ToolTip 由 FrameworkElement 提供（类型为 Object），无需重复声明。

    ''' <summary>VB 会据此生成嵌套委托 <c>MyIconButton.ClickEventHandler</c>。</summary>
    Public Event Click(Sender As Object, e As EventArgs)

End Class

''' <summary>列表项的最小占位实现（上游是可点击的 WPF 自定义控件）。</summary>
Public Class MyListItem
    Inherits FrameworkElement

    ''' <summary>勾选样式。</summary>
    Public Enum CheckType
        Uncheckable = 0
        Clickable = 1
        CheckBox = 2
    End Enum

    Public Property Title As String
    Public Property Info As String
    Public Property Logo As String
    Public Property Type As CheckType = CheckType.Uncheckable
    Public Property ContentHandler As Action(Of MyListItem, EventArgs)

    ''' <summary>右侧图标按钮。</summary>
    Public Property Buttons As MyIconButton() = New MyIconButton() {}

    ''' <summary>标题行内元素。上游用的是 InlineCollection。</summary>
    Public ReadOnly Property Inlines As New List(Of Inline)

    ''' <summary>VB 会据此生成嵌套委托 <c>MyListItem.ClickEventHandler</c>。</summary>
    Public Event Click(Sender As Object, e As EventArgs)

End Class

''' <summary>虚拟化列表元素的最小占位实现。</summary>
Public Class MyVirtualizingElement(Of T As FrameworkElement)

    Public Sub New(factory As Func(Of T))
        Me.Factory = factory
    End Sub

    Public ReadOnly Property Factory As Func(Of T)

    ''' <summary>上游用于设定行高（<c>With {.Height = 42}</c>）。</summary>
    Public Property Height As Double

End Class

