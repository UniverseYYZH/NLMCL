' NLCL 的启动适配器：把切出来的 PCL 启动参数管线暴露成 NLCL.Abstractions.IGameLauncher。
'
' 复用的是 PCL 的「启动参数」区块（Vendor/ModLaunch.Arguments.vb）：
'   McLaunchArgumentsJVM / McLaunchArgumentsGame / McLaunchArgumentsReplace / McLaunchArgumentMain
' 这一段包含 ${auth_player_name}、${classpath}、${version_type} 等一整套占位符语义，
' 以及 1.12.2- 在 JRE 8u200~321 下的窗口尺寸修复——都是容易写错、值得原样复用的逻辑。
'
' 不复用的是 PCL 的进程监视（Watcher）、崩溃检测、登录页与音乐——那些与 UI 绑定。

Imports System.Diagnostics
Imports System.Threading
Imports System.Threading.Tasks
Imports NLCL.Abstractions

Namespace Global.NLCL.Core.Pcl.Vb

    ''' <summary>
    ''' 构建启动命令行。参数管线的入口只操作全局量，因此这里负责给它们赋值。
    ''' </summary>
    Public NotInheritable Class PclLaunchCommandBuilder

        ''' <summary>
        ''' 从 java 可执行文件推导 Java 主目录（带反斜杠）。
        ''' 参数管线要的是主目录（用来判断版本、拼 natives 路径），不是 exe。
        ''' </summary>
        Public Shared Function DeriveJavaHome(executablePath As String) As String
            If String.IsNullOrWhiteSpace(executablePath) Then Return String.Empty

            Dim binFolder = Path.GetDirectoryName(executablePath)
            If String.IsNullOrEmpty(binFolder) Then Return String.Empty

            Dim home = Path.GetDirectoryName(binFolder)
            If String.IsNullOrEmpty(home) Then Return String.Empty

            Return home.TrimEnd("\"c) & "\"
        End Function

        ''' <summary>
        ''' 生成完整命令行（不含 java 可执行文件本身）。
        ''' </summary>
        Public Shared Function BuildCommand(
            Instance As McInstance,
            JavaFolder As String,
            JavaVersion As Version,
            UserName As String,
            Uuid As String,
            AccessToken As String,
            Optional ServerIp As String = Nothing,
            Optional ExtraGameArgs As IEnumerable(Of String) = Nothing,
            Optional ClientId As String = Nothing,
            Optional Xuid As String = Nothing,
            Optional RamMb As Integer = 0) As String

            If Instance Is Nothing Then Throw New ArgumentNullException(NameOf(Instance))

            ' 内存：PCL 的参数管线从 PageInstanceSetup.GetRam 取（见 Vendor\ModLaunch.Arguments.vb），
            ' 那原本是实例设置页的滑块。我们没有那个页面，所以先把它要的值放进去，
            ' 否则不管界面选了多少，命令行永远是 -Xmx2048m。
            PageInstanceSetup.RamMbOverride = If(RamMb > 0, RamMb, 0)

            ' 管线读取的是这几个全局量，先摆好。
            Dim versionDir = Instance.PathVersion.TrimEnd("\"c)
            Dim versionsDir = Path.GetDirectoryName(versionDir)
            McFolderSelected = Path.GetDirectoryName(versionsDir) & "\"
            McInstanceSelected = Instance
            McLaunchJavaSelected = New Java(JavaFolder, JavaVersion)

            McLoginLoader.Output.Name = If(UserName, "Player")
            McLoginLoader.Output.Uuid = If(Uuid, String.Empty)
            McLoginLoader.Output.AccessToken = If(AccessToken, "0")

            Dim Options As New McLaunchOptions
            If ServerIp IsNot Nothing Then Options.ServerIp = ServerIp
            If ExtraGameArgs IsNot Nothing Then Options.ExtraGameArgs.AddRange(ExtraGameArgs)
            CurrentLaunchOptions = Options

            ' 入口只用 McLaunchLog 输出，不碰 Loader，因此传 Nothing 是安全的。
            McLaunchArgumentMain(Nothing)

            ' PCL 的替换表**没有** ${clientid} / ${auth_xuid}（整个仓库搜不到 xuid），
            ' 而 1.19+ 的原版参数里就有这两个。PCL 自己启动时会把字面量直接塞给游戏——
            ' 能跑（游戏不校验这两个值），但不干净；我们在这里补上。
            ' 离线账号没有 xuid，统一给 "0" 保持参数个数不变。
            Dim argument = McLaunchArgument
            argument = argument.Replace("${clientid}", If(ClientId, "0"))
            argument = argument.Replace("${auth_xuid}", If(Xuid, "0"))

            ' 版本 JSON 里写的是 ${natives_directory}/java，但 PCL 把 natives 按库解压到
            ' -natives\lwjgl、-natives\jna 等子目录，根本没有 java 子目录。
            ' 后果是 LWJGL 报 "Failed to locate library: lwjgl.dll"，游戏在 preLaunch 就崩。
            ' 这里按实际目录结构重建 java.library.path。
            argument = FixNativesLibraryPath(argument, GetNativesFolder())

            Return argument
        End Function

        ''' <summary>
        ''' 启动前把 natives 解压到 &lt;版本&gt;-natives\。
        '''
        ''' 两种格式都要处理：
        '''  1. 旧格式（≤1.18.2）：库节点带 <c>natives.windows</c> + <c>downloads.classifiers</c>，
        '''     由 PCL 的 McLaunchNatives 处理（已原样切分进 Vendor\ModLaunch.Natives.vb）。
        '''  2. 新格式（1.19+）：natives 是**独立库**，名字形如
        '''     <c>org.lwjgl:lwjgl:3.3.1:natives-windows</c>，没有 natives 字段。
        '''     PCL 自己的 McLaunchNatives 不认这种（IsNatives 恒为 False），
        '''     它 rely on LWJGL 运行时自己解压；但那条路一旦走不通，
        '''     游戏就在 preLaunch 阶段报 "Failed to locate library: lwjgl.dll"，
        '''     于是这里也把它们的 dll 平铺解压到同一个目录。
        ''' </summary>
        Public Shared Sub PrepareNatives(Instance As McInstance)
            If Instance Is Nothing Then Return

            ' McLaunchNatives 读的是这两个全局量：
            '   McInstanceSelected —— 解压到哪个 <版本>-natives\
            '   McFolderSelected   —— 库文件在哪个 .minecraft\libraries\
            Dim versionDirectory = Instance.PathVersion.TrimEnd("\"c)
            Dim versionsDirectory = Path.GetDirectoryName(versionDirectory)
            If Not String.IsNullOrEmpty(versionsDirectory) Then
                Dim minecraftDirectory = Path.GetDirectoryName(versionsDirectory)
                If Not String.IsNullOrEmpty(minecraftDirectory) Then
                    McFolderSelected = minecraftDirectory & "\"
                End If
            End If
            McInstanceSelected = Instance

            Try
                Dim libraries = McLibListGet(Instance, True)
                Dim target = GetNativesFolder()

                ' 旧格式：交给 PCL 的原逻辑。
                ' 只有真的存在 Natives 条目时才调 —— 它结束时会删除目录里"多余"的文件，
                ' 新格式实例里那些文件是 LWJGL 自己解压出来的，不能动。
                If libraries IsNot Nothing AndAlso libraries.Any(Function(item) item.IsNatives) Then
                    McLaunchNatives(Nothing)
                End If

                ExtractModernNatives(libraries, target)
            Catch ex As Exception
                ' 解压失败不拦启动：让游戏自己报错，同时把原因写进日志。
                Logger.Warn(ex, "解压 Natives 失败")
            End Try
        End Sub

        ''' <summary>
        ''' 把新格式 natives（独立的 <c>:natives-windows</c> 库）里的 dll 平铺到 natives 目录。
        ''' 平铺而不是保留 jar 内层级：Windows 加载 dll 时依赖会在同目录查找，
        ''' 分开放会导致 lwjgl.dll 找不到 jemalloc.dll 之类的同族依赖。
        ''' </summary>
        Private Shared Sub ExtractModernNatives(libraries As List(Of McLibEntry), target As String)
            If libraries Is Nothing OrElse String.IsNullOrEmpty(target) Then Return

            Dim isArm64 = String.Equals(
                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE"),
                "ARM64",
                StringComparison.OrdinalIgnoreCase)

            Dim wantedSuffix = If(isArm64, ":natives-windows-arm64", ":natives-windows")

            Directory.CreateDirectory(target)

            For Each library In libraries
                If library.IsNatives OrElse String.IsNullOrEmpty(library.OriginalName) Then Continue For

                Dim name = library.OriginalName.ToLowerInvariant()
                If Not name.EndsWith(wantedSuffix, StringComparison.Ordinal) Then Continue For
                If String.IsNullOrEmpty(library.LocalPath) OrElse Not File.Exists(library.LocalPath) Then Continue For

                Dim zip As ZipArchive = Nothing
                Try
                    zip = FileUtils.OpenZip(library.LocalPath)

                    For Each entry In zip.Entries
                        If Not entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) Then Continue For

                        ' jar 内路径形如 windows/x64/org/lwjgl/lwjgl.dll —— 取文件名平铺
                        Dim fileName = Path.GetFileName(entry.FullName)
                        Dim destination = Path.Combine(target, fileName)

                        If File.Exists(destination) AndAlso New FileInfo(destination).Length = entry.Length Then
                            Continue For
                        End If

                        FileUtils.Write(destination, entry.Open)
                    Next
                Catch ex As Exception
                    Logger.Warn(ex, "解压 Natives 失败：" & library.LocalPath)
                Finally
                    If zip IsNot Nothing Then zip.Dispose()
                End Try
            Next
        End Sub

        ''' <summary>
        ''' 把命令行里的 -Djava.library.path 改成"所有含 dll 的目录"的分号列表。
        ''' 原值指向不存在的 ...-natives/java 时才会改。
        ''' </summary>
        Public Shared Function FixNativesLibraryPath(command As String, nativesRoot As String) As String
            If String.IsNullOrEmpty(command) Then Return command
            If String.IsNullOrWhiteSpace(nativesRoot) Then Return command

            Const marker As String = "-Djava.library.path="
            Dim index = command.IndexOf(marker, StringComparison.Ordinal)
            If index < 0 Then Return command

            Dim valueStart = index + marker.Length
            If valueStart >= command.Length Then Return command

            ' 值可能被引号包住（整个参数带引号，或只有值带引号），两种都按"下一个引号"处理
            Dim contentStart = If(command(valueStart) = """"c, valueStart + 1, valueStart)
            Dim contentEnd As Integer
            Dim nextQuote = command.IndexOf(""""c, contentStart)
            If nextQuote >= 0 Then
                contentEnd = nextQuote
            Else
                Dim spaceIndex = command.IndexOf(" "c, contentStart)
                contentEnd = If(spaceIndex < 0, command.Length, spaceIndex)
            End If
            If contentEnd <= contentStart Then Return command

            Dim repaired = BuildLibraryPath(nativesRoot)
            If String.IsNullOrEmpty(repaired) Then Return command

            Return command.Substring(0, contentStart) & repaired & command.Substring(contentEnd)
        End Function

        ''' <summary>收集 natives 根目录下所有含 dll 的目录，用分号连接。</summary>
        Public Shared Function BuildLibraryPath(nativesRoot As String) As String
            If String.IsNullOrWhiteSpace(nativesRoot) Then Return String.Empty

            Dim root = nativesRoot.TrimEnd("\"c, "/"c)
            If Not Directory.Exists(root) Then Return String.Empty

            Dim directories As New List(Of String)

            Try
                If Directory.GetFiles(root, "*.dll").Length > 0 Then directories.Add(root)

                For Each child In Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                    If Directory.GetFiles(child, "*.dll").Length > 0 Then directories.Add(child)
                Next
            Catch ex As IOException
                Return String.Empty
            Catch ex As UnauthorizedAccessException
                Return String.Empty
            End Try

            If directories.Count = 0 Then Return String.Empty

            Return String.Join(";", directories)
        End Function

    End Class

    ''' <summary>
    ''' <see cref="IGameLauncher"/> 的 PCL 实现：构建命令行后用独立进程启动游戏。
    ''' </summary>
    Public NotInheritable Class PclGameLauncher
        Implements IGameLauncher

        ''' <summary>Java 可执行文件路径。默认取 PATH 里的 javaw.exe。</summary>
        Public Property JavaExecutable As String = "javaw.exe"

        ''' <summary>Java 主版本，用于匹配版本 JSON 的 Java 要求（默认 17）。</summary>
        Public Property JavaFolder As String = String.Empty

        ''' <summary>Java 版本号。上游用完整 Version，这里默认 17。</summary>
        Public Property JavaVersion As Version = New Version(17, 0)

        Public Function LaunchAsync(Request As LaunchRequest,
                                    cancellationToken As CancellationToken) As Task(Of LaunchResult) _
            Implements IGameLauncher.LaunchAsync

            If Request Is Nothing Then Throw New ArgumentNullException(NameOf(Request))

            Dim work As Func(Of LaunchResult) = Function() BuildAndStart(Request)
            Return Task.Run(work, cancellationToken)
        End Function

        ''' <summary>构建命令行并启动进程（同步实现，由 <see cref="LaunchAsync"/> 放到线程池）。</summary>
        Private Function BuildAndStart(Request As LaunchRequest) As LaunchResult
            Dim versionFolder = Path.Combine(Request.MinecraftDirectory, "versions", Request.VersionId)
            If Not Directory.Exists(versionFolder) Then
                Return New LaunchResult(0, False, "未找到版本目录：" & versionFolder)
            End If

            ' PCL 的垫片会创建 WPF 的 Window（要求 STA），因此这段必须在 STA 线程上跑
            Dim command = StaRunner.Run(
                Function()
                    ' 必须在 New McInstance 之前摆好：McInstance 加载时要用它定位
                    ' <游戏目录>\versions\<inheritsFrom>\ 去合并父版本 JSON。
                    ' 设晚了（例如等到 BuildCommand 里再设），Fabric 这类 inheritsFrom 实例
                    ' 只会拿到自己的 8 个库，父版本的 jopt-simple/lwjgl 等全丢，
                    ' 启动时报 NoClassDefFoundError: joptsimple/OptionSpec。
                    McFolderSelected = Request.MinecraftDirectory.TrimEnd("\"c) & "\"

                    Dim instance As New McInstance(versionFolder)
                    instance.Check()
                    PclLaunchCommandBuilder.PrepareNatives(instance)

                    Dim javaHome = If(String.IsNullOrWhiteSpace(JavaFolder),
                                       PclLaunchCommandBuilder.DeriveJavaHome(If(Request.JavaExecutable, JavaExecutable)),
                                       JavaFolder)

                    Return PclLaunchCommandBuilder.BuildCommand(
                        instance,
                        javaHome,
                        If(Request.JavaVersion, JavaVersion),
                        UserName:=If(Request.PlayerName, "Player"),
                        Uuid:=If(Request.PlayerUuid, String.Empty),
                        AccessToken:=If(Request.AccessToken, "0"),
                        ServerIp:=Request.ServerAddress,
                        RamMb:=Request.MaximumMemoryMb)
                End Function)

            ' 工作目录必须是**版本目录**（PCL 的 PathIndie），不是 .minecraft 根：
            '  - 游戏的 logs/ 会落在版本目录里（PCL 也这样，出事时能找到 latest.log）
            '  - 某些 mod 会按相对路径解析自己的资源/原生库；用错目录会直接 native 崩溃
            '    （实测：ysm 2.6.5 在 .minecraft 根下必崩，版本目录下正常）
            Dim info As New ProcessStartInfo(If(Request.JavaExecutable, JavaExecutable), command) With {
                .WorkingDirectory = versionFolder,
                .UseShellExecute = False,
                .CreateNoWindow = False
            }

            ' 把"实际用的命令行 + 工作目录 + 退出码"记下来。
            ' 游戏静默退出时（无日志、无 hs_err）这是唯一能拿来对比的线索。
            Dim launchLog = PclLaunchLog.Write(info, Request)

            Dim child = Process.Start(info)
            If child Is Nothing Then
                Return New LaunchResult(0, False, "进程未能启动。")
            End If

            ' 进程退出后补一行退出码（后台等，不阻塞界面）
            Dim capturedId = child.Id
            Task.Run(
                Sub()
                    Try
                        child.WaitForExit(120000)
                        If child.HasExited Then PclLaunchLog.Append(launchLog, "退出码: " & child.ExitCode)
                    Catch ex As Exception
                        Logger.Warn(ex, "记录游戏退出码失败")
                    End Try
                End Sub)

            Return New LaunchResult(capturedId, True, "已启动（PID " & capturedId & "）")
        End Function

    End Class

    ''' <summary>
    ''' 启动记录：两个启动入口（发射后不管 / 带控制台）共用同一份 last-launch.txt。
    ''' 游戏静默退出时（无日志、无 hs_err）这是唯一能拿来对比"我们到底发了什么命令"的线索。
    ''' </summary>
    Friend Module PclLaunchLog

        ''' <summary>写记录并返回路径；失败返回空串。</summary>
        Friend Function Write(info As ProcessStartInfo, Request As LaunchRequest) As String
            Try
                Dim folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NLCL")
                Directory.CreateDirectory(folder)

                Dim logPath = Path.Combine(folder, "last-launch.txt")
                Dim builder As New System.Text.StringBuilder()
                builder.AppendLine("时间: " & DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                builder.AppendLine("版本: " & Request.VersionId)
                builder.AppendLine("游戏目录: " & Request.MinecraftDirectory)
                builder.AppendLine("工作目录: " & info.WorkingDirectory)
                builder.AppendLine("Java: " & info.FileName)
                builder.AppendLine("Java 版本: " & If(Request.JavaVersion?.ToString(), "(未指定)"))
                builder.AppendLine("内存上限: " & Request.MaximumMemoryMb & " MB")
                builder.AppendLine()
                builder.AppendLine("命令行:")
                builder.AppendLine(info.Arguments)

                File.WriteAllText(logPath, builder.ToString(), System.Text.Encoding.UTF8)
                Return logPath
            Catch ex As Exception
                Logger.Warn(ex, "写入启动记录失败")
                Return String.Empty
            End Try
        End Function

        Friend Sub Append(logPath As String, line As String)
            If String.IsNullOrEmpty(logPath) Then Return

            Try
                File.AppendAllText(logPath, Environment.NewLine & line, System.Text.Encoding.UTF8)
            Catch ex As Exception
                Logger.Warn(ex, "追加启动记录失败")
            End Try
        End Sub

    End Module

End Namespace
