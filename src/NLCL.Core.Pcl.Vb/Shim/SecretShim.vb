' 垫片：PCL 的网络鉴权与临时目录。
'
' 上游 ModSecret.vb 是**开源版删改过**的文件（文件头注明"部分代码已被删除"），
' 但下载栈用到的两个函数完整可见，这里按其原始语义复刻。
'
' 与上游的差异（有意为之，需在 review 时确认）：
'   1. User-Agent 改为 NLCL 自身标识，继续冒充 PCL 会误导服务端统计；
'   2. Referer 不再指向 PCL 的服务器；
'   3. 环境变量仍沿用 PCL_MS_CLIENT_ID / PCL_CURSEFORGE_API_KEY 的读取位置，只是不提供默认值。
'
' 上游：Plain Craft Launcher 2\Modules\ModSecret.vb  L19 / L26
'       Plain Craft Launcher 2\Modules\ModMain.vb    L941（RequestTaskTempFolder）

Imports System.IO
Imports System.Net.Http

Friend Module ModSecretShim

    ''' <summary>CurseForge API Key，从环境变量读取（上游同此）。</summary>
    Public ReadOnly CurseForgeAPIKey As String =
        If(Environment.GetEnvironmentVariable("PCL_CURSEFORGE_API_KEY"), "")

    ''' <summary>用于微软登录的 ClientId（上游同此）。</summary>
    Public ReadOnly OAuthClientId As String =
        If(Environment.GetEnvironmentVariable("PCL_MS_CLIENT_ID"), "")

    ''' <summary>是否限制 Minecraft 下载源。上游开源版恒为 True。</summary>
    Public Const RestrictMcDownload As Boolean = True

    ''' <summary>
    ''' 处理带 <c>{CDN}</c> 标记的地址：去掉标记，并把空格转义。
    ''' 上游实现逐字一致（不含任何加密逻辑，开源版保留了完整实现）。
    ''' </summary>
    Friend Function SecretCdnSign(UrlWithMark As String) As String
        If Not UrlWithMark.EndsWith("{CDN}") Then Return UrlWithMark
        Return UrlWithMark.Replace("{CDN}", "").Replace(" ", "%20")
    End Function

    ''' <summary>
    ''' 为请求补齐 User-Agent / Referer / API Key。
    ''' 语义与上游一致，但 UA 换成 NLCL 自己的标识。
    ''' </summary>
    Friend Sub SecretHeadersSign(Url As String, ByRef Req As HttpRequestMessage,
                                 Optional SimulateBrowserHeaders As Boolean = False)
        If Not Req.Headers.UserAgent.Any Then
            If Url.Contains("baidupcs.com") OrElse Url.Contains("baidu.com") Then
                Req.Headers.Add("User-Agent", "LogStatistic")
            ElseIf SimulateBrowserHeaders Then
                Req.Headers.Add("User-Agent",
                    $"NLCL/{VersionBaseName} Mozilla/5.0 AppleWebKit/537.36 Chrome/63.0.3239.132 Safari/537.36")
            Else
                Req.Headers.Add("User-Agent", $"NLCL/{VersionBaseName}")
            End If
        End If

        If Url.Contains("api.curseforge.com") OrElse Url.Contains("forgecdn.net") Then
            If CurseForgeAPIKey <> "" Then Req.Headers.Add("x-api-key", CurseForgeAPIKey)
        End If
    End Sub

End Module

''' <summary>
''' 下载用的临时目录。上游会按需在 <c>PathTemp</c> 下建任务子目录，
''' 这里用 <c>%TEMP%\NLCL\Temp\</c>，返回值保证以反斜杠结尾（上游约定）。
''' </summary>
Public Module PclTempPaths

    Public Function RequestTaskTempFolder(Optional RequireNonSpace As Boolean = False) As String
        Dim root = Path.Combine(Path.GetTempPath(), "NLCL", "Temp")
        Dim folder = Path.Combine(root, Guid.NewGuid().ToString("N").Substring(0, 8))
        Directory.CreateDirectory(folder)
        Return folder & "\"
    End Function

End Module
