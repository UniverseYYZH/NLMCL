using System;
using System.IO;
using Microsoft.Win32;

namespace NLMCL.App.Services;

/// <summary>把 nlmcl:// 注册到当前用户，不写 HKLM，不需要管理员权限。</summary>
public static class UrlProtocolRegistrar
{
    private const string ProtocolKey = @"Software\Classes\nlmcl";

    public static bool TryRegister(string executablePath, out string? error)
    {
        error = null;
        try
        {
            string fullPath = Path.GetFullPath(executablePath);
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(ProtocolKey, writable: true)
                ?? throw new InvalidOperationException("无法创建协议注册表项。");
            key.SetValue(string.Empty, "URL:NLMCL Invite");
            key.SetValue("URL Protocol", string.Empty);
            using (RegistryKey icon = key.CreateSubKey("DefaultIcon", writable: true))
            {
                icon.SetValue(string.Empty, "\"" + fullPath + "\",0");
            }

            using RegistryKey command = key.CreateSubKey(@"shell\open\command", writable: true);
            command.SetValue(string.Empty, "\"" + fullPath + "\" \"%1\"");
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            error = ex.Message;
            return false;
        }
    }
}
