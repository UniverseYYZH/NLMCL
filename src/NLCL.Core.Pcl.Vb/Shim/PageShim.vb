' 垫片：实例设置页（只补启动参数管线用到的那一个成员）。
'
' 上游 `PageInstanceSetup` 是 WPF 页面，负责实例级的内存、Java、启动参数等设置。
' 启动参数管线只调用 `GetRam(Instance)` 取该实例分配的内存。
'
' 上游：Plain Craft Launcher 2\Pages\PageInstance\PageInstanceSetup.xaml.vb

''' <summary>实例设置页的占位。</summary>
Public Class PageInstanceSetup

    ''' <summary>
    ''' 本次启动要求的内存（MB），由 NLCL 的启动请求写入，0 表示没指定。
    ''' 上游这里读的是实例设置页的滑块；我们没有那个页面，所以由适配器在构建命令行前赋值。
    ''' </summary>
    Public Shared Property RamMbOverride As Integer = 0

    ''' <summary>
    ''' 该实例分配的内存（GB）。上游读实例级配置与滑块值；
    ''' 这里优先用本次启动请求里的值，其次回落到设置，读不到就给 2 GB。
    ''' </summary>
    Public Shared Function GetRam(Instance As McInstance) As Double
        If RamMbOverride > 0 Then Return RamMbOverride / 1024.0

        Dim megabytes = Settings.[Get](Of Integer)("LaunchArgumentRam", Instance)
        If megabytes <= 0 Then megabytes = 2048
        Return megabytes / 1024.0
    End Function

End Class
