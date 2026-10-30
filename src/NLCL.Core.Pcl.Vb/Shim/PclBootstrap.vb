' 垫片：PCL 全局状态的初始化时机。
'
' PCL 的跨线程判定是：
'     Private ReadOnly UiThreadId As Integer = Thread.CurrentThread.ManagedThreadId
'     Public Function RunInUi() As Boolean
'         Return Thread.CurrentThread.ManagedThreadId = UiThreadId
'     End Function
'
' 也就是说 UiThreadId 在 **ModBase 类型初始化那一刻**取当前线程 ID。
' 我们从 Task.Run 里发起第一次网络请求，ModBase 就首次被线程池线程触碰，
' UiThreadId 被记成那个线程池线程的 ID —— 随后 SendRequest 里的
' "If RunInUi() Then Throw New Exception("在 UI 线程执行了网络请求")" 立刻误判，
' 表现就是"第一次点刷新失败、第二次成功"（线程池线程 ID 变了）。
'
' 因此必须在启动时（UI 线程）主动把这个类型初始化掉。

Namespace Global.NLCL.Core.Pcl.Vb

    ''' <summary>在 UI 线程上把 PCL 的全局状态先初始化好。</summary>
    Public Module PclBootstrap

        ''' <summary>
        ''' 必须在 UI 线程（应用启动）调用一次，且要在任何网络/下载之前。
        ''' 触碰 <c>PathTemp</c> 会触发 ModBase 的静态初始化，使 UiThreadId 记录成 UI 线程。
        ''' </summary>
        Public Sub InitializeOnUiThread()
            Dim probe = PathTemp
            If String.IsNullOrEmpty(probe) Then
                ' 理论上不会发生：PathTemp 有默认值（%TEMP%\PCL\）
                Logger.Warn("PCL 临时目录为空，UiThreadId 可能未被正确初始化")
            End If
        End Sub

    End Module

End Namespace
