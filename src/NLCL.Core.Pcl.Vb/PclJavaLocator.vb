' NLCL 的 Java 探测适配器：复用 PCL 的 JavaUtils（PCLCS 的 C# 部分）。
'
' 上游链路：
'   JavaUtils.RefreshListAsync()  →  扫描候选目录、逐个执行 java -XshowSettings 探测版本、
'                                    按优先级排序、扣掉用户手动删除过的项
'   结果写入 Configs.JavaList（PCLCS 的配置容器，已 vendor）
'
' 因此这里不需要自己实现扫描或版本解析——只负责调用并把结果映射成 NLCL 的 DTO。

Imports System.Threading
Imports System.Threading.Tasks
Imports NLCL.Abstractions

Namespace Global.NLCL.Core.Pcl.Vb

    ''' <summary><see cref="IJavaLocator"/> 的 PCL 实现。</summary>
    Public NotInheritable Class PclJavaLocator
        Implements IJavaLocator

        Public Function FindInstalledAsync(ct As CancellationToken) As Task(Of IReadOnlyList(Of JavaRuntime)) _
            Implements IJavaLocator.FindInstalledAsync

            Return Task.Run(Of IReadOnlyList(Of JavaRuntime))(
                Function()
                    ct.ThrowIfCancellationRequested()

                    ' RefreshListAsync 会把结果写回 Configs.JavaList。
                    ' 若该配置尚未初始化，先放一个空列表，避免上游内部的空引用。
                    If Configs.JavaList.Get() Is Nothing Then
                        Configs.JavaList.Set(New List(Of Java))
                    End If

                    JavaUtils.RefreshListAsync(ct, Nothing).GetAwaiter().GetResult()
                    ct.ThrowIfCancellationRequested()

                    Dim found = Configs.JavaList.Get()
                    If found Is Nothing Then Return CType(New List(Of JavaRuntime), IReadOnlyList(Of JavaRuntime))

                    Dim result As New List(Of JavaRuntime)
                    For Each java In found
                        result.Add(New JavaRuntime(java.Folder, java.Version))
                    Next

                    Return CType(result, IReadOnlyList(Of JavaRuntime))
                End Function, ct)
        End Function

    End Class

End Namespace
