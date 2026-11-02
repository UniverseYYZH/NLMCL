using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NLMCL.App.ViewModels;
using Xunit;

namespace NLMCL.Tests
{
    /// <summary>
    /// 守卫测试：`[RelayCommand]` 必须真的生成出对应的 `XxxCommand` 属性。
    ///
    /// 背景：曾经有一次脚本补丁把 `[RelayCommand]` 写到了 `///` 注释后面，
    /// 特性被注释吞掉，命令根本没生成——XAML 里绑定的 Command 变成 null，
    /// 界面上表现为"点了没反应"，而构建、测试全绿。这条测试就是为了让这种错误编译得过也跑不过。
    /// </summary>
    public class RelayCommandGuardTests
    {
        private static readonly Assembly ViewModelsAssembly = typeof(MainViewModel).Assembly;

        [Fact]
        public void Every_relay_command_method_has_a_generated_command_property()
        {
            List<string> missing = new List<string>();

            foreach (Type type in ViewModelsAssembly.GetTypes())
            {
                if (!type.IsClass || type.IsAbstract)
                {
                    continue;
                }

                foreach (MethodInfo method in type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    bool hasRelayCommand = method.GetCustomAttributes(inherit: false)
                        .Any(attribute => attribute.GetType().Name == "RelayCommandAttribute");

                    if (!hasRelayCommand)
                    {
                        continue;
                    }

                    // 生成器把 XxxAsync 命名为 XxxCommand
                    string baseName = method.Name.EndsWith("Async", StringComparison.Ordinal)
                        ? method.Name.Substring(0, method.Name.Length - "Async".Length)
                        : method.Name;

                    string expected = baseName + "Command";

                    if (type.GetProperty(expected, BindingFlags.Public | BindingFlags.Instance) == null)
                    {
                        missing.Add(type.Name + "." + method.Name + " → 缺少 " + expected);
                    }
                }
            }

            Assert.True(missing.Count == 0,
                "以下 [RelayCommand] 没有生成命令属性（检查特性是否被注释/写错位置）：" + Environment.NewLine +
                string.Join(Environment.NewLine, missing));
        }

        [Fact]
        public void No_relay_command_attribute_is_hidden_inside_a_comment()
        {
            string? sourceDirectory = FindViewModelsSourceDirectory();

            if (sourceDirectory == null)
            {
                return; // 找不到源码目录时跳过（发布产物里的测试运行）
            }

            List<string> offenders = new List<string>();

            foreach (string file in Directory.GetFiles(sourceDirectory, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                    file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);

                for (int index = 0; index < lines.Length; index++)
                {
                    string trimmed = lines[index].TrimStart();

                    // 行首是 /// 却在同一行出现特性 → 特性在注释里，等于没写
                    if (trimmed.StartsWith("///", StringComparison.Ordinal) &&
                        trimmed.IndexOf("[RelayCommand]", StringComparison.Ordinal) >= 0)
                    {
                        offenders.Add(Path.GetFileName(file) + ":" + (index + 1));
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "以下位置的 [RelayCommand] 被写在 /// 注释里，不会生效：" + string.Join(", ", offenders));
        }

        /// <summary>从测试程序集往上找 src\NLMCL.App.ViewModels。</summary>
        private static string? FindViewModelsSourceDirectory()
        {
            DirectoryInfo? current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            for (int depth = 0; depth < 8 && current != null; depth++)
            {
                string candidate = Path.Combine(current.FullName, "src", "NLMCL.App.ViewModels");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            return null;
        }
    }
}
