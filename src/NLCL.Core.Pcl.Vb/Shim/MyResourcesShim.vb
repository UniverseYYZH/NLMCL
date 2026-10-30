' 垫片：VB 的 My.Resources。
'
' 上游是 VB 应用工程，自动生成 My.Resources（Resources.resx → Resources.Designer.vb）。
' 类库工程没有这个自动成员，而 ModBase.ExtractResources 会调用
'     My.Resources.ResourceManager.GetObject(ResourceName)
' 来释放内嵌资源（如 JavaWrapper.jar）。
'
' NLCL 目前没有内嵌这些资源，因此 GetObject 永远返回 Nothing，
' ExtractResources 会安全地什么都不做。日后若需要，把资源加到本工程并改 ResourceManager 的基名即可。
'
' 上游引用点：ModBase.vb L1068

Namespace My

    Public Class Resources

        ''' <summary>指向本程序集的资源管理器；没有资源时 GetObject 返回 Nothing。</summary>
        Public Shared ReadOnly ResourceManager As New System.Resources.ResourceManager(
            "NLCL.Core.Pcl.Vb.Resources", GetType(Resources).Assembly)

    End Class

End Namespace
