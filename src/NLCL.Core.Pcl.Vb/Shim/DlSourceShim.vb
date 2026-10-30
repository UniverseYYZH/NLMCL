' 垫片：PCL 的下载源（镜像）解析函数。
'
' 上游这些函数在 `ModDownload.vb` 里，把 Mojang 官方 URL 映射成 BMCLAPI 等镜像，
' 并按设置决定优先级。它们让 `ModMinecraft.Helpers` 里的库/资源枚举函数与
' `ModDownload` 形成**循环依赖**（M3-B2 的阻塞点）。
'
' 本垫片只做一件事：**原样返回官方地址**。
' 这不是"假装有镜像"，而是明确的语义选择——NLCL 当前只走官方源，
' 镜像支持留到后续（届时替换本文件即可，调用方无需改动）。
'
' 上游签名：
'   DlSourceLauncherOrMetaGet(Original As String) As IEnumerable(Of String)
'   DlSourceLibraryGet(Original As String) As IEnumerable(Of String)
'   DlSourceAssetsGet(Original As String) As IEnumerable(Of String)
'   DlSourcePreferMojang(...) / DlSourceModGet(Original As String) As String

Public Module DlSourceShim

    ''' <summary>版本清单/元数据地址：只给官方源。</summary>
    Public Function DlSourceLauncherOrMetaGet(Original As String) As IEnumerable(Of String)
        Return {Original}
    End Function

    ''' <summary>库文件地址：只给官方源。</summary>
    Public Function DlSourceLibraryGet(Original As String) As IEnumerable(Of String)
        Return {Original}
    End Function

    ''' <summary>资源文件地址：只给官方源。</summary>
    Public Function DlSourceAssetsGet(Original As String) As IEnumerable(Of String)
        Return {Original}
    End Function

    ''' <summary>Mod 下载地址：只给官方源。</summary>
    Public Function DlSourceModGet(Original As String) As String
        Return Original
    End Function

    ''' <summary>是否优先官方源（上游是只读布尔属性）。NLCL 恒为是。</summary>
    Public ReadOnly Property DlSourcePreferMojang As Boolean = True

    ''' <summary>
    ''' 取版本对应的客户端 JAR 下载项（上游由 ModDownload 提供）。
    ''' 返回 Nothing 表示"无需单独下载"——客户端 JAR 由 NLCL 的安装器负责，
    ''' 本函数只服务于 classpath 构建。
    ''' </summary>
    Public Function DlClientJarGet(Instance As McInstance,
                                   ReturnNothingOnFileUseable As Boolean) As NetFile
        Return Nothing
    End Function

    ''' <summary>按优先级排列下载源（上游会合并官方与镜像）。NLCL 只有官方源，原样返回。</summary>
    Public Function DlSourceOrder(OfficialUrls As IEnumerable(Of String),
                                  MirrorUrls As IEnumerable(Of String)) As IEnumerable(Of String)
        Return OfficialUrls
    End Function

    ''' <summary>版本清单的源顺序。同上，只保留官方源。</summary>
    Public Function DlVersionListOrder(OfficialUrls As IEnumerable(Of String),
                                       MirrorUrls As IEnumerable(Of String)) As IEnumerable(Of String)
        Return OfficialUrls
    End Function

End Module

''' <summary>上游的更新提示弹窗（ModDownload 里调用）。无 UI 时不做事。</summary>
Public Module UpdateHintShim

    Public Sub McUpdateLogShow(VersionName As String)
        Debug.WriteLine("[McUpdateLogShow] " & VersionName)
    End Sub

End Module

''' <summary>下载页的占位类型（上游 PageDownloadInstall，被更新提示引用）。</summary>
Public Class PageDownloadInstall
    Public Shared Sub Show()
    End Sub

    ''' <summary>跳到下载页时要预选的版本。</summary>
    Public Shared Property VersionWaitingSelect As String = String.Empty
End Class
