' NLCL 的启动会话适配器：复用 PCL 的参数管线构建命令行，并捕获进程输出。
'
' 与 PclGameLauncher 的区别：
'   PclGameLauncher   —— 发射后不管，只返回 PID（IGameLauncher）
'   PclGameSessionLauncher —— 返回可观察的会话，stdout/stderr 逐行上报（IGameSessionLauncher）
'
' 两者共用 PclLaunchCommandBuilder，因此参数语义完全一致。

Imports System.Diagnostics
Imports System.Threading
Imports System.Threading.Tasks
Imports NLCL.Abstractions

Namespace Global.NLCL.Core.Pcl.Vb

    ''' <summary>一个正在运行的游戏进程。</summary>
    Public NotInheritable Class PclGameSession
        Implements IGameSession

        Private ReadOnly _process As Process
        Private _disposed As Boolean

        ''' <summary>已观测到的退出码；Integer.MinValue 表示还没退出。</summary>
        Private _exitCode As Integer = Integer.MinValue

        Public Sub New(child As Process)
            _process = child
            _process.EnableRaisingEvents = True
            AddHandler _process.OutputDataReceived, AddressOf OnOutput
            AddHandler _process.ErrorDataReceived, AddressOf OnError
            AddHandler _process.Exited, AddressOf OnExited

            _process.BeginOutputReadLine()
            _process.BeginErrorReadLine()

            ' JVM 参数被拒、主类缺失这类"秒退"会在消费者订阅事件之前就发生。
            ' 那时 Exited 已经广播完毕、没人听见，所以这里先把终态记下来，
            ' 消费者订阅后查 ExitCode 即可补上（否则界面永远停在"运行中"）。
            Try
                If _process.HasExited Then
                    _exitCode = _process.ExitCode
                End If
            Catch ex As InvalidOperationException
            End Try
        End Sub

        Public ReadOnly Property ProcessId As Integer Implements IGameSession.ProcessId
            Get
                Try
                    Return _process.Id
                Catch ex As InvalidOperationException
                    Return 0
                End Try
            End Get
        End Property

        Public ReadOnly Property IsRunning As Boolean Implements IGameSession.IsRunning
            Get
                Try
                    Return Not _process.HasExited
                Catch ex As InvalidOperationException
                    Return False
                End Try
            End Get
        End Property

        Public ReadOnly Property ExitCode As Integer? Implements IGameSession.ExitCode
            Get
                Dim code = _exitCode
                If code = Integer.MinValue Then Return Nothing
                Return code
            End Get
        End Property

        Public Event Output As EventHandler(Of GameOutputLine) Implements IGameSession.Output
        Public Event Exited As EventHandler(Of Integer) Implements IGameSession.Exited

        Private Sub OnOutput(sender As Object, e As DataReceivedEventArgs)
            If e.Data Is Nothing Then Return
            RaiseEvent Output(Me, New GameOutputLine(e.Data, False))
        End Sub

        Private Sub OnError(sender As Object, e As DataReceivedEventArgs)
            If e.Data Is Nothing Then Return
            RaiseEvent Output(Me, New GameOutputLine(e.Data, True))
        End Sub

        Private Sub OnExited(sender As Object, e As EventArgs)
            Dim code As Integer = 0
            Try
                code = _process.ExitCode
            Catch ex As InvalidOperationException
            End Try
            _exitCode = code
            RaiseEvent Exited(Me, code)
        End Sub

        Public Sub [Stop]() Implements IGameSession.Stop
            Try
                If Not _process.HasExited Then _process.Kill()
            Catch ex As InvalidOperationException
            Catch ex As System.ComponentModel.Win32Exception
            End Try
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If _disposed Then Return
            _disposed = True
            RemoveHandler _process.OutputDataReceived, AddressOf OnOutput
            RemoveHandler _process.ErrorDataReceived, AddressOf OnError
            RemoveHandler _process.Exited, AddressOf OnExited
            _process.Dispose()
        End Sub

    End Class

    ''' <summary><see cref="IGameSessionLauncher"/> 的 PCL 实现。</summary>
    Public NotInheritable Class PclGameSessionLauncher
        Implements IGameSessionLauncher

        ''' <summary>Java 可执行文件路径（请求里没指定时用它）。</summary>
        Public Property JavaExecutable As String = "javaw.exe"

        Public Property JavaFolder As String = String.Empty

        Public Property JavaVersion As Version = New Version(17, 0)

        Public Function StartAsync(Request As LaunchRequest,
                                   cancellationToken As CancellationToken) As Task(Of IGameSession) _
            Implements IGameSessionLauncher.StartAsync

            If Request Is Nothing Then Throw New ArgumentNullException(NameOf(Request))

            Dim work As Func(Of IGameSession) = Function() StartCore(Request)
            Return Task.Run(work, cancellationToken)
        End Function

        Private Function StartCore(Request As LaunchRequest) As IGameSession
            Dim versionFolder = Path.Combine(Request.MinecraftDirectory, "versions", Request.VersionId)
            If Not Directory.Exists(versionFolder) Then
                Throw New DirectoryNotFoundException("未找到版本目录：" & versionFolder)
            End If

            ' 同上：PCL 垫片触碰 WPF，必须在 STA 线程
            Dim command = StaRunner.Run(
                Function()
                    ' 必须在 New McInstance 之前摆好（合并 inheritsFrom 的父版本 JSON 要用它）
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

            ' 同 PclGameLauncher：工作目录必须是版本目录（理由见那里的注释）
            Dim info As New ProcessStartInfo(If(Request.JavaExecutable, JavaExecutable), command) With {
                .WorkingDirectory = versionFolder,
                .UseShellExecute = False,
                .CreateNoWindow = True,
                .RedirectStandardOutput = True,
                .RedirectStandardError = True
            }

            ' 带控制台的启动也要留同一份记录：出问题时能直接对比"命令行到底发了什么"。
            Dim launchLog = PclLaunchLog.Write(info, Request)

            Dim child = Process.Start(info)
            If child Is Nothing Then Throw New InvalidOperationException("进程未能启动。")

            ' 退出码补进同一份记录（后台等，不阻塞）
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

            Return New PclGameSession(child)
        End Function

    End Class

End Namespace
