' NLCL 的下载适配器：把 PCL 的 ModNet 暴露成 NLCL.Abstractions.IDownloadService。
'
' 走的是 ModNet 的 NetDownloadByClient / NetRequestByClient——这两个入口不依赖 LoaderBase，
' 因此无需 Loader 框架即可用。等 M3 正式接入 ModDownload（安装流程）后，
' 进度上报会换成带 LoaderBase 的重载。

Imports System.IO
Imports System.Threading
Imports System.Threading.Tasks
Imports NLCL.Abstractions

Namespace Global.NLCL.Core.Pcl.Vb

    ''' <summary>
    ''' <see cref="IDownloadService"/> 的 PCL 实现。
    ''' </summary>
    Public NotInheritable Class PclDownloadService
        Implements IDownloadService

        ''' <summary>
        ''' 下载到本地文件。父目录不存在时自动创建。
        ''' </summary>
        Public Function DownloadFileAsync(Url As String, LocalPath As String,
                                          ct As CancellationToken) As Task Implements IDownloadService.DownloadFileAsync
            If String.IsNullOrWhiteSpace(Url) Then Throw New ArgumentNullException(NameOf(Url))
            If String.IsNullOrWhiteSpace(LocalPath) Then Throw New ArgumentNullException(NameOf(LocalPath))

            ' ModNet 是同步阻塞的，放到线程池里跑，避免占住调用方的 UI 线程。
            ' （ModNet 自己也会检测"在 UI 线程执行网络请求"并抛错，这里从根上避开。）
            Return Task.Run(
                Sub()
                    ct.ThrowIfCancellationRequested()

                    Dim folder = Path.GetDirectoryName(LocalPath)
                    If Not String.IsNullOrEmpty(folder) AndAlso Not Directory.Exists(folder) Then
                        Directory.CreateDirectory(folder)
                    End If

                    NetDownloadByClient(Url, LocalPath)
                    ct.ThrowIfCancellationRequested()
                End Sub, ct)
        End Function

        ''' <summary>读取文本内容。</summary>
        Public Function GetStringAsync(Url As String, ct As CancellationToken) As Task(Of String) _
            Implements IDownloadService.GetStringAsync
            If String.IsNullOrWhiteSpace(Url) Then Throw New ArgumentNullException(NameOf(Url))

            Return Task.Run(
                Function()
                    ct.ThrowIfCancellationRequested()
                    ' NetRequestByClient 直接返回响应正文（上游签名如此）。
                    Dim result As String = NetRequestByClient(Url)
                    ct.ThrowIfCancellationRequested()
                    Return result
                End Function, ct)
        End Function

        ''' <summary>带请求头的 GET。</summary>
        Public Function GetStringWithHeadersAsync(Url As String,
                                                  Headers As IReadOnlyDictionary(Of String, String),
                                                  ct As CancellationToken) As Task(Of String) _
            Implements IDownloadService.GetStringAsync

            If String.IsNullOrWhiteSpace(Url) Then Throw New ArgumentNullException(NameOf(Url))

            Return Task.Run(
                Function()
                    ct.ThrowIfCancellationRequested()
                    Dim result As String = NetRequestByClient(Url, Headers:=ToHeaderArray(Headers))
                    ct.ThrowIfCancellationRequested()
                    Return result
                End Function, ct)
        End Function

        ''' <summary>POST JSON。</summary>
        Public Function PostJsonAsync(Url As String, JsonBody As String,
                                      Headers As IReadOnlyDictionary(Of String, String),
                                      ct As CancellationToken) As Task(Of String) _
            Implements IDownloadService.PostJsonAsync

            If String.IsNullOrWhiteSpace(Url) Then Throw New ArgumentNullException(NameOf(Url))

            Return Task.Run(
                Function()
                    ct.ThrowIfCancellationRequested()
                    Dim result As String = NetRequestByClient(
                        Url,
                        New Net.Http.HttpMethod("POST"),
                        Content:=JsonBody,
                        ContentType:="application/json",
                        Headers:=ToHeaderArray(Headers))
                    ct.ThrowIfCancellationRequested()
                    Return result
                End Function, ct)
        End Function

        ''' <summary>POST 表单（OAuth 端点）。</summary>
        Public Function PostFormAsync(Url As String,
                                      Fields As IReadOnlyDictionary(Of String, String),
                                      ct As CancellationToken) As Task(Of String) _
            Implements IDownloadService.PostFormAsync

            If String.IsNullOrWhiteSpace(Url) Then Throw New ArgumentNullException(NameOf(Url))
            If Fields Is Nothing Then Throw New ArgumentNullException(NameOf(Fields))

            Return Task.Run(
                Function()
                    ct.ThrowIfCancellationRequested()

                    Dim builder As New Text.StringBuilder()
                    For Each pair In Fields
                        If builder.Length > 0 Then builder.Append("&"c)
                        builder.Append(Uri.EscapeDataString(pair.Key))
                        builder.Append("="c)
                        builder.Append(Uri.EscapeDataString(pair.Value))
                    Next

                    Dim result As String = NetRequestByClient(
                        Url,
                        New Net.Http.HttpMethod("POST"),
                        Content:=builder.ToString(),
                        ContentType:="application/x-www-form-urlencoded")
                    ct.ThrowIfCancellationRequested()
                    Return result
                End Function, ct)
        End Function

        ''' <summary>把字典转成 ModNet 需要的二维数组。</summary>
        Private Shared Function ToHeaderArray(Headers As IReadOnlyDictionary(Of String, String)) As String(,)
            If Headers Is Nothing OrElse Headers.Count = 0 Then Return Nothing

            Dim result(Headers.Count - 1, 1) As String
            Dim index As Integer = 0
            For Each pair In Headers
                result(index, 0) = pair.Key
                result(index, 1) = pair.Value
                index += 1
            Next
            Return result
        End Function

    End Class

End Namespace
