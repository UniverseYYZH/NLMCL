' 在专用 STA 线程上执行 PCL 的启动参数构建。
'
' 为什么需要它：PCL 的垫片（PclUiStubs）里 `FrmMain` 继承自 WPF 的 Window，
' 而 Window 的构造**要求 STA 线程**。我们的启动流程在线程池上跑（Task.Run），
' 那是 MTA，一碰就抛 "调用线程必须为 STA"。
'
' 界面线程本身是 STA，所以这不是"PCL 只能在 UI 线程用"，而是
' "别在线程池里创建 WPF 对象"。这里开一个短命的 STA 线程把这段包起来。

Imports System.Threading

Friend Module StaRunner

    ''' <summary>
    ''' 在新 STA 线程上执行并等待结果；异常原样抛出。
    ''' 带超时：PCL 的部分代码会等 Dispatcher 消息泵，没有泵的线程上会永远卡住——
    ''' 宁可报超时，也不要让界面永远"正在启动"。
    ''' </summary>
    Public Function Run(Of T)(Work As Func(Of T)) As T
        Return Run(Of T)(Work, 30)
    End Function

    Public Function Run(Of T)(Work As Func(Of T), TimeoutSeconds As Integer) As T
        Dim result As T = Nothing
        Dim failure As Exception = Nothing

        Dim worker As New Thread(
            Sub()
                Try
                    result = Work()
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)

        worker.IsBackground = True
        worker.SetApartmentState(ApartmentState.STA)
        worker.Start()

        If Not worker.Join(TimeSpan.FromSeconds(TimeoutSeconds)) Then
            Throw New TimeoutException(
                $"启动参数构建超时（{TimeoutSeconds} 秒）。PCL 的某些代码会等待 Dispatcher 消息泵，没有泵的线程上会卡住。")
        End If

        If failure IsNot Nothing Then
            Throw failure
        End If

        Return result
    End Function

End Module
