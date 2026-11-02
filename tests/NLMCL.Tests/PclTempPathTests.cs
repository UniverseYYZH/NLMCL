using System;
using System.IO;
using NLMCL.Core.Pcl.Vb;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 回归：PCL 的临时目录来自设置键 SystemSystemCache（空串 = 用 %TEMP%\PCL\）。
    /// 我们曾把默认值写成布尔 True，`Get(Of String)` 会把它转成字面量 "True"，
    /// 结果 PathTemp = "True\"（相对路径），HTTP 缓存在工作区里建了个 True\Cache\Http。
    /// </summary>
    public class PclTempPathTests
    {
        [Fact]
        public void Temp_path_falls_back_to_user_temp_folder()
        {
            string expected = Path.Combine(Path.GetTempPath(), "PCL") + Path.DirectorySeparatorChar;

            Assert.Equal(
                expected.TrimEnd(Path.DirectorySeparatorChar),
                ModBase.PathTemp.TrimEnd(Path.DirectorySeparatorChar),
                ignoreCase: true);

            Assert.False(
                ModBase.PathTemp.StartsWith("True", StringComparison.OrdinalIgnoreCase),
                "PathTemp 又被解析成了相对路径 'True\\'：" + ModBase.PathTemp);
        }
    }
}
