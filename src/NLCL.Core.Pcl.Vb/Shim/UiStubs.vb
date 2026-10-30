' 垫片：PCL 的窗体与全局 UI 占位。
'
' 上游 ModMain.vb 里有：
'     Public FrmMain As FormMain          ' 注意类名是 FormMain，字段名才是 FrmMain
'     Public FrmSpeedLeft As ...          ' 下载速度面板
'
' 下载栈只用到它们的极少成员。这里让 FormMain **真的继承 Window**——因为
' ModBase.vb:1161 会调用 element.TransformToAncestor(FrmMain)，参数必须是 Visual。
' 继承 Window 后 ActualWidth / ActualHeight / TaskbarItemInfo 都免费得到，
' 同时不需要任何 XAML 编译。
'
' 上游引用点：
'   ModBase.vb   L1162  FrmMain.ActualWidth / ActualHeight
'   ModLoader.vb L557/641/713  FrmMain.BtnExtraDownload.ShowRefresh()
'   ModLoader.vb L700   FrmMain.BtnExtraDownload.Progress = …
'   ModLoader.vb L708   FrmMain.TaskbarItemInfo.ProgressValue = …
'   ModLoader.vb L712   FrmMain.TaskbarItemInfo.ProgressState = …
'   ModLoader.vb L677/689  FrmSpeedLeft.TaskRemove / TaskRefresh

''' <summary>下载按钮的占位：只需要进度与刷新。</summary>
Public Class PclDownloadButtonStub
    Public Property Progress As Double

    ''' <summary>上游用于刷新按钮上的转圈动画，这里无事可做。</summary>
    Public Sub ShowRefresh()
    End Sub
End Class

''' <summary>主窗口的占位（继承自 WPF 的 Window，但从不 Show）。</summary>
Public Class FormMain
    Inherits System.Windows.Window

    ''' <summary>下载按钮（上游是自定义控件）。</summary>
    Public ReadOnly Property BtnExtraDownload As New PclDownloadButtonStub

    ''' <summary>主内容面板（上游用于取"与启动器窗口一致"的游戏窗口尺寸）。</summary>
    Public ReadOnly Property PanForm As New PclPanelStub

    ''' <summary>页面类型（上游是 FormMain 内的嵌套枚举）。</summary>
    Public Enum PageType
        Download = 0
        InstanceSetup = 1
    End Enum

    ''' <summary>页面子类型。</summary>
    Public Enum PageSubType
        DownloadInstall = 0
        InstanceSetup = 1
    End Enum

    ''' <summary>切换页面。NLCL 的界面自研，这里不做事。</summary>
    Public Sub PageChange(Type As PageType, SubType As PageSubType, Optional Extra As Object = Nothing)
    End Sub

End Class

''' <summary>面板的占位：只需要客户区尺寸。</summary>
Public Class PclPanelStub
    Public ReadOnly Property ActualWidth As Double = 1024
    Public ReadOnly Property ActualHeight As Double = 640
End Class

''' <summary>下载速度面板的占位类型。</summary>
Public Class FrmSpeedLeftStub
    ''' <summary>把一个加载任务从速度面板上移除。</summary>
    Public Sub TaskRemove(Loader As LoaderBase)
    End Sub

    ''' <summary>刷新速度面板上的任务条目。</summary>
    Public Sub TaskRefresh(Task As Object)
    End Sub
End Class

''' <summary>Minecraft 文件夹条目（上游是 ModMinecraft.McFolder）。</summary>
Public Class PclMcFolderStub
    Public Property Name As String
    Public Property Location As String
End Class

''' <summary>提示条的类型。</summary>
Public Enum HintType
    Blue = 0
    Green = 1
    Yellow = 2
    Red = 3
End Enum

