' ============================================================================
' 从 PCL 的 ModMinecraft.vb **原样切分**出来的 McInstance 类（B1 方案）
'
' 上游：Plain Craft Launcher 2\Modules\Minecraft\ModMinecraft.vb
'        原始行号 218–867（共 650 行）
' 切分原因：McInstance 没有独立文件，与 ModMinecraft 的模块级 UI 代码（登录、页面跳转、
'           资源广场、动画开关、配置）同处一文件，导致整个文件无法脱离 UI 编译。
'           切分只改变**存放位置**，不改变任何一行代码。
' 维护：上游更新后需按行号重新切分，见 Vendor/README.md「更新方式」。
' ============================================================================

    Public Class McInstance

        Public Enum McInstanceState
            [Error]
            Original
            Snapshot
            Fool
            OptiFine
            Old
            Forge
            NeoForge
            LiteLoader
            Fabric
        End Enum

        ''' <summary>
        ''' 版本文件夹路径，以 \ 结尾。
        ''' </summary>
        Public ReadOnly Property PathVersion As String
        ''' <summary>
        ''' 应用版本隔离后，该版本所对应的 Minecraft 根文件夹，以 \ 结尾。
        ''' </summary>
        Public ReadOnly Property PathIndie As String
            Get
                If Not Settings.HasSaved("VersionArgumentIndieV2", Instance:=Me) Then
                    If Not IsLoaded Then Load()
                    '决定该版本是否应该被隔离
                    Dim ShouldBeIndie =
                    Function() As Boolean
                        '从老的版本独立设置中迁移：-1 未决定，0 使用全局设置，1 手动开启，2 手动关闭
                        If Settings.HasSaved("VersionArgumentIndie", Instance:=Me) AndAlso Settings.Get(Of Integer)("VersionArgumentIndie", Instance:=Me) > 0 Then
                            Logger.Info($"版本隔离初始化（{Name}）：从老的版本独立设置中迁移")
                            Return Settings.Get(Of Integer)("VersionArgumentIndie", Instance:=Me) = 1
                        End If
                        '若版本文件夹下包含 mods 或 saves 文件夹，则自动开启版本隔离
                        Dim ModFolder = DirectoryUtils.GetInfo(PathVersion & "mods\")
                        Dim SaveFolder = DirectoryUtils.GetInfo(PathVersion & "saves\")
                        If (ModFolder.Exists AndAlso ModFolder.EnumerateFiles.Any) OrElse (SaveFolder.Exists AndAlso SaveFolder.EnumerateDirectories.Any) Then
                            Logger.Info($"版本隔离初始化（{Name}）：版本文件夹下存在 mods 或 saves 文件夹，自动开启")
                            Return True
                        End If
                        '根据全局的默认设置决定是否隔离
                        Dim IsRelease As Boolean = State <> McInstanceState.Fool AndAlso State <> McInstanceState.Old AndAlso State <> McInstanceState.Snapshot
                        Logger.Info($"版本隔离初始化（{Name}）：从全局默认设置中（{Settings.Get(Of Integer)("LaunchArgumentIndieV2")}）判断，State {State}，IsRelease {IsRelease}，Modable {Modable}")
                        Select Case Settings.Get(Of Integer)("LaunchArgumentIndieV2")
                            Case 0 '关闭
                                Return False
                            Case 1 '仅隔离可安装 Mod 的版本
                                Return Modable
                            Case 2 '仅隔离非正式版
                                Return Not IsRelease
                            Case 3 '隔离非正式版与可安装 Mod 的版本
                                Return Not IsRelease OrElse Modable
                            Case Else '隔离所有版本
                                Return True
                        End Select
                    End Function
                    Settings.Set("VersionArgumentIndieV2", ShouldBeIndie(), Instance:=Me)
                End If
                Return If(Settings.Get(Of Boolean)("VersionArgumentIndieV2", Instance:=Me), PathVersion, McFolderSelected)
            End Get
        End Property

        ''' <summary>
        ''' 该版本的版本文件夹名称。
        ''' </summary>
        Public ReadOnly Property Name As String
            Get
                If _Name Is Nothing AndAlso Not PathVersion = "" Then _Name = PathUtils.GetLastPart(PathVersion)
                Return _Name
            End Get
        End Property
        Private _Name As String = Nothing

        ''' <summary>
        ''' 显示的描述文本。
        ''' </summary>
        Public Info As String = "该版本未被加载，请向作者反馈此问题"

        ''' <summary>
        ''' 该版本的列表检查原始结果，不受自定义影响。
        ''' </summary>
        Public State As McInstanceState = McInstanceState.Error
        ''' <summary>
        ''' 显示的版本图标。
        ''' </summary>
        Public Logo As String
        ''' <summary>
        ''' 是否为收藏的版本。
        ''' </summary>
        Public IsStar As Boolean = False
        ''' <summary>
        ''' 强制版本分类，0 为未启用，1 为隐藏，2 及以上为其他普通分类。
        ''' </summary>
        Public DisplayType As McInstanceCardType = McInstanceCardType.Auto
        ''' <summary>
        ''' 该版本是否可以安装 Mod。
        ''' </summary>
        Public ReadOnly Property Modable As Boolean
            Get
                If Not IsLoaded Then Load()
                Return Version.ModLoaders.Flags.Any OrElse DisplayType = McInstanceCardType.API '#223
            End Get
        End Property
        ''' <summary>
        ''' 版本信息。
        ''' </summary>
        Public Property Version As McVersion
            Get
                If _Version IsNot Nothing Then Return _Version
                Try
                    Dim Result = New McVersion(Name, JsonObject, JsonVersion)
                    _Version = Result
                    If Result.VanillaName = "Unknown" Then Info = "PCL 无法识别该版本的 MC 版本号"
                Catch ex As Exception
                    Logger.Warn(ex, "识别 Minecraft 版本时出错")
                    _Version = New McVersion("Unknown")
                    Info = "无法识别：" & ex.Message
                End Try
                Return _Version
            End Get
            Set(value As McVersion)
                _Version = value
            End Set
        End Property
        Private _Version As McVersion = Nothing

        ''' <summary>
        ''' 版本的发布时间。
        ''' </summary>
        Public ReadOnly Property ReleaseTime As Date
            Get
                Return Version.ReleaseTime
            End Get
        End Property

        ''' <summary>
        ''' 获取该版本的 JSON 路径。
        ''' </summary>
        Public Function GetJsonPath() As String
            Dim JsonPath As String = PathVersion & Name & ".json"
            If Not FileUtils.Exists(JsonPath) Then
                '尝试寻找 JSON 文件
                JsonPath = Nothing
                For Each JsonCandidatePath In DirectoryUtils.EnumerateFiles(PathVersion, searchPattern:="*.json")
                    Try
                        Dim JsonCandidate As JObject = FileUtils.ReadAsJson(JsonCandidatePath)
                        If Not JsonCandidate.ContainsKey("mainClass") Then Continue For
                        If Not JsonCandidate.ContainsKey("type") Then Continue For
                        If Not JsonCandidate.ContainsKey("id") Then Continue For
                    Catch
                        Continue For
                    End Try
                    JsonPath = JsonCandidatePath
                    Exit For
                Next
                If JsonPath Is Nothing Then
                    Throw New Exception($"未找到版本 JSON 文件：{PathVersion}{Name}.json")
                Else
                    Logger.Warn($"未找到同名版本 JSON，自动换用 {JsonPath}")
                End If
            End If
            Return JsonPath
        End Function
        ''' <summary>
        ''' 该版本的 Json 文本。会抛出异常。
        ''' </summary>
        Public Property JsonText As String
            Get
                '快速检查 JSON 是否以 { 开头、} 结尾；忽略空白字符
                Dim FastJsonCheck =
                Function(Json As String) As Boolean
                    Dim TrimedJson As String = Json.Trim()
                    Return TrimedJson.StartsWithF("{") AndAlso TrimedJson.EndsWithF("}")
                End Function
                If _JsonText Is Nothing Then
                    Dim JsonPath As String = GetJsonPath()
                    _JsonText = If(FileUtils.TryReadAsString(JsonPath), "")
                    '如果 ReadFile 失败会返回空字符串；这可能是由于文件被临时占用，故延时后重试
                    If Not FastJsonCheck(_JsonText) Then
                        If RunInUi() Then
                            Logger.Warn("版本 JSON 文件为空或有误，由于代码在主线程运行，将不再进行重试")
                            _JsonText.DeserializeJson() '触发异常
                        Else
                            Logger.Warn($"版本 JSON 文件为空或有误，将在 2s 后重试读取（{JsonPath}）")
                            Thread.Sleep(2000)
                            _JsonText = FileUtils.ReadAsString(JsonPath)
                            If Not FastJsonCheck(_JsonText) Then _JsonText.DeserializeJson() '触发异常
                        End If
                    End If
                End If
                Return _JsonText
            End Get
            Set(value As String)
                _JsonText = value
            End Set
        End Property
        Private _JsonText As String = Nothing
        ''' <summary>
        ''' 该版本的 Json 对象。
        ''' 若 Json 存在问题，在获取该属性时即会抛出异常。
        ''' </summary>
        Public Property JsonObject As JObject
            Get
                If _JsonObject Is Nothing Then
                    Dim Text As String = JsonText '触发 JsonText 的 Get 事件
                    Try
                        _JsonObject = Text.DeserializeJson()
                        '转换 HMCL 关键项
                        If _JsonObject.ContainsKey("patches") AndAlso Not _JsonObject.ContainsKey("time") Then
                            IsHmclFormatJson = True
                            '合并 Json
                            'Dim HasOptiFine As Boolean = False, HasForge As Boolean = False
                            Dim CurrentObject As JObject = Nothing
                            Dim SubjsonList As New List(Of JObject)
                            For Each Subjson As JObject In _JsonObject("patches")
                                SubjsonList.Add(Subjson)
                            Next
                            SubjsonList = SubjsonList.SortByComparison(
                                Function(Left, Right) Val(If(Left("priority"), "0").ToString) < Val(If(Right("priority"), "0").ToString))
                            For Each Subjson As JObject In SubjsonList
                                Dim Id As String = Subjson("id")
                                If Id IsNot Nothing Then
                                    '合并 Json
                                    Logger.Info($"合并 HMCL 分支项：{Id}")
                                    If CurrentObject IsNot Nothing Then
                                        CurrentObject.Merge(Subjson)
                                    Else
                                        CurrentObject = Subjson
                                    End If
                                Else
                                    Logger.Info("存在为空的 HMCL 分支项")
                                End If
                            Next
                            _JsonObject = CurrentObject
                            '修改附加项
                            _JsonObject("id") = Name
                            If _JsonObject.ContainsKey("inheritsFrom") Then _JsonObject.Remove("inheritsFrom")
                        End If
                        '与继承版本合并
                        Dim InstanceNameInheritFrom = Nothing
                        Try
                            InstanceNameInheritFrom = If(_JsonObject("inheritsFrom") Is Nothing, "", _JsonObject("inheritsFrom").ToString)
                            If InstanceNameInheritFrom = Name Then
                                Logger.Warn($"自引用的继承版本：{Name}")
                                InstanceNameInheritFrom = ""
                                Exit Try
                            End If
Recheck:
                            If InstanceNameInheritFrom <> "" Then
                                Dim InheritInstance As New McInstance(InstanceNameInheritFrom)
                                '继续循环
                                If InheritInstance.InheritName = InstanceNameInheritFrom Then Throw New Exception("版本依赖项出现嵌套：" & InstanceNameInheritFrom)
                                InstanceNameInheritFrom = InheritInstance.InheritName
                                '合并 Libraries 项：子版本放在前面，父版本放在后面（5978#：如果多个 jar 包中含有相同的类，Java 8 和之前的版本按照 -cp 指定的顺序选择第一个）
                                Dim CurrentLib As JArray = _JsonObject("libraries").DeepClone()
                                For Each LibToken In InheritInstance.JsonObject("libraries")
                                    CurrentLib.Add(LibToken)
                                Next
                                '合并其他项：子版本优先于父版本
                                InheritInstance.JsonObject.Merge(_JsonObject)
                                _JsonObject = InheritInstance.JsonObject
                                _JsonObject("libraries") = CurrentLib
                                GoTo Recheck
                            End If
                        Catch ex As Exception
                            Logger.Warn(ex, $"合并版本依赖项 JSON 失败（{If(InstanceNameInheritFrom, "null")}）")
                        End Try
                    Catch ex As Exception
                        Throw New Exception("初始化版本 JSON 时失败（" & If(Name, "null") & "）", ex)
                    End Try
                End If
                Return _JsonObject
            End Get
            Set(value As JObject)
                _JsonObject = value
            End Set
        End Property
        Private _JsonObject As JObject = Nothing
        ''' <summary>
        ''' 是否为旧版 Json 格式。
        ''' </summary>
        Public ReadOnly Property IsOldJson As Boolean
            Get
                Return JsonObject("minecraftArguments") IsNot Nothing AndAlso JsonObject("minecraftArguments") <> ""
            End Get
        End Property
        ''' <summary>
        ''' Json 是否为 HMCL 格式。
        ''' </summary>
        Public Property IsHmclFormatJson As Boolean = False

        ''' <summary>
        ''' 版本 jar 中的 version.json 文件对象。
        ''' 若没有则返回 Nothing。
        ''' </summary>
        Public ReadOnly Property JsonVersion As JObject
            Get
                Static JsonVersionInited As Boolean = False
                If Not JsonVersionInited Then
                    JsonVersionInited = True
                    Try
                        If Not FileUtils.Exists($"{PathVersion}{Name}.jar") Then Exit Try
                        Using JarArchive = FileUtils.OpenZip(PathVersion & Name & ".jar")
                            Dim VersionJson As ZipArchiveEntry = JarArchive.GetEntry("version.json")
                            If VersionJson Is Nothing Then Exit Try
                            Using VersionJsonStream As New StreamReader(VersionJson.Open)
                                _JsonVersion = VersionJsonStream.ReadToEnd().DeserializeJson()
                            End Using
                        End Using
                    Catch ex As Exception
                        Logger.Warn(ex, $"从版本 jar 中读取 version.json 失败（{PathVersion}{Name}.jar）")
                    End Try
                End If
                Return _JsonVersion
            End Get
        End Property
        Private _JsonVersion As JObject = Nothing

        ''' <summary>
        ''' 该版本的依赖版本。若无依赖版本则为空字符串。
        ''' </summary>
        Public ReadOnly Property InheritName As String
            Get
                If _InheritName Is Nothing Then
                    _InheritName = If(JsonObject("inheritsFrom"), "").ToString
                    '由于过老的 LiteLoader 中没有 Inherits（例如 1.5.2），需要手动判断以获取真实继承版本
                    '此外，由于这里的加载早于版本种类判断，所以需要手动判断是否为 LiteLoader
                    '如果版本提供了不同的 Jar，代表所需的 Jar 可能已被更改，则跳过 Inherit 替换
                    If JsonText.Contains("liteloader") AndAlso Version.VanillaName <> Name AndAlso Not JsonText.Contains("logging") Then
                        If If(JsonObject("jar"), Version.VanillaName).ToString = Version.VanillaName Then _InheritName = Version.VanillaName
                    End If
                    'HMCL 版本无 Json
                    If IsHmclFormatJson Then _InheritName = ""
                End If
                Return _InheritName
            End Get
        End Property
        Private _InheritName As String = Nothing

        Public Config As JsonConfigProvider
        Public Sub ResetSettingsCache()
            IniClearCache(PathIndie & "options.txt")
            IniClearCache(PathVersion & "PCL\Setup.ini")
            Config.DiscardCache()
            For Each Entry In Settings.Entries.Values
                If Entry.Source = Settings.Sources.Instance Then Entry.ValueCache.Remove(PathVersion)
            Next
        End Sub

        ''' <summary>
        ''' 从版本名，或版本文件夹的完整路径初始化。
        ''' </summary>
        Public Sub New(NameOrFullPath As String)
            NameOrFullPath = PathUtils.RemoveExtendedPrefix(NameOrFullPath)
            PathVersion = PathUtils.AddSlashSuffix(If(NameOrFullPath.Contains(":"), "", McFolderSelected & "versions\") & NameOrFullPath)
            Config = New JsonConfigProvider(Path.Combine(PathVersion, "PCL\config.json"))
        End Sub

        ''' <summary>
        ''' 检查 Minecraft 版本，若检查通过 State 则为 Original 且返回 True。
        ''' </summary>
        Public Function Check() As Boolean

            '检查文件夹
            If Not DirectoryUtils.Exists(PathVersion) Then
                State = McInstanceState.Error
                Info = "未找到版本 " & Name
                Return False
            End If
            '确认 Json 可用性
            Try
                If JsonObject("mainClass") Is Nothing Then Throw New Exception("版本 JSON 中没有 mainClass 项！") '这里也会触发 JsonObject 的 Get 事件
            Catch ex As Exception
                Logger.Warn(ex, $"版本 JSON 可用性检查失败（{PathVersion}）")
                JsonText = ""
                JsonObject = Nothing
                Info = ex.Message
                State = McInstanceState.Error
                Return False
            End Try
            '检查版本号获取
            Try
                If String.IsNullOrEmpty(Version.VanillaName) Then Throw New Exception("无法获取版本号，结果为空")
            Catch ex As Exception
                Logger.Warn(ex, $"版本号获取失败（{Name}）")
                State = McInstanceState.Error
                Info = "版本号获取失败：" & ex.GetDisplay(False)
                Return False
            End Try
            '检查依赖版本
            Try
                If Not InheritName = "" Then
                    If Not FileUtils.Exists($"{PathUtils.RemoveLastPart(PathVersion)}\{InheritName}\{InheritName}.json") Then
                        State = McInstanceState.Error
                        Info = "需要安装 " & InheritName & " 作为前置版本"
                        Return False
                    End If
                End If
            Catch ex As Exception
                Logger.Warn(ex, $"依赖版本检查出错（{Name}）")
                State = McInstanceState.Error
                Info = "未知错误：" & ex.GetDisplay(False)
                Return False
            End Try

            State = McInstanceState.Original
            Return True
        End Function
        ''' <summary>
        ''' 加载 Minecraft 版本的详细信息。不使用其缓存，且会更新缓存。
        ''' </summary>
        Public Function Load() As McInstance
            Try
                '检查版本，若出错则跳过数据确定阶段
                If Not Check() Then GoTo ExitDataLoad
#Region "确定版本分类"
                Select Case Version.VanillaName '在获取 Version.Original 对象时会完成它的加载
                    Case "Unknown"
                        State = McInstanceState.Error
                    Case "Old"
                        State = McInstanceState.Old
                    Case Else
                        '愚人节与快照版本
                        If Version.IsFool Then
                            State = McInstanceState.Fool
                        ElseIf Version.IsSnapshot Then
                            State = McInstanceState.Snapshot
                        End If
                        'OptiFine
                        If Version.HasOptiFine Then
                            State = McInstanceState.OptiFine
                        End If
                        'LiteLoader
                        If Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) Then
                            State = McInstanceState.LiteLoader
                        End If
                        'Fabric、Forge、NeoForge
                        If Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then
                            State = McInstanceState.Fabric
                        ElseIf Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then
                            State = McInstanceState.Forge
                        ElseIf Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then
                            State = McInstanceState.NeoForge
                        End If
                End Select
#End Region
ExitDataLoad:
                '确定版本图标
                Logo = ReadIni(PathVersion & "PCL\Setup.ini", "Logo", "")
                If Logo = "" OrElse Not CType(ReadIni(PathVersion & "PCL\Setup.ini", "LogoCustom", False), Boolean) Then
                    Select Case State
                        Case McInstanceState.Original
                            Logo = PathImage & "Blocks/Grass.png"
                        Case McInstanceState.Snapshot
                            Logo = PathImage & "Blocks/CommandBlock.png"
                        Case McInstanceState.Old
                            Logo = PathImage & "Blocks/CobbleStone.png"
                        Case McInstanceState.Forge
                            Logo = PathImage & "Blocks/Anvil.png"
                        Case McInstanceState.NeoForge
                            Logo = PathImage & "Blocks/NeoForge.png"
                        Case McInstanceState.Fabric
                            Logo = PathImage & "Blocks/Fabric.png"
                        Case McInstanceState.OptiFine
                            Logo = PathImage & "Blocks/GrassPath.png"
                        Case McInstanceState.LiteLoader
                            Logo = PathImage & "Blocks/Egg.png"
                        Case McInstanceState.Fool
                            Logo = PathImage & "Blocks/GoldBlock.png"
                        Case Else
                            Logo = PathImage & "Blocks/RedstoneBlock.png"
                    End Select
                End If
                '确定版本描述
                RefreshInfo()
                '确定版本收藏状态
                IsStar = ReadIni(PathVersion & "PCL\Setup.ini", "IsStar", False)
                '确定版本显示种类
                DisplayType = ReadIni(PathVersion & "PCL\Setup.ini", "DisplayType", McInstanceCardType.Auto)
                '写入缓存
                If DirectoryUtils.Exists(PathVersion) Then
                    WriteIni(PathVersion & "PCL\Setup.ini", "State", State)
                    WriteIni(PathVersion & "PCL\Setup.ini", "Info", Info)
                    WriteIni(PathVersion & "PCL\Setup.ini", "Logo", Logo)
                End If
                If State <> McInstanceState.Error AndAlso Version.Reliable Then
                    WriteIni(PathVersion & "PCL\Setup.ini", "ReleaseTime", ReleaseTime.ToString("yyyy'-'MM'-'dd HH':'mm"))
                    WriteIni(PathVersion & "PCL\Setup.ini", "VersionFabric", If(Version.ModLoaders.HasFlagF(ModLoaders.Fabric), Version.ModLoaderVersions(ModLoaders.Fabric), Nothing))
                    WriteIni(PathVersion & "PCL\Setup.ini", "VersionOptiFine", If(Version.OptiFineAsMod, Nothing, Version.OptiFine))
                    WriteIni(PathVersion & "PCL\Setup.ini", "VersionLiteLoader", Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader))
                    WriteIni(PathVersion & "PCL\Setup.ini", "VersionForge", If(Version.ModLoaders.HasFlagF(ModLoaders.Forge), Version.ModLoaderVersions(ModLoaders.Forge), Nothing))
                    WriteIni(PathVersion & "PCL\Setup.ini", "VersionNeoForge", If(Version.ModLoaders.HasFlagF(ModLoaders.NeoForge), Version.ModLoaderVersions(ModLoaders.NeoForge), Nothing))
                    WriteIni(PathVersion & "PCL\Setup.ini", "VersionVanillaName", Version.VanillaName)
                    WriteIni(PathVersion & "PCL\Setup.ini", "VersionVanilla", Version.Vanilla.ToString)
                End If
            Catch ex As Exception
                Info = ex.GetDisplay(False)
                Logo = PathImage & "Blocks/RedstoneBlock.png"
                State = McInstanceState.Error
                Logger.Warn(ex, $"加载版本失败（{Name}）")
            Finally
                IsLoaded = True
            End Try
            Return Me
        End Function
        Public IsLoaded As Boolean = False
        ''' <summary>
        ''' 根据当前组件信息刷新版本描述。
        ''' </summary>
        Public Sub RefreshInfo()
            Dim CustomInfo As String = ReadIni(PathVersion & "PCL\Setup.ini", "CustomInfo")
            If CustomInfo <> "" Then
                Info = CustomInfo
            Else
                Info = VersionDisplayName()
                If State = McInstanceState.Fool Then
                    Info = GetMcFoolName(Version.VanillaName)
                ElseIf State <> McInstanceState.Error Then
                    If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Me) = 3 Then Info += ", 统一通行证验证"
                    If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Me) = 4 Then Info += ", Authlib 验证"
                End If
            End If
        End Sub
        ''' <summary>
        ''' 获取对版本信息的简短描述。
        ''' 例如 “快照 16w01a”、“原版 1.12.2, Forge 1.2.3”、“愚人节版本 2.0”。
        ''' </summary>
        Public Function VersionDisplayName() As String
            'Mod Loader 信息
            Dim ModLoaderInfo As String = ""
            If Version.ModLoaders.HasFlagF(ModLoaders.Forge) Then ModLoaderInfo += ", Forge" & If(Version.ModLoaderVersions(ModLoaders.Forge) = "未知版本", "", " " & Version.ModLoaderVersions(ModLoaders.Forge))
            If Version.ModLoaders.HasFlagF(ModLoaders.NeoForge) Then ModLoaderInfo += ", NeoForge" & If(Version.ModLoaderVersions(ModLoaders.NeoForge) = "未知版本", "", " " & Version.ModLoaderVersions(ModLoaders.NeoForge))
            If Version.ModLoaders.HasFlagF(ModLoaders.Fabric) Then ModLoaderInfo += ", Fabric" & If(Version.ModLoaderVersions(ModLoaders.Fabric) = "未知版本", "", " " & Version.ModLoaderVersions(ModLoaders.Fabric))
            If Version.HasOptiFine Then ModLoaderInfo += ", OptiFine" & If(Version.OptiFine = "未知版本", "", " " & Version.OptiFine.Replace("-", " ").Replace("_", " "))
            If Version.ModLoaders.HasFlagF(ModLoaders.LiteLoader) Then ModLoaderInfo += ", LiteLoader"
            '基础信息
            Dim Info As String
            Select Case State
                Case McInstanceState.Snapshot, McInstanceState.Original, McInstanceState.Forge, McInstanceState.NeoForge, McInstanceState.Fabric, McInstanceState.OptiFine, McInstanceState.LiteLoader
                    If Version.VanillaName.ContainsIgnoreCase("pre") Then
                        Info = "预发布版 " & Version.VanillaName
                    ElseIf Version.VanillaName.ContainsIgnoreCase("rc") Then
                        Info = "发布候选 " & Version.VanillaName
                    ElseIf Version.VanillaName.ContainsIgnoreCase("experimental") Then
                        Info = "实验性快照" & Version.VanillaName
                    ElseIf Version.VanillaName = "pending" Then
                        Info = "实验性快照"
                    ElseIf Version.IsSnapshot Then
                        Info = If(Version.Reliable, "快照版 " & Version.VanillaName.Replace("-snapshot", ""), "快照版")
                    Else
                        Info = If(Version.Reliable, "正式版 " & Version.VanillaName, "正式版")
                    End If
                Case McInstanceState.Old
                    Info = "远古版本"
                Case McInstanceState.Fool
                    Info = "愚人节版本 " & Version.VanillaName
                Case McInstanceState.Error
                    Return Me.Info '已有错误信息
                Case Else
                    Return "发生了未知错误，请向作者反馈此问题"
            End Select
            Return (Info & ModLoaderInfo).Replace("_", "-")
        End Function

        Public Function ToListItem(Optional ContentHandler As Action(Of MyListItem, EventArgs) = Nothing) As MyVirtualizingElement(Of MyListItem)
            Return New MyVirtualizingElement(Of MyListItem)(
            Function()
                Dim NewItem As New MyListItem With {
                    .Info = Info, .Height = 42, .Tag = Me, .SnapsToDevicePixels = True, .Type = MyListItem.CheckType.Clickable,
                    .ContentHandler = ContentHandler
                }
                '标题
                NewItem.Inlines.Clear()
                NewItem.Inlines.Add(New Run(Name))
                If ReadIni(PathVersion & "PCL\Setup.ini", "CustomInfo") <> "" Then '如果版本设置了自定义描述，在标题后面以淡灰色显示其版本号
                    NewItem.Inlines.Add(New Run("  |  " & VersionDisplayName()) With {.Foreground = New MyColor(215, 215, 215), .FontSize = 12})
                End If
                'Logo
                Try
                    If Logo.EndsWithF("PCL\Logo.png") Then
                        NewItem.Logo = PathVersion & "PCL\Logo.png" '修复老版本中，存储的自定义 Logo 使用完整路径，导致移动后无法加载的 Bug
                    Else
                        NewItem.Logo = Logo
                    End If
                Catch ex As Exception
                    Logger.Error(ex, "加载版本图标失败", LogBehavior.Toast)
                    NewItem.Logo = "pack://application:,,,/images/Blocks/RedstoneBlock.png"
                End Try
                Return NewItem
            End Function) With {.Height = 42}
        End Function

        ''' <summary>
        ''' 重命名 MC 版本。
        ''' </summary>
        Public Function Rename(NewName As String) As McInstance
            Dim OldName = Name
            Dim OldPath = PathVersion
            Dim NewPath = PathUtils.AddSlashSuffix(Path.Combine(PathUtils.RemoveLastPart(OldPath), NewName))
            '重新读取版本 JSON 信息，避免 JsonObject 中已被合并的项被重新存储
            Dim JsonObject As JObject
            Dim OldJsonPath = GetJsonPath()
            Try
                JsonObject = FileUtils.ReadAsJson(OldJsonPath)
            Catch ex As Exception
                Logger.Warn(ex, "在重命名读取 JSON 时失败")
                JsonObject = Me.JsonObject
            End Try
            '清理缓存
            Config.Save()
            ResetSettingsCache()
            '重命名主文件夹
            DirectoryUtils.Move(OldPath, NewPath)
            '重命名 jar 文件与 natives 文件夹
            '不能进行遍历重命名，否则在版本名很短的时候容易误伤其他文件（#6443）
            If DirectoryUtils.Exists($"{NewPath}{OldName}-natives") Then DirectoryUtils.Move($"{NewPath}{OldName}-natives", $"{NewPath}{NewName}-natives")
            If FileUtils.Exists($"{NewPath}{OldName}.jar") Then FileUtils.Move($"{NewPath}{OldName}.jar", $"{NewPath}{NewName}.jar")
            '替换版本设置文件中的路径
            If FileUtils.Exists(NewPath & "PCL\Setup.ini") Then
                FileUtils.Write(NewPath & "PCL\Setup.ini", FileUtils.ReadAsString(NewPath & "PCL\Setup.ini").Replace(OldPath, NewPath))
            End If
            '更改已选中的版本
            If ReadIni(McFolderSelected & "PCL.ini", "Version") = OldName Then WriteIni(McFolderSelected & "PCL.ini", "Version", NewName)
            '更新版本 Json
            Try
                JsonObject("id") = NewName
                FileUtils.Delete(NewPath & PathUtils.GetLastPart(OldJsonPath))
                FileUtils.Write(NewPath & NewName & ".json", JsonObject.ToString)
            Catch ex As Exception
                Logger.Warn(ex, "重命名版本 json 失败")
            End Try
            Return New McInstance(NewPath).Load()
        End Function

        '运算符支持
        Public Overrides Function Equals(obj As Object) As Boolean
            Dim version = TryCast(obj, McInstance)
            Return version IsNot Nothing AndAlso PathUtils.IsSame(PathVersion, version.PathVersion)
        End Function
        Public Shared Operator =(a As McInstance, b As McInstance) As Boolean
            If a Is Nothing AndAlso b Is Nothing Then Return True
            If a Is Nothing OrElse b Is Nothing Then Return False
            Return PathUtils.IsSame(a.PathVersion, b.PathVersion)
        End Operator
        Public Shared Operator <>(a As McInstance, b As McInstance) As Boolean
            Return Not (a = b)
        End Operator

    End Class

' ============================================================================
' 从 PCL 的 ModMinecraft.vb **原样切分**：McInstanceCardType 枚举
'
' 上游：Plain Craft Launcher 2\Modules\Minecraft\ModMinecraft.vb
'        原始行号 1348–1357（共 10 行）
' 切分原因：它是模块级声明，但被 McInstance.Load() / DisplayType 直接使用，
'           随 McInstance 一起切出才能脱离 ModMinecraft 编译。
' ============================================================================

    Public Enum McInstanceCardType
        Star = -1
        Auto = 0 '仅用于强制版本分类的自动
        Hidden = 1
        API = 2
        OriginalLike = 3
        Rubbish = 4
        Fool = 5
        [Error] = 6
    End Enum
