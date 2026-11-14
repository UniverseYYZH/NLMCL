using System;
using System.IO;

namespace NLMCL.Core.Runtime
{
    /// <summary>
    /// 运行环境探测。低完整性/受控目录等情况下，启动器可能连自己的游戏目录都写不了；
    /// 提前给出提示，比等到启动 Forge 或安装整合包时才报 AccessDenied 更容易理解。
    /// </summary>
    public static class EnvironmentProbe
    {
        public static bool CanWriteDirectory(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            if (File.Exists(directory))
            {
                return false;
            }

            if (!Directory.Exists(directory))
            {
                // 目录尚未创建时不做判断，避免首次启动误报；真正创建时再处理异常。
                return true;
            }

            string probe = Path.Combine(directory, ".nlmcl-write-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                {
                }

                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
