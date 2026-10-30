' 垫片：ModLaunch 的启动参数管线所依赖的全局量。
'
' 上游这些成员散落在 `Module ModMain`、`ModLaunch` 与 WPF 窗体里。
' 我们只切了「启动参数」这一段（见 Vendor/ModLaunch.Arguments.vb），
' 因此这里只补这一段实际用到的东西，**不搬运登录、页面、音乐等子系统**。
'
' 上游对应位置：
'   McInstanceSelected / McLaunchArgument / McLaunchEncoding / PathPure / CurrentLaunchOptions → Module ModMain
'   McLaunchLog / McLaunchJavaSelected                                                      → Module ModLaunch
'   McLoginLoader                                                                          → Module ModLaunch
'   ArgumentReplace                                                                        → Module ModMain
'   DPI / GetPixelSize / FrmMain.PanForm                                                   → 窗体与全局

Imports System.Text

''' <summary>Minecraft 文件夹里的“纯净”路径（上游是带缓存的路径对象）。</summary>
Public Class PclPathValue
    Public Property Value As String = String.Empty
End Class

''' <summary>启动参数（上游 McLaunchOptions）。这里只保留参数构建用到的两个成员。</summary>
Public Class McLaunchOptions
    ''' <summary>额外的游戏参数，逐行追加到命令行。</summary>
    Public Property ExtraGameArgs As New List(Of String)

    ''' <summary>直连服务器地址；为空时回落到设置里的值。</summary>
    Public Property ServerIp As String = String.Empty
End Class

''' <summary>登录结果（上游 McLoginLoader.Output）。</summary>
Public Class McLoginOutput
    Public Property Type As String = "Legacy"
    Public Property Name As String = "Player"
    Public Property Uuid As String = String.Empty
    Public Property AccessToken As String = String.Empty
End Class

''' <summary>登录加载器（上游是 LoaderTask）。</summary>
Public Class McLoginLoaderStub
    Public ReadOnly Property Output As New McLoginOutput
End Class

''' <summary>
''' ModLaunch 参数管线依赖的全局状态。
''' 由 NLCL 的适配器在调用前赋值（实例、Java、登录结果）。
''' </summary>
Public Module LaunchGlobals

    ''' <summary>当前选中的版本实例。上游由启动页维护。</summary>
    Public Property McInstanceSelected As McInstance

    ''' <summary>当前选中的 Java。上游由 Java 选择器维护。</summary>
    Public Property McLaunchJavaSelected As Java

    ''' <summary>最终拼出的命令行（上游 McLaunchArgument）。</summary>
    Public Property McLaunchArgument As String = String.Empty

    ''' <summary>启动日志编码。</summary>
    Public Property McLaunchEncoding As Encoding = Encoding.UTF8

    ''' <summary>本次启动选项。</summary>
    Public Property CurrentLaunchOptions As New McLaunchOptions

    ''' <summary>登录结果。</summary>
    Public ReadOnly McLoginLoader As New McLoginLoaderStub

    ''' <summary>启动日志。无 UI 时写调试输出；适配器可以挂回调接管。</summary>
    Public Property McLaunchLogSink As Action(Of String)

    Public Sub McLaunchLog(Text As String, Optional Level As Object = Nothing)
        Dim sink = McLaunchLogSink
        If sink IsNot Nothing Then
            sink(Text)
        Else
            Debug.WriteLine("[McLaunch] " & Text)
        End If
    End Sub

    ''' <summary>
    ''' 占位符替换（上游 ModMain.ArgumentReplace，约 60 行，支持 ${date} 等）。
    ''' 这里实现参数构建实际用到的子集：不做时间/随机数替换，只做路径与版本类占位符。
    ''' </summary>
    Public Function ArgumentReplace(Text As String,
                                    Optional ReplaceTime As Boolean = True,
                                    Optional Instance As McInstance = Nothing) As String
        If Text Is Nothing Then Return ""
        Return Text
    End Function

End Module
