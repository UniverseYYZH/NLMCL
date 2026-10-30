' 垫片：PCL 的全局设置与安装标识。
'
' 上游 `Settings` 是 **Module ModMain 里的一个类实例**（不是 Module），
' 因为代码里出现了 `Settings.Sources.Instance` / `Settings.Entries` 这类嵌套类型引用——
' 而 VB 的 Module 不能声明嵌套类型。这里按同样形态复刻：
'   SettingsProvider（类，含嵌套 Sources / SettingEntry） + 模块级字段 Settings。
'
' 存储布局：%APPDATA%\NLCL\pcl_settings.json
'   全局键     → { "ToolDownloadThread": 6, ... }
'   实例级键   → { "instances": { "<实例名>": { "VersionArgumentIndie": 1 } } }
'
' 只覆盖下载栈与 McInstance 用到的键；缺省值取自 PCL 的默认表现。
' 上游：Plain Craft Launcher 2\Modules\ModMain.vb（Module ModMain）

Imports System.Collections.Generic
Imports System.IO
Imports Newtonsoft.Json.Linq

''' <summary>设置的提供者。上游同名类型在 ModMain.vb 里，这里只复刻被用到的表面。</summary>
Public Class SettingsProvider

    ''' <summary>设置项的来源。上游区分全局与实例级。</summary>
    Public Enum Sources
        [Default] = 0
        Instance = 1
    End Enum

    ''' <summary>设置项。上游还有 Value / DefaultValue 等，这里只保留缓存清理需要的成员。</summary>
    Public Class SettingEntry
        Public Property Source As Sources = Sources.Default
        ''' <summary>各实例缓存过的值，ResetSettingsCache 会按实例路径清理。</summary>
        Public ReadOnly Property ValueCache As New List(Of String)
    End Class

    ''' <summary>全部设置项。仅 ResetSettingsCache 使用。</summary>
    Public ReadOnly Property Entries As New Dictionary(Of String, SettingEntry)

    ' LaunchArgumentIndieV2 = 3 即「隔离非正式版与可安装 Mod 的版本」（PCL 的默认口径）。
    ' 不设这一项时 Get(Of Integer) 会拿到 0（=不隔离），新装的 Fabric 实例就会把
    ' .minecraft\mods 里的全局 mod 全加载进来，出现 loader 版本冲突 / 模组崩溃。
    Private ReadOnly Defaults As New Dictionary(Of String, Object) From {
        {"ToolDownloadThread", 6},
        {"ToolDownloadSpeed", 0},
        {"SystemDebugMode", False},
        {"SystemDebugDelay", False},
        {"SystemDebugSkipCopy", False},
        {"SystemSystemCache", ""},
        {"LaunchArgumentIndieV2", 3}
    }

    Private _cache As JObject
    Private ReadOnly _lock As New Object()

    Private ReadOnly Property Cache As JObject
        Get
            If _cache Is Nothing Then
                SyncLock _lock
                    If _cache Is Nothing Then _cache = LoadFromDisk()
                End SyncLock
            End If
            Return _cache
        End Get
    End Property

    Private ReadOnly Property FilePath As String
        Get
            Return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NLCL", "pcl_settings.json")
        End Get
    End Property

    Private Function LoadFromDisk() As JObject
        Try
            If File.Exists(FilePath) Then Return JObject.Parse(File.ReadAllText(FilePath))
        Catch ex As Exception
            ' 配置损坏不应让上层失败——退回默认值。
        End Try
        Return New JObject()
    End Function

    Private Sub Save()
        Try
            Dim dir = Path.GetDirectoryName(FilePath)
            If Not Directory.Exists(dir) Then Directory.CreateDirectory(dir)
            File.WriteAllText(FilePath, Cache.ToString())
        Catch ex As Exception
            ' 落盘失败不影响内存中的设置。
        End Try
    End Sub

    Private ReadOnly Property InstanceStore As JObject
        Get
            Dim node = Cache("instances")
            If node Is Nothing Then
                node = New JObject()
                Cache("instances") = node
            End If
            Return CType(node, JObject)
        End Get
    End Property

    Private Shared Function InstanceKey(Instance As McInstance) As String
        If Instance Is Nothing Then Return Nothing
        Return If(String.IsNullOrEmpty(Instance.Name), "unknown", Instance.Name)
    End Function

    ''' <summary>该键是否被显式写过（而不是取到默认值）。</summary>
    Public Function HasSaved(Key As String, Optional Instance As McInstance = Nothing) As Boolean
        Dim name = InstanceKey(Instance)
        If name IsNot Nothing Then
            Dim node = InstanceStore(name)
            If node Is Nothing Then Return False
            Return CType(node, JObject)(Key) IsNot Nothing
        End If
        Return Cache(Key) IsNot Nothing
    End Function

    ''' <summary>
    ''' 读取设置；缺失时返回默认值，再缺失则返回 T 的默认值。
    ''' <c>Get</c> 是 VB 保留字，必须方括号转义才能与上游同名（vendor 按此名调用）。
    ''' </summary>
    Public Function [Get](Of T)(Key As String, Optional Instance As McInstance = Nothing) As T
        Dim name = InstanceKey(Instance)
        Try
            Dim token As JToken = Nothing
            If name IsNot Nothing Then
                Dim node = InstanceStore(name)
                If node IsNot Nothing Then token = node(Key)
            Else
                token = Cache(Key)
            End If
            If token IsNot Nothing Then Return token.ToObject(Of T)()
        Catch ex As Exception
        End Try

        If name Is Nothing Then
            Dim fallback As Object = Nothing
            If Defaults.TryGetValue(Key, fallback) Then
                Try
                    Return CType(Convert.ChangeType(fallback, GetType(T)), T)
                Catch ex As Exception
                End Try
            End If
        End If

        ' PCL 的设置对字符串返回空串而不是 Nothing——上游有大量 `.Any` / `.Length`
        ' 之类按"永不为 null"写的代码，返回 Nothing 会直接炸。
        If GetType(T) Is GetType(String) Then Return CType(CObj(String.Empty), T)

        Return Nothing
    End Function

    ''' <summary>写入设置并立即落盘。<c>Set</c> 同样是 VB 保留字。</summary>
    Public Sub [Set](Key As String, Value As Object, Optional Instance As McInstance = Nothing)
        SyncLock _lock
            Dim name = InstanceKey(Instance)
            Dim token As JToken = If(Value Is Nothing, JValue.CreateNull(), JToken.FromObject(Value))

            If name IsNot Nothing Then
                Dim node = InstanceStore(name)
                If node Is Nothing Then
                    node = New JObject()
                    InstanceStore(name) = node
                End If
                CType(node, JObject)(Key) = token
            Else
                Cache(Key) = token
            End If

            If Not Entries.ContainsKey(Key) Then Entries(Key) = New SettingEntry With {.Source = Sources.Default}
            Save()
        End SyncLock
    End Sub

End Class

''' <summary>上游在 <c>Module ModMain</c> 里声明的全局设置实例。</summary>
Public Module PclSettings
    Public ReadOnly Settings As New SettingsProvider
End Module

''' <summary>
''' 安装标识。上游 ModMain.GetIdentify() 会生成/读取一个本机唯一的字符串，
''' 用于统计安装量与同步。这里只返回稳定占位值，M3 不依赖其语义。
''' </summary>
Public Module PclIdentity
    Public Function GetIdentify() As String
        Return "NLCL-0000-0000-0000"
    End Function
End Module
