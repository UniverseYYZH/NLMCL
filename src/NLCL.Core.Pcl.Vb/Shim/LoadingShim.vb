' 垫片：PCL 把 ILoadingTrigger / MyLoadingState 定义在 WPF 控件 Controls\MyLoading.xaml.vb 里，
' 属于 UI 类型漏进了加载框架。这里按上游原样复刻最小契约（枚举值与接口签名逐项照抄），
' 不含任何渲染逻辑，因此 VB 类库无需引入 XAML 编译即可复用 PCL 的 LoaderBase。
'
' 上游：Plain Craft Launcher 2\Controls\MyLoading.xaml.vb  L3 / L99 / L251 / L258
' 差异：枚举按上游嵌套在 MyLoading 内（ModLoader.vb 以 MyLoading.MyLoadingState 引用），
'       因此本文件内所有引用都写全限定名。

Public Partial Class MyLoading

    ''' <summary>加载状态。</summary>
    Public Enum MyLoadingState
        Unloaded = -1
        Run = 0
        [Stop] = 1
        [Error] = 2
    End Enum

End Class

''' <summary>
''' 可被 LoaderBase 观察的加载触发器契约。
''' </summary>
Public Interface ILoadingTrigger
    ReadOnly Property IsLoader As Boolean
    Property LoadingState As MyLoading.MyLoadingState

    Event LoadingStateChanged(NewState As MyLoading.MyLoadingState, OldState As MyLoading.MyLoadingState)
    Event ProgressChanged(NewProgress As Double, OldProgress As Double)
End Interface

''' <summary>
''' 不依赖任何 UI 的 <see cref="ILoadingTrigger"/> 实现——正是它让 LoaderBase 可以脱离 WPF 使用。
''' </summary>
Public Class MyLoadingStateSimulator
    Implements ILoadingTrigger

    Private Property _LoadingState As MyLoading.MyLoadingState = MyLoading.MyLoadingState.Unloaded

    Public Property LoadingState As MyLoading.MyLoadingState Implements ILoadingTrigger.LoadingState
        Get
            Return _LoadingState
        End Get
        Set(value As MyLoading.MyLoadingState)
            If _LoadingState = value Then Return
            Dim OldState = _LoadingState
            _LoadingState = value
            RaiseEvent LoadingStateChanged(value, OldState)
        End Set
    End Property

    Public ReadOnly Property IsLoader As Boolean = False Implements ILoadingTrigger.IsLoader

    Public Event LoadingStateChanged(NewState As MyLoading.MyLoadingState, OldState As MyLoading.MyLoadingState) Implements ILoadingTrigger.LoadingStateChanged
    Public Event ProgressChanged(NewProgress As Double, OldProgress As Double) Implements ILoadingTrigger.ProgressChanged

End Class
