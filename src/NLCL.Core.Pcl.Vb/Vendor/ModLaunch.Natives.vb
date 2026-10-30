' ============================================================================
' 从 PCL 的 ModLaunch.vb **切分**：启动前解压 Natives
'
' 上游：Plain Craft Launcher 2\Modules\Minecraft\ModLaunch.vb
'        原始行号 1659–1722（#Region "解压 Natives"）
'
' 切分原因：M4 只切了「启动参数构建」（1191–1655），但 PCL 真正的启动流程在
'           构建参数之前还有一步 McLaunchNatives：把版本用到的 *-natives.jar
'           里的 dll 解压到 <版本>-natives\。缺了这一步，${natives_directory}
'           指向一个空目录，LWJGL 在 preLaunch 阶段报
'           "Failed to locate library: lwjgl.dll"，Fabric 的 sodium 直接炸。
'
' **与上游的唯一内容性差异**：McLaunchNatives 由 Private 放宽为 Friend，
' 以便同程序集内的适配器调用；函数体一行未改。
' ============================================================================

Public Module ModLaunchNatives

    Friend Sub McLaunchNatives(Loader As LoaderTask(Of Integer, Integer))

        '创建文件夹
        Dim Target As String = GetNativesFolder()
        DirectoryUtils.Create(Target)

        '解压文件
        McLaunchLog("正在解压 Natives 文件")
        Dim ExistFiles As New List(Of String)
        For Each Native As McLibEntry In McLibListGet(McInstanceSelected, True)
            If Not Native.IsNatives Then Continue For
            Dim Zip As ZipArchive
            Try
                Zip = FileUtils.OpenZip(Native.LocalPath)
            Catch ex As InvalidDataException
                Logger.Warn(ex, $"打开 Natives 文件失败（{Native.LocalPath}）")
                FileUtils.Delete(Native.LocalPath)
                Throw New Exception("无法打开 Natives 文件（" & Native.LocalPath & "），该文件可能已损坏，请重新尝试启动游戏")
            End Try
            For Each Entry In Zip.Entries
                Dim FileName As String = Entry.FullName
                If PathUtils.GetExtension(FileName) = "dll" Then
                    '实际解压文件的步骤
                    Dim FilePath As String = Target & FileName
                    ExistFiles.Add(FilePath)
                    Dim OriginalFile = FileUtils.GetInfo(FilePath)
                    If OriginalFile.Exists Then
                        If OriginalFile.Length = Entry.Length Then
                            If ModeDebug Then McLaunchLog("无需解压：" & FilePath)
                            Continue For
                        End If
                        '删除原文件
                        Try
                            FileUtils.Delete(FilePath)
                        Catch ex As UnauthorizedAccessException
                            McLaunchLog("删除原 dll 访问被拒绝，这通常代表有一个 MC 正在运行，跳过解压：" & FilePath)
                            McLaunchLog("实际的错误信息：" & ex.GetDisplay(True))
                            Exit For
                        End Try
                    End If
                    '解压新文件
                    FileUtils.Write(FilePath, Entry.Open)
                    McLaunchLog("已解压：" & FilePath)
                End If
            Next
            If Zip IsNot Nothing Then Zip.Dispose()
        Next

        '删除多余文件
        For Each FileName As String In DirectoryUtils.EnumerateFiles(Target)
            If ExistFiles.Contains(FileName) Then Continue For
            Try
                McLaunchLog("删除：" & FileName)
                FileUtils.Delete(FileName)
            Catch ex As UnauthorizedAccessException
                McLaunchLog("删除多余文件访问被拒绝，跳过删除步骤")
                McLaunchLog("实际的错误信息：" & ex.GetDisplay(True))
                Return
            End Try
        Next

    End Sub

End Module
