using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NLCL.Abstractions;
using NLCL.Core.Installing;
using Xunit;

namespace NLCL.Tests
{
    public class JavaRequirementsTests
    {
        private static string NewRoot() =>
            Path.Combine(Path.GetTempPath(), "nlcl-jreq-" + Guid.NewGuid().ToString("N"));

        private static void WriteVersion(string root, string name, string json)
        {
            string folder = Path.Combine(root, "versions", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, name + ".json"), json, Encoding.UTF8);
        }

        [Fact]
        public void Reads_major_from_version_json()
        {
            string root = NewRoot();
            try
            {
                WriteVersion(root, "1.20.1", @"{ ""id"": ""1.20.1"", ""javaVersion"": { ""majorVersion"": 17, ""component"": ""java-runtime-gamma"" } }");

                Assert.Equal(17, JavaRequirements.ReadRequiredMajor(root, "1.20.1"));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Follows_inherits_from_when_child_has_no_java_version()
        {
            string root = NewRoot();
            try
            {
                WriteVersion(root, "1.20.1", @"{ ""javaVersion"": { ""majorVersion"": 17 } }");
                WriteVersion(root, "1.20.1-fabric", @"{ ""inheritsFrom"": ""1.20.1"" }");

                Assert.Equal(17, JavaRequirements.ReadRequiredMajor(root, "1.20.1-fabric"));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Returns_null_for_missing_or_broken_json()
        {
            string root = NewRoot();
            try
            {
                Assert.Null(JavaRequirements.ReadRequiredMajor(root, "不存在"));

                WriteVersion(root, "broken", "{ 这不是 json");
                Assert.Null(JavaRequirements.ReadRequiredMajor(root, "broken"));
            }
            finally
            {
                TryDelete(root);
            }
        }

        [Fact]
        public void Survives_inheritance_cycles()
        {
            string root = NewRoot();
            try
            {
                WriteVersion(root, "a", @"{ ""inheritsFrom"": ""b"" }");
                WriteVersion(root, "b", @"{ ""inheritsFrom"": ""a"" }");

                // 成环时返回 null 而不是死循环
                Assert.Null(JavaRequirements.ReadRequiredMajor(root, "a"));
            }
            finally
            {
                TryDelete(root);
            }
        }

        // 必须真实存在：Pick 会过滤掉可执行文件不存在的条目
        private static readonly JavaRuntime J8 = TestSupport.CreateJava(8);
        private static readonly JavaRuntime J17 = TestSupport.CreateJava(17);
        private static readonly JavaRuntime J21 = TestSupport.CreateJava(21);
        private static readonly JavaRuntime J25 = TestSupport.CreateJava(25);

        [Fact]
        public void Prefers_exact_major_over_newer()
        {
            JavaRuntime? picked = JavaRequirements.Pick(new[] { J8, J17, J25 }, 17);

            // 要 17 就必须给 17，不能因为 25 更新就给 25
            Assert.Same(J17, picked);
        }

        [Fact]
        public void Refuses_a_newer_java_instead_of_guessing()
        {
            // 只接受精确匹配：21 跑不了要求 17 的版本时，交给上层自动下载 Java 17，
            // 而不是拿 21 去赌（实测 25 这类"更新"的 Java 会在 LWJGL 上 native 崩溃）。
            Assert.Null(JavaRequirements.Pick(new[] { J21 }, 17));
        }

        /// <summary>
        /// 回归：1.20.1 要 Java 17，机器上只有 Java 25 时，旧 LWJGL 会在 25 上
        /// 触发 EXCEPTION_ACCESS_VIOLATION (jni_fast_GetLongField) 的 native 崩溃。
        /// 必须拒绝启动并提示装 Java 17，而不是"挑个更新的凑合用"。
        /// </summary>
        [Fact]
        public void Refuses_a_much_newer_java()
        {
            JavaRuntime? picked = JavaRequirements.Pick(new[] { J25 }, 17);

            Assert.Null(picked);
        }

        [Fact]
        public void Returns_null_when_nothing_meets_requirement()
        {
            JavaRuntime? picked = JavaRequirements.Pick(new[] { J8 }, 17);

            Assert.Null(picked);
        }

        [Fact]
        public void Without_requirement_picks_highest()
        {
            JavaRuntime? picked = JavaRequirements.Pick(new[] { J8, J17, J25 }, null);

            Assert.Same(J25, picked);
        }

        private static void TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