''' <summary>
''' 上游散落在 <c>Module ModMain</c> 里的全局对象。
''' 为了两个字段去搬整个 ModMain（1084 行、119 处窗体引用）不划算，这里统一承载。
''' </summary>
Public Module PclUiStubs

    Public FrmMain As FormMain = New FormMain
    Public FrmSpeedLeft As FrmSpeedLeftStub = Nothing

    ''' <summary>
    ''' 当前选中的 Minecraft 文件夹（PCL 约定：以反斜杠结尾）。
    ''' 上游由 ModMinecraft 维护；这里作为可写全局，由适配器在开始操作前设置。
    ''' </summary>
    Public Property McFolderSelected As String =
        IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft") & "\"

    ''' <summary>愚人节版本的显示名。上游做了花名映射，这里原样返回。</summary>
    Public Function GetMcFoolName(Name As String) As String
        Return Name
    End Function

    ''' <summary>已配置的 Minecraft 文件夹列表（上游由 ModMinecraft 维护）。</summary>
    Public ReadOnly McFolderList As New List(Of PclMcFolderStub)

    ''' <summary>
    ''' 模态消息框。无 UI 时退化为写调试输出，并固定返回 1（= 用户点了第一个按钮）。
    ''' 参数与上游对齐：上游最多可带 3 个按钮 + 各自的回调。
    ''' </summary>
    Public Function MyMsgBox(Message As String,
                             Optional Title As String = "",
                             Optional Button1 As String = "",
                             Optional Button2 As String = "",
                             Optional Button3 As String = "",
                             Optional Button1Action As Action = Nothing,
                             Optional Button2Action As Action = Nothing,
                             Optional Button3Action As Action = Nothing,
                             Optional IsWarn As Boolean = False,
                             Optional ForceWait As Boolean = False) As Integer
        Debug.WriteLine($"[MyMsgBox:{Title}{(If(IsWarn, " (warn)", ""))}] {Message}")
        Return 1
    End Function

    ''' <summary>提示条。无 UI 时退化为写调试输出。</summary>
    Public Sub Hint(Message As String, Optional Type As HintType = HintType.Blue)
        Debug.WriteLine("[Hint] " & Message)
    End Sub

    ''' <summary>匿名数据上报。NLCL 阶段一律丢弃——不向第三方回传任何内容。</summary>
    Public Sub Telemetry(EventName As String, ParamArray Data() As Object)
    End Sub

End Module

''' <summary>
''' 上游的设置服务（ModMain.vb）。下载栈只用它读取"是否为预览版"之类的开关，
''' 这里给出最小实现，全部走 <see cref="Settings"/>。
''' </summary>
Public Class SettingService
    Public Shared ReadOnly Property GetSettings As New Dictionary(Of String, Object)

    Public Shared Function [Get](Of T)(Key As String) As T
        Return Settings.[Get](Of T)(Key)
    End Function

    Public Shared Sub SetValue(Of T)(Key As String, Value As T)
        Settings.[Set](Key, Value, Nothing)
    End Sub

    ''' <summary>上游会把控件上的设置写回设置存储。无 UI 时无事可做。</summary>
    Public Shared Sub RefreshSettings(Control As Object)
    End Sub
End Class

''' <summary>登录类型（上游 ModLaunch 里的枚举）。</summary>
Public Enum McLoginType
    Legacy = 0
    Nide = 1
    Auth = 2
    Ms = 3
End Enum

''' <summary>Java 下拉框的占位：只需要 Enabled。</summary>
Public Class PclComboStub
    Public Property IsEnabled As Boolean = True
End Class

''' <summary>启动设置窗口的占位（类名用 Form*，字段名用 Frm*——与上游一致，VB 大小写不敏感）。</summary>
Public Class FormSetupLaunch
    Public Sub UpdateJavaList()
    End Sub
End Class

''' <summary>实例设置窗口的占位。</summary>
Public Class FormInstanceSetup
    Public Sub UpdateJavaList()
    End Sub

    Public ReadOnly Property ComboArgumentJava As New PclComboStub
    Public ReadOnly Property ComboArgumentJavaSelect As New PclComboStub
End Class

''' <summary>实例列表左栏的占位。</summary>
Public Class PageInstanceLeft
    Public Shared Property Instance As McInstance

    Public Shared Sub ReloadCurrentJava()
    End Sub
End Class

''' <summary>上游在 Module ModMain 里声明的窗口字段。</summary>
Public Module PclWindowStubs
    ''' <summary>可能为 Nothing——上游也按可空使用（<c>FrmInstanceSetup?.UpdateJavaList()</c>）。</summary>
    Public FrmSetupLaunch As FormSetupLaunch = Nothing
    Public FrmInstanceSetup As FormInstanceSetup = Nothing
End Module

Public Module PclSettingService
    Public ReadOnly SettingService As New SettingService
End Module
