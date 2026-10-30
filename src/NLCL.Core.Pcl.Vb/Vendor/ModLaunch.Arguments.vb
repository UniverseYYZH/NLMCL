' ============================================================================
' 从 PCL 的 ModLaunch.vb **切分**：启动参数管线
'
' 上游：Plain Craft Launcher 2\Modules\Minecraft\ModLaunch.vb
'        原始行号 1191–1655（共 465 行）
' 内容：McLaunchArgumentMain / McLaunchArgumentsJVM / McLaunchArgumentsGame /
'       McLaunchArgumentsReplace（${...} 替换表）/ DeduplicateJavaArguments
'
' 切分原因：整个 ModLaunch 需要补 382 个符号（登录页、页面栈、皮肤、音乐），
'           但「启动参数」这一段只依赖十余个全局量，是 M4 真正需要的部分。
'
' **与上游的唯一内容性差异**：5 个入口由 Private 放宽为 Friend，
' 以便同程序集内的 C# 适配器调用；函数体一行未改。
' 另：外层包了一个 Module（上游这些成员属于 Module ModLaunch）。
' ============================================================================

Public Module ModLaunchArguments

    Friend Sub McLaunchArgumentMain(Loader As LoaderTask(Of String, Integer))
        '获取基础参数
        Dim Arguments As New List(Of String)
        McLaunchLog("参数获取开始")
        Arguments.AddRange(McLaunchArgumentsJVM()) 'JVM 参数
        Arguments.Add(McInstanceSelected.JsonObject("mainClass")) 'mainClass
        Arguments.AddRange(McLaunchArgumentsGame()) '游戏参数
        '替换
        McLaunchLog("=================== 启动参数开始 ===================")
        Dim Replacements = McLaunchArgumentsReplace(Arguments).ToList
        Dim Argument As String = ""
        For Each Arg In Arguments
            '应用替换标记
            For Each Replacement In Replacements
                Arg = Arg.Replace(Replacement.Key, Replacement.Value)
            Next
            '为带特殊字符的参数添加双引号
            If Arg.IndexOfAny({"&"c, "|"c, "<"c, ">"c, "^"c, " "c}) >= 0 AndAlso Not Arg.StartsWithF("""") AndAlso Not Arg.EndsWithF("""") Then
                Arg = $"""{Arg.Replace("""", "\""")}""" '转义在原本参数中间的双引号
            End If
            '输出
            McLaunchLog(Arg)
            Argument &= Arg & " "
        Next
        McLaunchLog("=================== 启动参数结束 ===================")
        '输出
        McLaunchArgument = Argument.TrimEnd
    End Sub

    'JVM 部分
    Friend Function McLaunchArgumentsJVM() As List(Of String)
        Dim Args As New List(Of String)

        'Minecraft 参数
        If McInstanceSelected.JsonObject("arguments") IsNot Nothing AndAlso McInstanceSelected.JsonObject("arguments")("jvm") IsNot Nothing Then
            '新版 MC：从版本 JSON 获取参数
            Dim CurrentInstance As McInstance = McInstanceSelected
NextInstance:
            McLaunchLog($"新版 JVM 参数：从版本 JSON 获取（{CurrentInstance.Name}）")
            If CurrentInstance.JsonObject("arguments") IsNot Nothing AndAlso CurrentInstance.JsonObject("arguments")("jvm") IsNot Nothing Then
                For Each SubJson As JToken In CurrentInstance.JsonObject("arguments")("jvm")
                    Select Case SubJson.Type
                        Case JTokenType.String
                            Args.Add(SubJson.ToString.Trim)
                        Case JTokenType.Object
                            Dim Argument As JObject = CType(SubJson, JObject)
                            If Argument.ContainsKey("rules") AndAlso Not McJsonRuleCheck(Argument("rules")) Then Continue For '不满足准则
                            If Not Argument.ContainsKey("value") Then Continue For '没有 value 字段
                            If Argument("value").Type = JTokenType.String Then
                                Args.Add(Argument("value").ToString.Trim)
                            Else
                                For Each value As JToken In Argument("value")
                                    Args.Add(value.ToString.Trim)
                                Next
                            End If
                    End Select
                Next
            End If
            If CurrentInstance.InheritName <> "" Then
                CurrentInstance = New McInstance(CurrentInstance.InheritName)
                GoTo NextInstance
            End If
            '1.17.1 中有个 "-Dos.name=Windows 10"，不立即加双引号会导致 10 被隔开
            Args = Args.Select(Function(a) If(a.Contains(" ") AndAlso Not a.StartsWithF("""") AndAlso Not a.EndsWithF(""""), $"""{a.Replace("""", "\""")}""", a)).ToList
        Else
            '旧版 MC：直接添加参数
            McLaunchLog($"旧版 JVM 参数")
            Args.Add("-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump")
            Args.Add("-Djava.library.path=${natives_directory}")
            Args.Add("-cp") : Args.Add("${classpath}")
        End If

        '设置里自定义的参数
        Dim CustomArg As String = Settings.Get(Of String)("VersionAdvanceJvm", Instance:=McInstanceSelected)
        If CustomArg = "" Then CustomArg = Settings.Get(Of String)("LaunchAdvanceJvm")
        If CustomArg <> "" Then CustomArg = ArgumentReplace(CustomArg)

        '重新分割字符串并去重
        Args = DeduplicateJavaArguments(SplitJavaArguments(Args.Join(" "c).Replace("McEmu= ", "McEmu=") & " " & CustomArg).ToList, True)

        '统一通行证
        If McLoginLoader.Output.Type = "Nide" Then
            Args.Add("-javaagent:""${pure_directory}\nide8auth.jar""=""" & Settings.Get(Of String)("VersionServerNide", Instance:=McInstanceSelected) & """")
        End If
        'Authlib-Injector
        If McLoginLoader.Output.Type = "Auth" Then
            If McLaunchJavaSelected.Version.Major >= 6 Then Args.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT") '信任系统根证书（5252#）
            Dim Server As String = Settings.Get(Of String)("VersionServerAuthServer", Instance:=McInstanceSelected)
            Try
                McLaunchLog($"开始 Authlib Injector Prefetch")
                Dim Response As String = NetRequestByClientRetry(Server, Encoding:=Encoding.UTF8)
                Args.Add("-javaagent:""${pure_directory}\authlib-injector.jar""=""" & Server & """")
                Args.Add("-Dauthlibinjector.side=client")
                Args.Add("-Dauthlibinjector.yggdrasil.prefetched=" & Convert.ToBase64String(Encoding.UTF8.GetBytes(Response)))
            Catch ex As Exception
                Throw New Exception("无法连接到第三方登录服务器（" & If(Server, Nothing) & "）", ex)
            End Try
        End If

        'JLW
        '非 GBK 编码下命令行参数乱码的问题（JDK-8272352）
        Dim UseJLW As Boolean =
            Not Settings.Get(Of Boolean)("LaunchAdvanceDisableJLW") AndAlso Not Settings.Get(Of Boolean)("VersionAdvanceDisableJLW", McInstanceSelected) AndAlso
            Not IsGBKEncoding '即使游戏文件夹名称不含非 ASCII 字符，但玩家名等也可能出现中文，因此依然需要 JLW（#8739）
        If UseJLW AndAlso CustomArg.Contains("-javaagent") Then
            UseJLW = False
            McLaunchLog("由于使用了自定义 javaagent，已禁用 JLW，这可能导致游戏崩溃！", LogLevel.Warn)
        End If
        If UseJLW Then
            If McLaunchJavaSelected.Version.Major >= 9 Then
                Args.Add("--add-exports") : Args.Add("cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED")
            End If
            Args.Add("-Doolloo.jlw.tmpdir=${pure_directory}") '这里需要不以 \ 结尾
        End If
        McLaunchLog("使用 JLW：" & UseJLW)

        'LUA
        Dim UseLUA As Boolean =
            Not Settings.Get(Of Boolean)("LaunchAdvanceDisableLUA") AndAlso Not Settings.Get(Of Boolean)("VersionAdvanceDisableLUA", McInstanceSelected) AndAlso
            McLibListGet(McInstanceSelected, False).Select(Function(e) e.OriginalName).Contains("org.lwjgl:lwjgl:3.4.1")
        If UseLUA Then Args.Add($"-javaagent:""{LaunchUtils.ExtractPatch("Launch\LwjglUnsafeAgent.jar", PathPure.Value)}""")
        McLaunchLog("使用 LUA：" & UseLUA)

#Region "内存管理"

        McLaunchLog("当前剩余内存：" & Math.Round(My.Computer.Info.AvailablePhysicalMemory / 1024 / 1024 / 1024 * 10) / 10 & "G")
        Dim TargetRam As Integer = Math.Floor(PageInstanceSetup.GetRam(McInstanceSelected) * 1024)
        Args.Add("-Xmx" & TargetRam & "m")

        '获取设置的管理方式
        '0：在 Java 21+ 使用分代 ZGC，20~15 使用非分代的 ZGC，14- 使用 G1GC
        '1：在 Java 21+ 使用分代 ZGC，20- 使用 G1GC
        '2：仅 G1GC
        '3：不指定，玩家自定义
        Dim SetupType As Integer = Settings.Get(Of Integer)("LaunchAdvanceGC")
        If McInstanceSelected IsNot Nothing AndAlso Settings.Get(Of Integer)("VersionAdvanceGC", McInstanceSelected) > 0 Then SetupType = Settings.Get(Of Integer)("VersionAdvanceGC", McInstanceSelected) - 1 '去掉默认
        If SetupType <> 3 Then
            '确定是使用 G1GC 还是 ZGC
            Dim UseG1GC As Boolean = False
            If (SetupType = 0 AndAlso McLaunchJavaSelected.Version.Major < 15) OrElse
               (SetupType = 1 AndAlso McLaunchJavaSelected.Version.Major < 21) OrElse
               (SetupType = 2 OrElse SetupType = 4) Then UseG1GC = True '检查 Java 版本，ZGC 在 Java 15+ 可用，分代 ZGC 在 Java 21+ 可用
            If Environment.OSVersion.Version.Major < 10 OrElse Environment.OSVersion.Version.Build < 17763 Then UseG1GC = True '检查系统兼容性，ZGC 需要 Windows 10 1809+
            McLaunchLog($"GC 设置：{SetupType}，选取 {If(UseG1GC, "G1GC", "ZGC")}，Java 版本：{McLaunchJavaSelected.Version.Major}")
            '移除已有的 GC 参数
            Args = Args.Where(Function(a) Not a.RegexCheck(
                "( )*-XX:[+-]?(Use\w+GC|ZGenerational|UseCompactObjectHeaders|G1\w+Percent|G1\w+Size|(Max|Min)(GCPauseMillis|HeapFreeRatio))")).ToList
            '添加 GC 参数
            Args.Add("-XX:+UnlockExperimentalVMOptions")
            If McLaunchJavaSelected.Version.Major >= 24 Then Args.Add("-XX:+UseCompactObjectHeaders")
            If UseG1GC AndAlso SetupType = 4 Then
                '优化的 G1GC
                Args.Add("-XX:+UseG1GC")
                Args.Add("-XX:G1NewSizePercent=20")
                Args.Add("-XX:G1ReservePercent=20")
                Args.Add("-XX:G1HeapRegionSize=32M")
                Args.Add("-XX:MaxGCPauseMillis=50")
                If Not ModeDebug Then Args.Add("-XX:+PerfDisableSharedMem")
                If McLaunchJavaSelected.Version.Major <= 7 Then Args.Add("-XX:MaxPermSize=512m") '#8286
                If McLaunchJavaSelected.Version.Major = 8 Then Args.Add("-XX:+ParallelRefProcEnabled")
                If McLaunchJavaSelected.Version.Major >= 12 Then Args.Add("-XX:MinHeapFreeRatio=25") '需要比 G1NewSizePercent 大一点
                If McLaunchJavaSelected.Version.Major >= 12 Then Args.Add("-XX:MaxHeapFreeRatio=40") '需要比 MinHeapFreeRatio 大一些
            ElseIf UseG1GC Then
                'Mojang G1GC
                Args.Add("-XX:+UseG1GC")
                Args.Add("-XX:G1NewSizePercent=20")
                Args.Add("-XX:G1ReservePercent=20")
                Args.Add("-XX:G1HeapRegionSize=32M")
                Args.Add("-XX:MaxGCPauseMillis=50")
                If McLaunchJavaSelected.Version.Major <= 7 Then Args.Add("-XX:MaxPermSize=512m") '#8286
            Else
                'ZGC
                Args.Add("-XX:+UseZGC")
                If McLaunchJavaSelected.Version.Major = 21 OrElse McLaunchJavaSelected.Version.Major = 22 Then Args.Add("-XX:+ZGenerational") 'Java 23 起默认启用分代 ZGC，不需要再添加参数
            End If
        End If

        Args.Remove("-XX:MaxDirectMemorySize=256M") '#3511 的清理

#End Region

        'Log4j 漏洞防御参数
        Args.Add("-Dlog4j2.formatMsgNoLookups=true")

        '编码（#4700、#5892、#5909、#6934、#8950、#9241）
        Dim UseCompatEncoding = McLaunchJavaSelected.Version.Major >= 18
        Dim EncodingName = If(UseCompatEncoding,
                              McLaunchJavaSelected.NativeEncodingName, 'Java 18+ 会添加 COMPAT，因此 Java 会回退到 native.encoding
                              McLaunchJavaSelected.FileEncodingName) 'Java 17- 直接使用 file.encoding
        Try
            McLaunchEncoding = Encoding.GetEncoding(If(EncodingName.Equals("GBK", StringComparison.OrdinalIgnoreCase), "GB18030", EncodingName))
        Catch ex As Exception
            Dim FallbackEncoding = Encoding.GetEncoding(Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage) '老版本 Java 的编码选择行为
            McLaunchLog($"无法识别 Java 编码 {If(EncodingName, "Nothing")}，已回退为 {FallbackEncoding.WebName}", LogLevel.Warn)
            McLaunchEncoding = FallbackEncoding
        End Try
        McLaunchLog($"使用编码：{McLaunchEncoding.WebName}")
        Dim LaunchEncodingName = If(McLaunchEncoding.CodePage = 54936, "GBK", McLaunchEncoding.WebName)
        Dim StdoutArgName = If(McLaunchJavaSelected.Version.Major < 19, "sun.stdout.encoding", "stdout.encoding") '老版本 Java 应该设置 sun.std
        Dim StderrArgName = If(McLaunchJavaSelected.Version.Major < 19, "sun.stderr.encoding", "stderr.encoding")
        If Not Args.Any(Function(a) a.StartsWithF($"-D{StdoutArgName}=")) Then Args.Add($"-D{StdoutArgName}={LaunchEncodingName}")
        If Not Args.Any(Function(a) a.StartsWithF($"-D{StderrArgName}=")) Then Args.Add($"-D{StderrArgName}={LaunchEncodingName}")
        If UseCompatEncoding AndAlso Not Args.Any(Function(a) a.StartsWithF("-Dfile.encoding=")) Then Args.Add("-Dfile.encoding=COMPAT") '防止老版本 MC 与部分 Mod 不兼容 UTF-8

        '为 JLW 添加 -jar，它必须放在最后
        If UseJLW Then
            Args.Add("-jar") : Args.Add(LaunchUtils.ExtractPatch("Launch\JavaWrapper.jar", PathPure.Value))
        End If

        '再次去重并输出
        Return DeduplicateJavaArguments(Args, True)
    End Function
    '游戏参数部分
    Friend Function McLaunchArgumentsGame() As List(Of String)
        Dim Arg As String = ""

        'Minecraft 参数
        '旧版 MC：直接添加参数
        If McInstanceSelected.JsonObject("minecraftArguments")?.ToString.Any Then
            Dim BasicString As String = McInstanceSelected.JsonObject("minecraftArguments").ToString
            McLaunchLog($"旧版游戏参数：" & BasicString)
            BasicString += " --height ${resolution_height} --width ${resolution_width}" '总是添加宽高选项，之后去重的时候会覆盖 MC 自带的
            Arg &= " " & BasicString
        End If
        '新版 MC：从版本 JSON 获取参数
        If McInstanceSelected.JsonObject("arguments") IsNot Nothing AndAlso McInstanceSelected.JsonObject("arguments")("game") IsNot Nothing Then
            Dim CurrentInstance As McInstance = McInstanceSelected
            Dim AppendArg = Sub(a As String) Arg &= " " & If(a.Contains(" ") AndAlso Not a.StartsWithF("""") AndAlso Not a.EndsWithF(""""), $"""{a.Replace("""", "\""")}""", a)
NextInstance:
            McLaunchLog($"新版游戏参数：从版本 JSON 获取（{CurrentInstance.Name}）")
            If CurrentInstance.JsonObject("arguments") IsNot Nothing AndAlso CurrentInstance.JsonObject("arguments")("game") IsNot Nothing Then
                For Each SubJson As JToken In CurrentInstance.JsonObject("arguments")("game")
                    Select Case SubJson.Type
                        Case JTokenType.String
                            AppendArg(SubJson.ToString)
                        Case JTokenType.Object
                            Dim Argument As JObject = CType(SubJson, JObject)
                            If Argument.ContainsKey("rules") AndAlso Not McJsonRuleCheck(Argument("rules")) Then Continue For '不满足准则
                            If Not Argument.ContainsKey("value") Then Continue For '没有 value 字段
                            If Argument("value").Type = JTokenType.String Then
                                AppendArg(Argument("value").ToString)
                            Else
                                For Each value As JToken In Argument("value")
                                    AppendArg(value.ToString)
                                Next
                            End If
                    End Select
                Next
            End If
            If CurrentInstance.InheritName <> "" Then
                CurrentInstance = New McInstance(CurrentInstance.InheritName)
                GoTo NextInstance
            End If
        End If

        '设置里自定义的参数
        Dim CustomArg As String = Settings.Get(Of String)("VersionAdvanceGame", Instance:=McInstanceSelected)
        If CustomArg = "" Then CustomArg = Settings.Get(Of String)("LaunchAdvanceGame")
        If CustomArg <> "" Then CustomArg = ArgumentReplace(CustomArg)
        Arg &= " " & CustomArg

        '把 OptiFineForgeTweaker 放在参数的末尾
        If (McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Forge) OrElse McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader)) AndAlso
                McInstanceSelected.Version.HasOptiFine AndAlso Not McInstanceSelected.Version.OptiFineAsMod Then
            If Arg.Contains("--tweakClass optifine.OptiFineForgeTweaker") Then
                McLaunchLog("发现正确的 OptiFineForge TweakClass，目前的游戏参数：" & Arg)
                Arg = Arg.Replace(" --tweakClass optifine.OptiFineForgeTweaker", "") & " --tweakClass optifine.OptiFineForgeTweaker"
            End If
            If Arg.Contains("--tweakClass optifine.OptiFineTweaker") Then
                McLaunchLog("发现错误的 OptiFineForge TweakClass，目前的游戏参数：" & Arg)
                Arg = Arg.Replace(" --tweakClass optifine.OptiFineTweaker", "") & " --tweakClass optifine.OptiFineForgeTweaker"
                Try
                    FileUtils.Write(
                        McInstanceSelected.GetJsonPath(),
                        FileUtils.ReadAsString(McInstanceSelected.GetJsonPath()).Replace("optifine.OptiFineTweaker", "optifine.OptiFineForgeTweaker"))
                Catch ex As Exception
                    Logger.Warn(ex, "替换 OptiFineForge TweakClass 失败")
                End Try
            End If
        End If

        '重新分割字符串并去重
        Dim Args As List(Of String) = DeduplicateJavaArguments(SplitJavaArguments(Arg).ToList, False)

        '额外传入的参数
        Args.AddRange(CurrentLaunchOptions.ExtraGameArgs)

        '全屏
        If Settings.Get(Of Integer)("LaunchArgumentWindowType") = 0 Then Args.Add("--fullscreen")

        '自动进入服务器
        Dim Server As String = If(String.IsNullOrEmpty(CurrentLaunchOptions.ServerIp), Settings.Get(Of String)("VersionServerEnter", McInstanceSelected), CurrentLaunchOptions.ServerIp)
        If Server.Any Then
            If McInstanceSelected.ReleaseTime > New Date(2023, 4, 4) Then
                'QuickPlay
                Args.Add($"--quickPlayMultiplayer") : Args.Add(Server)
            Else
                '老版本
                If Server.Contains(":") Then
                    '包含端口号
                    Args.Add("--server") : Args.Add(Server.Split(":")(0))
                    Args.Add("--port") : Args.Add(Server.Split(":")(1))
                Else
                    '不包含端口号
                    Args.Add("--server") : Args.Add(Server)
                    Args.Add("--port") : Args.Add("25565")
                End If
                If McInstanceSelected.Version.HasOptiFine Then Hint("OptiFine 与自动进入服务器可能不兼容，有概率导致材质丢失甚至游戏崩溃！", HintType.Red)
            End If
        End If

        '再次去重并输出
        Return DeduplicateJavaArguments(Args, False)
    End Function
    '获取替换标记
    Friend Iterator Function McLaunchArgumentsReplace(Arguments As List(Of String)) As IEnumerable(Of (Key As String, Value As String))
        '基础参数
        Yield ("${classpath_separator}", ";")
        Yield ("${natives_directory}", PathUtils.RemoveSlashSuffix(GetNativesFolder()))
        Yield ("${library_directory}", PathUtils.ToShortPath(McFolderSelected & "libraries"))
        Yield ("${libraries_directory}", PathUtils.ToShortPath(McFolderSelected & "libraries"))
        Yield ("${pure_directory}", PathUtils.RemoveSlashSuffix(PathPure.Value)) '由 PCL 添加，这会允许在分割参数并去重后再替换路径，防止路径中的特殊字符影响参数分割和去重
        Yield ("${launcher_name}", "PCL")
        Yield ("${launcher_version}", VersionCode)
        Yield ("${version_name}", McInstanceSelected.Name)
        Yield ("${game_directory}", PathUtils.ToShortPath(Left(McInstanceSelected.PathIndie, McInstanceSelected.PathIndie.Count - 1)))
        Yield ("${assets_root}", PathUtils.ToShortPath(McFolderSelected & "assets"))
        Yield ("${user_properties}", "{}")
        Yield ("${auth_player_name}", McLoginLoader.Output.Name)
        Yield ("${auth_uuid}", McLoginLoader.Output.Uuid)
        Yield ("${auth_access_token}", McLoginLoader.Output.AccessToken)
        Yield ("${access_token}", McLoginLoader.Output.AccessToken)
        Yield ("${auth_session}", McLoginLoader.Output.AccessToken)
        Yield ("${user_type}", "msa") '#1221
        Yield ("${primary_jar}", PathUtils.ToShortPath(McInstanceSelected.PathVersion & McInstanceSelected.Name & ".jar")) '#6942
        Yield ("${game_assets}", PathUtils.ToShortPath(McFolderSelected & "assets\virtual\legacy")) '1.5.2 的 pre-1.6 资源索引应与 legacy 合并
        Yield ("${assets_index_name}", McAssetsGetIndexName(McInstanceSelected))

        '自定义信息
        Dim ArgumentInfo As String
        ArgumentInfo = Settings.Get(Of String)("VersionArgumentInfo", Instance:=McInstanceSelected)
        If ArgumentInfo = "" Then ArgumentInfo = Settings.Get(Of String)("LaunchArgumentInfo")
        If ArgumentInfo = "" Then '若自定义信息为空，则必须从参数中去掉该部分，不然 MC 会显示 ${version_type} 或者一个空字符串
            If Arguments.Contains("--versionType") Then
                Dim Index As Integer = Arguments.IndexOf("--versionType")
                Arguments.RemoveAt(Index)
                If Index < Arguments.Count Then Arguments.RemoveAt(Index)
            End If
            Yield ("${version_type}", """""")
        Else
            Yield ("${version_type}", ArgumentReplace(ArgumentInfo))
        End If

        '窗口尺寸
        Dim TargetSize As Size
        Select Case Settings.Get(Of Integer)("LaunchArgumentWindowType")
            Case 2 '与启动器尺寸一致
                Dim Result As Size
                RunInUiWait(Sub() Result = New Size(GetPixelSize(FrmMain.PanForm.ActualWidth), GetPixelSize(FrmMain.PanForm.ActualHeight)))
                TargetSize = Result
                TargetSize.Height -= 29.5 * DPI / 96 '标题栏高度
            Case 3 '自定义
                TargetSize = New Size(Math.Max(100, Settings.Get(Of Integer)("LaunchArgumentWindowWidth")), Math.Max(100, Settings.Get(Of Integer)("LaunchArgumentWindowHeight")))
            Case Else
                TargetSize = New Size(854, 480)
        End Select
        If McInstanceSelected.Version.Vanilla.Major <= 12 AndAlso McLaunchJavaSelected.Version >= New Version(8, 0, 200) AndAlso McLaunchJavaSelected.Version <= New Version(8, 0, 321) AndAlso
                Not McInstanceSelected.Version.HasOptiFine AndAlso Not McInstanceSelected.Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then '修复 1.12.2- 下 JRE 8u200~321 下窗口大小为设置大小的 DPI% 倍（#3463）
            McLaunchLog($"已应用窗口大小过大修复（Java 版本：{McLaunchJavaSelected.Version}）")
            TargetSize.Width /= DPI / 96
            TargetSize.Height /= DPI / 96
        End If
        Yield ("${resolution_width}", Math.Round(TargetSize.Width))
        Yield ("${resolution_height}", Math.Round(TargetSize.Height))

        '支持库
        Dim Cps As New List(Of String)
        Dim OptiFineCp As String = Nothing
        For Each Library As McLibEntry In McLibListGet(McInstanceSelected, True)
            If Library.IsNatives Then Continue For
            If Library.Name IsNot Nothing AndAlso Library.Name = "optifine:OptiFine" Then
                OptiFineCp = Library.LocalPath
            Else
                Cps.Add(Library.LocalPath)
            End If
        Next
        If OptiFineCp IsNot Nothing Then Cps.Insert(Cps.Count - 2, OptiFineCp) 'OptiFine 的总是需要放到倒数第二位
        Yield ("${classpath}", Cps.Select(Function(c) PathUtils.ToShortPath(c)).Join(";"c))
    End Function

    ''' <summary>
    ''' 分割 Java 参数字符串。
    ''' </summary>
    Private Iterator Function SplitJavaArguments(Arg As String) As IEnumerable(Of String)
        Arg = Arg.ReplaceLineEndings(" ", mergeMultiple:=True).Trim '预处理换行符
        If String.IsNullOrWhiteSpace(Arg) Then Return
        Dim Current As New StringBuilder
        Dim InQuotes As Boolean = False
        Dim i As Integer = 0
        While i < Arg.Length
            Dim c As Char = Arg(i)
            If c = "\"c AndAlso i + 1 < Arg.Length AndAlso Arg(i + 1) = """"c Then '检测 \"
                '转义的双引号：保留反斜杠和引号，不切换 InQuotes
                Current.Append("\""")
                i += 1 '额外跳过下一个字符（双引号）
            ElseIf c = """"c Then
                '未转义的双引号：切换 InQuotes
                InQuotes = Not InQuotes
                Current.Append(c)
            ElseIf c = " "c AndAlso Not InQuotes Then
                '不在双引号内的空格：一个参数结束
                If Current.Length > 0 Then
                    Yield Current.ToString()
                    Current.Clear()
                End If
            Else
                '普通字符，直接添加
                Current.Append(c)
            End If
            i += 1
        End While
        If Current.Length > 0 Then Yield Current.ToString '添加剩余的
    End Function
    ''' <summary>
    ''' 对 Java 参数进行去重。
    ''' </summary>
    Friend Function DeduplicateJavaArguments(Args As List(Of String), IsJVMArgs As Boolean) As List(Of String)
        Dim Result As New List(Of String)
        Dim i As Integer = 0
        While i < Args.Count
            Dim Key As String = Args(i)
            If Not Key.StartsWithF("-") OrElse
               (i + 1 >= Args.Count OrElse (Args(i + 1).StartsWithF("-") AndAlso Val(Args(i + 1).AfterFirst("-")) = 0)) Then '避免 -xPos 23 -xPos -50 中后面的 -50 被识别为参数
                '单参数
                i += 1
                If Result.Contains(Key) Then
                    McLaunchLog($"已去重单个{If(IsJVMArgs, " JVM ", "游戏")}参数：{Key}")
                    Continue While
                End If
                Result.Add(Key)
            Else
                '以空格间隔的键值对（例如 --width 233）
                Dim Value As String = Args(i + 1)
                i += 2
                For j = 0 To Result.Count - 2 '向前寻找相同的键
                    If Result(j) <> Key Then Continue For
                    '仅当是游戏参数且不是 tweakClass 时，用新的值覆盖旧的值
                    '据测试，如果包含多个相同 --width 或 --uuid 等参数，两个都会直接失效，如果这发生在关键参数上会导致崩溃
                    If Not IsJVMArgs AndAlso Key <> "--tweakClass" Then
                        McLaunchLog($"已覆盖重复的{If(IsJVMArgs, " JVM ", "游戏")}参数：{Key} {Result(j + 1)} → {Key} {Value}")
                        Result(j + 1) = Value
                        Continue While
                    End If
                    '键和值完全相同，直接抛弃
                    If Result(j + 1) = Value Then
                        McLaunchLog($"已去重{If(IsJVMArgs, " JVM ", "游戏")}参数对：{Key} {Value}")
                        Continue While
                    End If
                Next
                Result.Add(Key)
                Result.Add(Value)
            End If
        End While
        Return Result
    End Function

End Module
