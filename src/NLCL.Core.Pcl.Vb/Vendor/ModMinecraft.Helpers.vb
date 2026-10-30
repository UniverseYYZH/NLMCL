' ============================================================================
' 从 PCL 的 ModMinecraft.vb **原样切分**：模块级版本工具函数
'
' 上游：Plain Craft Launcher 2\Modules\Minecraft\ModMinecraft.vb
'        原始行号 1554–2152（共 599 行）
' 内容：McJsonRuleCheck / McLibListGet* / McLibNetFiles* / McLibGet /
'       ShouldIgnoreFileCheck / McAssetsGetIndex* / McAssetsFixList /
'       McDownloadClientUpdateHint / FilterAccessToken / FilterUserName
' M4 需要其中的库列表与资源索引函数来构建 classpath。
' 结构性改动（内容未变）：外层包 Module、开头补 #Region（上游该区块跨起点）。
' 其对 DlSource* 的依赖由 Shim/DlSourceShim.vb 提供（只走官方源）。
' ============================================================================

Public Module ModMinecraftHelpers

#Region "库文件（Libraries）"

    Public Function McJsonRuleCheck(RuleToken As JToken) As Boolean
        If RuleToken Is Nothing Then Return True

        '初始化
        Dim Required As Boolean = False
        For Each Rule As JToken In RuleToken

            '单条条件验证
            Dim IsRightRule As Boolean = True '是否为正确的规则
            If Rule("os") IsNot Nothing Then '操作系统
                If Rule("os")("name") IsNot Nothing Then '操作系统名称
                    Dim OsName As String = Rule("os")("name").ToString
                    If OsName = "unknown" Then
                    ElseIf OsName = "windows" Then
                        If Rule("os")("version") IsNot Nothing Then '操作系统版本
                            IsRightRule = IsRightRule AndAlso OSVersion.RegexCheck(Rule("os")("version").ToString)
                        End If
                    Else
                        IsRightRule = False
                    End If
                End If
                If Rule("os")("arch") IsNot Nothing Then '操作系统架构
                    IsRightRule = IsRightRule AndAlso Rule("os")("arch").ToString <> "x86" AndAlso Not Rule("os")("arch").Contains("arm")
                End If
            End If
            If Not IsNothing(Rule("features")) Then '标签
                IsRightRule = IsRightRule AndAlso IsNothing(Rule("features")("is_demo_user")) '反选是否为 Demo 用户
                If CType(Rule("features"), JObject).Children.Any(Function(j As JProperty) j.Name.Contains("quick_play")) Then
                    IsRightRule = False '不开 Quick Play，让玩家自己加去
                End If
            End If

            '反选确认
            If Rule("action").ToString = "allow" Then
                If IsRightRule Then Required = True 'allow
            Else
                If IsRightRule Then Required = False 'disallow
            End If

        Next
        Return Required
    End Function
    Private OSVersion As String = My.Computer.Info.OSVersion

    ''' <summary>
    ''' 递归获取 Minecraft 某一版本的完整支持库列表。
    ''' </summary>
    Public Function McLibListGet(Instance As McInstance, IncludeMainJar As Boolean) As List(Of McLibEntry)

        '获取当前支持库列表
        Logger.Info($"获取支持库列表：{Instance.Name}")
        Dim Result = McLibListGetWithJson(Instance.JsonObject)

        '需要添加原版 Jar
        If IncludeMainJar Then
            Dim RealInstance As McInstance
            Dim RequiredJar As String = Instance.JsonObject("jar")?.ToString
            If Instance.IsHmclFormatJson OrElse RequiredJar Is Nothing Then
                'HMCL 项直接使用自身的 Jar
                '根据 Inherit 获取最深层版本
                Dim OriginalInstance As McInstance = Instance
                '1.17+ 的 Forge 不寻找 Inherit
                If Not ((Instance.Version.ModLoaders.HasFlagF(ModLoaders.Forge) OrElse Instance.Version.ModLoaders.HasFlagF(ModLoaders.NeoForge)) AndAlso Instance.Version.Vanilla.Major >= 17) Then
                    Do Until OriginalInstance.InheritName = ""
                        If OriginalInstance.InheritName = OriginalInstance.Name Then Exit Do
                        OriginalInstance = New McInstance(McFolderSelected & "versions\" & OriginalInstance.InheritName & "\")
                    Loop
                End If
                '需要新建对象，否则后面的 Check 会导致 McInstanceCurrent 的 State 变回 Original
                '复现：启动一个 Snapshot 版本
                RealInstance = New McInstance(OriginalInstance.PathVersion)
            Else
                'Json 已提供 Jar 字段，使用该字段的信息
                RealInstance = New McInstance(RequiredJar)
            End If
            Dim ClientUrl As String, ClientSHA1 As String
            '判断需求的版本是否存在
            '不能调用 RealVersion.Check()，可能会莫名其妙地触发 CheckPermission 正被另一进程使用，导致误判前置不存在
            If Not FileUtils.Exists(RealInstance.PathVersion & RealInstance.Name & ".json") Then
                RealInstance = Instance
                Logger.Warn($"可能缺少前置版本 {RealInstance.Name}，找不到对应的 json 文件")
            End If
            '获取详细下载信息
            If RealInstance.JsonObject("downloads") IsNot Nothing AndAlso RealInstance.JsonObject("downloads")("client") IsNot Nothing Then
                ClientUrl = RealInstance.JsonObject("downloads")("client")("url")
                ClientSHA1 = RealInstance.JsonObject("downloads")("client")("sha1")
            Else
                ClientUrl = Nothing
                ClientSHA1 = Nothing
            End If
            '把所需的原版 Jar 添加进去
            Result.Add(New McLibEntry With {.LocalPath = RealInstance.PathVersion & RealInstance.Name & ".jar", .Size = 0, .IsNatives = False, .Url = ClientUrl, .SHA1 = ClientSHA1})
        End If

        Return Result
    End Function
    ''' <summary>
    ''' 获取 Minecraft 某一版本忽视继承的支持库列表，即结果中没有继承项。
    ''' </summary>
    Public Function McLibListGetWithJson(JsonObject As JObject, Optional KeepSameNameDifferentVersionResult As Boolean = False, Optional CustomMcFolder As String = Nothing) As List(Of McLibEntry)
        CustomMcFolder = If(CustomMcFolder, McFolderSelected)

        '转换为 LibToken
        Dim BasicArray As New List(Of McLibEntry)
        For Each Library As JObject In CType(JsonObject("libraries"), JArray).Children

            '清理 null 项（BakaXL 会把没有的项序列化为 null，但会被 Newtonsoft 转换为 JValue，导致 Is Nothing = false；这导致了 #409）
            For i = Library.Properties.Count - 1 To 0 Step -1
                If Library.Properties(i).Value.Type = JTokenType.Null Then Library.Remove(Library.Properties(i).Name)
            Next

            '检查是否需要（Rules）
            If Not McJsonRuleCheck(Library("rules")) Then Continue For

            '获取根节点下的 url
            Dim RootUrl As String = Library("url")
            If RootUrl IsNot Nothing Then
                RootUrl += McLibGet(Library("name"), False, True, CustomMcFolder).Replace("\", "/")
            End If

            '根据是否本地化处理（Natives）
            If Library("natives") Is Nothing Then '没有 Natives
                Dim LocalPath As String
                LocalPath = McLibGet(Library("name"), CustomMcFolder:=CustomMcFolder)
                Try
                    If Library("downloads") IsNot Nothing AndAlso Library("downloads")("artifact") IsNot Nothing Then
                        BasicArray.Add(New McLibEntry With {
                            .OriginalName = Library("name"),
                            .Url = If(Library("downloads")("artifact")("url"), RootUrl),
                            .LocalPath = If(Library("downloads")("artifact")("path") Is Nothing, McLibGet(Library("name"),
                                CustomMcFolder:=CustomMcFolder), CustomMcFolder & "libraries\" & Library("downloads")("artifact")("path").ToString.Replace("/", "\")),
                            .Size = Val(Library("downloads")("artifact")("size").ToString),
                            .IsNatives = False,
                            .SHA1 = Library("downloads")("artifact")("sha1")?.ToString})
                    Else
                        BasicArray.Add(New McLibEntry With {.OriginalName = Library("name"), .Url = RootUrl, .LocalPath = LocalPath, .Size = 0, .IsNatives = False, .SHA1 = Nothing})
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, $"处理实际支持库列表失败（无 Natives，{If(Library("name"), "Nothing")}）")
                    BasicArray.Add(New McLibEntry With {.OriginalName = Library("name"), .Url = RootUrl, .LocalPath = LocalPath, .Size = 0, .IsNatives = False, .SHA1 = Nothing})
                End Try
            ElseIf Library("natives")("windows") IsNot Nothing Then '有 Windows Natives
                Dim NativeName As String = Library("natives")("windows")
                Try
                    If Library("downloads") IsNot Nothing AndAlso Library("downloads")("classifiers") IsNot Nothing AndAlso Library("downloads")("classifiers")(NativeName) IsNot Nothing Then
                        Dim NativeData = Library("downloads")("classifiers")(NativeName)
                        BasicArray.Add(New McLibEntry With {
                             .OriginalName = Library("name"),
                             .Url = If(NativeData("url"), RootUrl),
                             .LocalPath = If(NativeData("path") Is Nothing,
                                 McLibGet(Library("name"), CustomMcFolder:=CustomMcFolder).
                                    Replace(".jar", "-" & NativeName & ".jar").
                                    Replace("${arch}", If(Environment.Is64BitOperatingSystem, "64", "32")),
                                    $"{CustomMcFolder}libraries\{NativeData("path").ToString.Replace("/", "\")}"),
                             .Size = Val(NativeData("size").ToString),
                             .IsNatives = True,
                             .SHA1 = NativeData("sha1").ToString})
                    Else
                        BasicArray.Add(
                        New McLibEntry With {.OriginalName = Library("name"), .Url = RootUrl, .Size = 0, .IsNatives = True, .SHA1 = Nothing,
                            .LocalPath = McLibGet(Library("name"), CustomMcFolder:=CustomMcFolder).
                                 Replace(".jar", $"-{NativeName}.jar").
                                 Replace("${arch}", If(Environment.Is64BitOperatingSystem, "64", "32"))
                        })
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, $"处理实际支持库列表失败（有 Natives，{If(Library("name"), "Nothing")}）")
                    BasicArray.Add(
                        New McLibEntry With {.OriginalName = Library("name"), .Url = RootUrl, .Size = 0, .IsNatives = True, .SHA1 = Nothing,
                             .LocalPath = McLibGet(Library("name"), CustomMcFolder:=CustomMcFolder).
                                 Replace(".jar", $"-{NativeName}.jar").
                                 Replace("${arch}", If(Environment.Is64BitOperatingSystem, "64", "32"))
                        })
                End Try
            End If

        Next

        '去重
        Dim ResultArray As New Dictionary(Of String, McLibEntry)
        Dim GetVersion =
        Function(Token As McLibEntry) As String
            '测试例：
            'D:\Minecraft\test\libraries\net\neoforged\mergetool\2.0.0\mergetool-2.0.0-api.jar
            'D:\Minecraft\test\libraries\org\apache\commons\commons-collections4\4.2\commons-collections4-4.2.jar
            'D:\Minecraft\test\libraries\com\google\guava\guava\31.1-jre\guava-31.1-jre.jar
            Return PathUtils.GetLastPart(PathUtils.RemoveLastPart(Token.LocalPath))
        End Function
        For i = 0 To BasicArray.Count - 1
            Dim Key As String = BasicArray(i).Name & BasicArray(i).IsNatives.ToString
            If ResultArray.ContainsKey(Key) Then
                Dim BasicArrayVersion As String = GetVersion(BasicArray(i))
                Dim ResultArrayVersion As String = GetVersion(ResultArray(Key))
                If BasicArrayVersion <> ResultArrayVersion AndAlso KeepSameNameDifferentVersionResult Then
                    Logger.Info($"发现疑似重复的支持库：{BasicArray(i)} ({BasicArrayVersion}) 与 {ResultArray(Key)} ({ResultArrayVersion})")
                    ResultArray.Add(Key & GetUuid(), BasicArray(i))
                Else
                    Logger.Info($"发现重复的支持库：{BasicArray(i)} ({BasicArrayVersion}) 与 {ResultArray(Key)} ({ResultArrayVersion})，已忽略其中之一")
                    If McUtils.CompareVersionGE(BasicArrayVersion, ResultArrayVersion) Then
                        ResultArray(Key) = BasicArray(i)
                    End If
                End If
            Else
                ResultArray.Add(Key, BasicArray(i))
            End If
        Next
        Return ResultArray.Values.ToList
    End Function

    ''' <summary>
    ''' 获取版本所需支持库文件的 NetFile。
    ''' </summary>
    Public Function McLibNetFilesFromInstance(Instance As McInstance) As List(Of NetFile)
        If Not Instance.IsLoaded Then Instance.Load()
        Dim Result As New List(Of NetFile)

        '更新此方法时需要同步更新 Forge 新版自动安装方法！

        '主 Jar 文件
        Try
            Dim MainJar As NetFile = DlClientJarGet(Instance, True)
            If MainJar IsNot Nothing Then Result.Add(MainJar)
        Catch ex As Exception
            Logger.Warn(ex, "版本缺失主 jar 文件所必须的信息")
        End Try

        'Library 文件
        Result.AddRange(McLibNetFilesFromTokens(McLibListGet(Instance, False)))

        '统一通行证文件
        If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Instance) = 3 Then
            Dim TargetFile As String = PathPure.Value & "nide8auth.jar"
            Try
                '测试链接：https://auth.mc-user.com:233/00000000000000000000000000000000/
                Logger.Info("开始获取统一通行证下载信息")
                Dim DownloadInfo As JObject = NetRequestByClientRetry("https://auth.mc-user.com:233/" & Settings.Get(Of String)("VersionServerNide", Instance:=Instance), RequireJson:=True).DeserializeJson()
                Result.Add(New NetFile(
                    Urls:={"https://login.mc-user.com:233/index/jar"},
                    LocalPath:=TargetFile,
                    Checker:=New FileChecker With {.Hash = DownloadInfo("jarHash").ToString}))
            Catch ex As Exception
                If FileUtils.Exists(TargetFile) Then
                    Logger.Warn(ex, "获取统一通行证下载信息失败")
                Else
                    Throw New Exception("获取统一通行证下载信息失败", ex)
                End If
            End Try
        End If

        'Authlib-Injector 文件
        If Settings.Get(Of Integer)("VersionServerLogin", Instance:=Instance) = 4 Then
            Dim TargetFile = PathPure.Value & "authlib-injector.jar"
            Try
                Logger.Info("开始获取 Authlib-Injector 下载信息")
                Dim DownloadInfo As JObject = NetRequestByClientRetry(
                        "https://authlib-injector.yushi.moe/artifact/latest.json",
                        BackupUrl:="https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json", RequireJson:=True).DeserializeJson()
                Dim DownloadAddress As String = DownloadInfo("download_url").ToString.
                        Replace("bmclapi2.bangbang93.com/mirrors/authlib-injector", "authlib-injector.yushi.moe")
                Result.Add(New NetFile({
                    DownloadAddress,
                    DownloadAddress.Replace("authlib-injector.yushi.moe", "bmclapi2.bangbang93.com/mirrors/authlib-injector")
                }, TargetFile, New FileChecker With {.Hash = DownloadInfo("checksums")("sha256").ToString}))
            Catch ex As Exception
                If FileUtils.Exists(TargetFile) Then
                    Logger.Warn(ex, "获取 Authlib-Injector 下载信息失败")
                Else
                    Throw New Exception("获取 Authlib-Injector 下载信息失败", ex)
                End If
            End Try
        End If

        '跳过校验
        If ShouldIgnoreFileCheck(Instance) Then
            Logger.Warn("设置要求尽量忽略文件检查，这可能会保留有误的文件")
            Result = Result.Where(
            Function(f)
                If FileUtils.Exists(f.LocalPath) Then
                    Logger.Warn($"跳过下载的支持库文件：{f.LocalPath}")
                    Return False
                Else
                    Return True
                End If
            End Function).ToList
        End If

        Return Result
    End Function
    ''' <summary>
    ''' 将 McLibToken 列表转换为 NetFile。
    ''' </summary>
    Public Function McLibNetFilesFromTokens(Libs As List(Of McLibEntry), Optional CustomMcFolder As String = Nothing) As List(Of NetFile)
        CustomMcFolder = If(CustomMcFolder, McFolderSelected)
        Dim Result As New List(Of NetFile)
        '获取
        For Each Token As McLibEntry In Libs
            '检查器
            Dim Checker As FileChecker
            If Token.Name.ContainsIgnoreCase("labymod") Then
                Checker = New FileChecker '不检查 LabyMod 的文件，它们提供的文件校验信息是错的（#3225）
            Else
                Checker = New FileChecker With {.ActualSize = If(Token.Size = 0, -1, Token.Size), .Hash = Token.SHA1}
            End If
            'URL
            Dim Urls As New List(Of String)
            If Token.Url Is Nothing AndAlso Token.Name = "net.minecraftforge:forge:universal" Then '特判修复 Forge 部分 universal 文件缺失 URL（#5455）
                Token.Url = "https://maven.minecraftforge.net" & Token.LocalPath.Replace(CustomMcFolder & "libraries", "").Replace("\", "/")
            End If
            If Token.Url IsNot Nothing Then
                '获取 URL 的真实地址
                Urls.Add(Token.Url)
                If Token.Url.Contains("launcher.mojang.com/v1/objects") OrElse Token.Url.Contains("client.txt") OrElse
                   Token.Url.Contains(".tsrg") Then
                    Urls.AddRange(DlSourceLauncherOrMetaGet(Token.Url)) 'Mappings（#4425）
                End If
                If Token.Url.Contains("maven") Then
                    Dim BmclapiUrl As String =
                        Token.Url.Replace(Token.Url.Substring(0, Token.Url.IndexOfF("maven")), "https://bmclapi2.bangbang93.com/").Replace("maven.fabricmc.net", "maven").Replace("maven.minecraftforge.net", "maven").Replace("maven.neoforged.net/releases", "maven")
                    If DlSourcePreferMojang Then
                        Urls.Add(BmclapiUrl) '官方源优先
                    Else
                        Urls.Insert(0, BmclapiUrl) '镜像源优先
                    End If
                End If
            End If
            If Token.LocalPath.Contains("transformer-discovery-service") Then
                'Transformer 文件释放
                If Not FileUtils.Exists(Token.LocalPath) Then ExtractResources(Token.LocalPath, "Transformer")
                Logger.Warn("已自动释放 Transformer Discovery Service")
                Continue For
            ElseIf Token.LocalPath.Contains("optifine\OptiFine") Then
                'OptiFine 主 Jar
                Dim OptiFineBase As String = Token.LocalPath.Replace(CustomMcFolder & "libraries\optifine\OptiFine\", "").Split("_")(0) & "/" & PathUtils.GetLastPart(Token.LocalPath).Replace("-", "_")
                OptiFineBase = "/maven/com/optifine/" & OptiFineBase
                If OptiFineBase.Contains("_pre") Then OptiFineBase = OptiFineBase.Replace("com/optifine/", "com/optifine/preview_")
                Urls.Add("https://bmclapi2.bangbang93.com" & OptiFineBase)
            ElseIf Urls.Count <= 2 Then
                '普通文件
                Urls.AddRange(DlSourceLibraryGet("https://libraries.minecraft.net" & Token.LocalPath.Replace(CustomMcFolder & "libraries", "").Replace("\", "/")))
            End If
            Result.Add(New NetFile(Urls.Distinct, Token.LocalPath, Checker))
        Next
        '去重并返回
        Return Result.DistinctBy(Function(a) a.LocalPath).ToList
    End Function
    ''' <summary>
    ''' 获取对应的支持库文件地址。
    ''' </summary>
    ''' <param name="Original">原始地址，如 com.mumfrey:liteloader:1.12.2-SNAPSHOT。</param>
    ''' <param name="WithHead">是否包含 Lib 文件夹头部，若不包含，则会类似以 com\xxx\ 开头。</param>
    Public Function McLibGet(Original As String, Optional WithHead As Boolean = True, Optional IgnoreLiteLoader As Boolean = False, Optional CustomMcFolder As String = Nothing) As String
        CustomMcFolder = If(CustomMcFolder, McFolderSelected)
        '有时候原始的有四段，例如 net.minecraftforge:forge:1.21.4-54.0.34:client
        '文件夹名应该忽略最后一段（client），但文件名需要保留
        '参见 #7306（以及 #5376 也是因为这里没有处理好导致的）
        Dim Splited = Original.Split(":")
        Dim Result = If(WithHead, CustomMcFolder & "libraries\", "") &
            $"{Splited(0).Replace(".", "\")}\{Splited(1)}\{Splited(2)}\{Splited.Skip(1).Join("-")}.jar"
        Return Result
    End Function

    ''' <summary>
    ''' 检查设置，是否应当忽略文件检查？
    ''' </summary>
    Public Function ShouldIgnoreFileCheck(Instance As McInstance)
        Return Settings.Get(Of Boolean)("VersionAdvanceAssetsV2", Instance:=Instance) OrElse (Settings.Get(Of Integer)("VersionAdvanceAssets", Instance:=Instance) = 2)
    End Function

#End Region

#Region "资源文件（Assets）"

    '获取索引
    ''' <summary>
    ''' 获取某版本资源文件索引的对应 Json 项，详见版本 Json 中的 assetIndex 项。失败会抛出异常。
    ''' </summary>
    Public Function McAssetsGetIndex(Instance As McInstance, Optional ReturnLegacyOnError As Boolean = False, Optional CheckURLEmpty As Boolean = False) As JToken
        Dim AssetsName As String
        Try
            Do While True
                Dim Index As JToken = Instance.JsonObject("assetIndex")
                If Index IsNot Nothing AndAlso Index("id") IsNot Nothing Then Return Index
                If Instance.JsonObject("assets") IsNot Nothing Then AssetsName = Instance.JsonObject("assets").ToString
                If CheckURLEmpty AndAlso Index("url") IsNot Nothing Then Return Index
                '下一个版本
                If Instance.InheritName = "" Then Exit Do
                Instance = New McInstance(McFolderSelected & "versions\" & Instance.InheritName)
            Loop
        Catch
        End Try
        '无法获取到下载地址
        If ReturnLegacyOnError Then
            '返回 assets 文件名会由于没有下载地址导致全局失败
            'If AssetsName IsNot Nothing AndAlso AssetsName <> "legacy" Then
            '    Log("[Minecraft] 无法获取资源文件索引下载地址，使用 assets 项提供的资源文件名：" & AssetsName)
            '    Return ("{""id"": """ & AssetsName & """}").DeserializeJson()
            'Else
            Logger.Info("无法获取资源文件索引下载地址，使用默认的 legacy 下载地址")
            Return "{
                ""id"": ""legacy"",
                ""sha1"": ""c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729"",
                ""size"": 134284,
                ""url"": ""https://launchermeta.mojang.com/mc-staging/assets/legacy/c0fd82e8ce9fbc93119e40d96d5a4e62cfa3f729/legacy.json"",
                ""totalSize"": 111220701
            }".DeserializeJson()
            'End If
        Else
            Throw New Exception("该版本不存在资源文件索引信息")
        End If
    End Function
    ''' <summary>
    ''' 获取某版本资源文件索引名，优先使用 assetIndex，其次使用 assets。失败会返回 legacy。
    ''' </summary>
    Public Function McAssetsGetIndexName(Instance As McInstance) As String
        Try
            Do While True
                If Instance.JsonObject("assetIndex") IsNot Nothing AndAlso Instance.JsonObject("assetIndex")("id") IsNot Nothing Then
                    Return Instance.JsonObject("assetIndex")("id").ToString
                End If
                If Instance.JsonObject("assets") IsNot Nothing Then
                    Return Instance.JsonObject("assets").ToString
                End If
                If Instance.InheritName = "" Then Exit Do
                Instance = New McInstance(McFolderSelected & "versions\" & Instance.InheritName)
            Loop
        Catch ex As Exception
            Logger.Warn(ex, "获取资源文件索引名失败")
        End Try
        Return "legacy"
    End Function

    '获取列表
    Private Structure McAssetsToken
        ''' <summary>
        ''' 文件的完整本地路径。
        ''' </summary>
        Public LocalPath As String
        ''' <summary>
        ''' Json 中书写的源路径。例如 minecraft/sounds/mob/stray/death2.ogg 。
        ''' </summary>
        Public SourcePath As String
        ''' <summary>
        ''' 文件大小。若无有效数据即为 0。
        ''' </summary>
        Public Size As Long
        ''' <summary>
        ''' 文件的 Hash 校验码。
        ''' </summary>
        Public Hash As String

        Public Overrides Function ToString() As String
            Return StringUtils.FormatByteSize(Size) & " | " & LocalPath
        End Function
    End Structure
    ''' <summary>
    ''' 获取 Minecraft 的资源文件列表。失败会抛出异常。
    ''' </summary>
    Private Function McAssetsListGet(Instance As McInstance) As List(Of McAssetsToken)
        Dim IndexName = McAssetsGetIndexName(Instance)
        Try

            '初始化
            If Not FileUtils.Exists($"{McFolderSelected}assets\indexes\{IndexName}.json") Then Throw New FileNotFoundException("未找到 Asset Index", McFolderSelected & "assets\indexes\" & IndexName & ".json")
            Dim Result As New List(Of McAssetsToken)
            Dim Json As JObject = FileUtils.ReadAsJson($"{McFolderSelected}assets\indexes\{IndexName}.json")

            '读取列表
            For Each File As JProperty In Json("objects").Children
                Dim LocalPath As String
                If Json("map_to_resources") IsNot Nothing AndAlso Json("map_to_resources").ToObject(Of Boolean) Then
                    'Remap
                    LocalPath = $"{Instance.PathIndie}resources\{File.Name}"
                ElseIf Json("virtual") IsNot Nothing AndAlso Json("virtual").ToObject(Of Boolean) Then
                    'Virtual
                    LocalPath = $"{McFolderSelected}assets\virtual\legacy\{File.Name}"
                Else
                    '正常
                    LocalPath = $"{McFolderSelected}assets\objects\{Left(File.Value()("hash").ToString, 2)}\{File.Value()("hash")}"
                End If
                Result.Add(New McAssetsToken With {
                    .LocalPath = LocalPath.Replace("/", "\"),
                    .SourcePath = File.Name,
                    .Hash = File.Value()("hash").ToString,
                    .Size = File.Value()("size").ToString
                })
            Next
            Return Result

        Catch ex As Exception
            Logger.Warn(ex, $"获取资源文件列表失败：{IndexName}")
            Throw
        End Try
    End Function

    ''' <summary>
    ''' 获取版本资源文件所对应的 NetFile。
    ''' </summary>
    Public Function McAssetsFixList(Instance As McInstance, CheckHash As Boolean, Optional ByRef ProgressFeed As LoaderBase = Nothing) As List(Of NetFile)
        '如果需要检查 Hash，则留到下载时处理，以借助多线程加快检查速度
        If CheckHash Then
            Return McAssetsListGet(Instance).
                Select(Function(Token As McAssetsToken) New NetFile(
                    DlSourceAssetsGet($"https://resources.download.minecraft.net/{Left(Token.Hash, 2)}/{Token.Hash}"),
                    LocalPath:=Token.LocalPath,
                    Checker:=New FileChecker With {.ActualSize = If(Token.Size = 0, -1, Token.Size), .Hash = Token.Hash})).ToList
        End If
        '如果不检查 Hash，则立即处理
        Dim Result As New List(Of NetFile)
        Dim AssetsList As List(Of McAssetsToken)
        Try
            AssetsList = McAssetsListGet(Instance)
            Dim Token As McAssetsToken
            If ProgressFeed IsNot Nothing Then ProgressFeed.Progress = 0.04
            For i = 0 To AssetsList.Count - 1
                '初始化
                Token = AssetsList(i)
                If ProgressFeed IsNot Nothing Then ProgressFeed.Progress = 0.05 + 0.94 * i / AssetsList.Count
                '检查文件是否存在
                Dim File = FileUtils.GetInfo(Token.LocalPath)
                If File.Exists AndAlso (Token.Size = 0 OrElse Token.Size = File.Length) Then Continue For
                '文件不存在，添加下载
                Result.Add(New NetFile(DlSourceAssetsGet($"https://resources.download.minecraft.net/{Left(Token.Hash, 2)}/{Token.Hash}"), Token.LocalPath, New FileChecker With {.ActualSize = If(Token.Size = 0, -1, Token.Size), .Hash = Token.Hash}))
            Next
        Catch ex As Exception
            Logger.Warn(ex, "获取版本缺失的资源文件下载列表失败")
        End Try
        If ProgressFeed IsNot Nothing Then ProgressFeed.Progress = 0.99
        Return Result
    End Function

#End Region

    ''' <summary>
    ''' 发送 Minecraft 更新提示。
    ''' </summary>
    Public Sub McDownloadClientUpdateHint(VersionName As String, Json As JObject)
        Try

            '获取对应版本
            Dim Version As JToken = Nothing
            For Each Token In Json("versions")
                If Token("id") IsNot Nothing AndAlso Token("id").ToString = VersionName Then
                    Version = Token
                    Exit For
                End If
            Next
            '进行提示
            If Version Is Nothing Then Return
            Dim Time As Date = Version("releaseTime")
            Dim MsgBoxText As String = $"新版本：{VersionName}{vbCrLf}" &
                If((Date.Now - Time).TotalDays > 1, "更新时间：" & Time.ToString, "更新于：" & StringUtils.FormatTimeSpan(Time - Date.Now, False))
            Dim MsgResult = MyMsgBox(MsgBoxText, "Minecraft 更新提示", "确定", If(RestrictMcDownload, "", "下载"), If((Date.Now - Time).TotalHours > 3, "更新日志", ""),
                Button3Action:=Sub() McUpdateLogShow(Version))
            '弹窗结果
            If MsgResult = 2 Then
                '下载
                RunInUi(
                Sub()
                    PageDownloadInstall.VersionWaitingSelect = VersionName
                    FrmMain.PageChange(FormMain.PageType.Download, FormMain.PageSubType.DownloadInstall)
                End Sub)
            End If

        Catch ex As Exception
            Logger.Error(ex, $"Minecraft 更新提示发送失败（{If(VersionName, "Nothing")}）")
        End Try
    End Sub

    ''' <summary>
    ''' 打码字符串中的 AccessToken。
    ''' </summary>
    Public Function FilterAccessToken(Raw As String, FilterChar As Char) As String
        '打码 "accessToken " 后的内容
        If Raw.Contains("accessToken ") Then
            For Each Token In Raw.RegexSearch("(?<=accessToken ([^ ]{5}))[^ ]+(?=[^ ]{5})")
                Raw = Raw.Replace(Token, New String(FilterChar, Token.Count))
            Next
        End If
        '打码当前登录的结果
        Dim AccessToken As String = McLoginLoader.Output.AccessToken
        If AccessToken IsNot Nothing AndAlso AccessToken.Length >= 10 AndAlso Raw.ContainsIgnoreCase(AccessToken) AndAlso
            McLoginLoader.Output.Uuid <> McLoginLoader.Output.AccessToken Then 'UUID 和 AccessToken 一样则不打码
            Raw = Raw.Replace(AccessToken, Left(AccessToken, 5) & New String(FilterChar, AccessToken.Length - 10) & Right(AccessToken, 5))
        End If
        Return Raw
    End Function
    ''' <summary>
    ''' 打码字符串中的 Windows 用户名。
    ''' </summary>
    Public Function FilterUserName(Raw As String, FilterChar As Char) As String
        If Raw.Contains(":\Users\") Then
            Raw = RegularExpressions.Regex.Replace(Raw, "(?<=:\\Users\\)[^\\]+", Function(Token) New String(FilterChar, Token.Length))
        End If
        If Raw.Contains(":/Users/") Then
            Raw = RegularExpressions.Regex.Replace(Raw, "(?<=:/Users/)[^/]+", Function(Token) New String(FilterChar, Token.Length))
        End If
        Return Raw
    End Function

' ---- 切分：GetNativesFolder（ModLaunch 行 1726–1737）----
    Public Function GetNativesFolder(Optional Instance As McInstance = Nothing) As String
        If Instance Is Nothing Then Instance = McInstanceSelected
        Dim Result As String = PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Instance.PathVersion)) & Instance.Name & "-natives\"
        If Not (IsGBKEncoding OrElse Result.IsAsciiOnly()) Then
            Result = Paths.AppData & ".minecraft\bin\natives\"
            If Not Result.IsAsciiOnly() Then
                Result = OsDrive & "ProgramData\PCL\natives\"
            End If
        End If
        DirectoryUtils.Create(Result) '提前创建，这样 DirectoryUtils.Shorten 才有结果（否则在长路径下首次启动 Forge 会崩溃）
        Return PathUtils.AddSlashSuffix(PathUtils.ToShortPath(Result))
    End Function

End Module

' ---- 切分：McLibEntry（上游行 1499–1548）----
    Public Class McLibEntry
        ''' <summary>
        ''' 文件的完整本地路径。
        ''' </summary>
        Public LocalPath As String
        ''' <summary>
        ''' 文件大小。若无有效数据即为 0。
        ''' </summary>
        Public Size As Long = 0
        ''' <summary>
        ''' 是否为 Natives 文件。
        ''' </summary>
        Public IsNatives As Boolean = False
        ''' <summary>
        ''' 文件的 SHA1。
        ''' </summary>
        Public SHA1 As String = Nothing
        ''' <summary>
        ''' 由 Json 提供的 URL，若没有则为 Nothing。
        ''' </summary>
        Public Property Url As String
            Get
                Return _Url
            End Get
            Set(value As String)
                '孤儿 Forge 作者喜欢把没有 URL 的写个空字符串
                _Url = If(String.IsNullOrWhiteSpace(value), Nothing, value)
            End Set
        End Property
        Private _Url As String
        ''' <summary>
        ''' 原 Json 中 Name 项除去版本号部分的较前部分。可能为 Nothing。
        ''' </summary>
        Public ReadOnly Property Name As String
            Get
                If OriginalName Is Nothing Then Return Nothing
                Dim Splited As New List(Of String)(OriginalName.Split(":"))
                Splited.RemoveAt(2) 'Java 的此格式下版本号固定为第三段，第四段可能包含架构、分包等其他信息
                Return Splited.Join(":"c)
            End Get
        End Property
        ''' <summary>
        ''' 原 Json 中的 Name 项。
        ''' </summary>
        Public OriginalName As String

        Public Overrides Function ToString() As String
            Return If(IsNatives, "[Native] ", "") & StringUtils.FormatByteSize(Size) & " | " & LocalPath
        End Function
    End Class
