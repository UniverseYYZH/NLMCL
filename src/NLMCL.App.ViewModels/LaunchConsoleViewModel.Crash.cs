using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLMCL.Abstractions;

namespace NLMCL.App.ViewModels
{
    /// <summary>「启动与日志」页的崩溃分析部分（拆分到单独文件，保持主文件聚焦进程控制）。</summary>
    public sealed partial class LaunchConsoleViewModel
    {
        /// <summary>由组合根注入。</summary>
        public ICrashAnalyzer? CrashAnalyzer { get; set; }

        [ObservableProperty]
        private string _analysisText = string.Empty;

        [ObservableProperty]
        private bool _hasAnalysis;

        /// <summary>关闭崩溃分析卡片；日志区随即恢复完整高度。</summary>
        [RelayCommand]
        public void CloseAnalysis()
        {
            HasAnalysis = false;
            AnalysisText = string.Empty;
        }

        [RelayCommand]
        public async Task AnalyzeCrashAsync()
        {
            if (CrashAnalyzer == null)
            {
                AnalysisText = "崩溃分析不可用。";
                HasAnalysis = true;
                return;
            }

            if (string.IsNullOrWhiteSpace(VersionId) || string.IsNullOrWhiteSpace(GameDirectory))
            {
                AnalysisText = "先在「本地版本」选一个实例，再点崩溃分析。";
                HasAnalysis = true;
                return;
            }

            AnalysisText = "正在读取崩溃报告…";
            HasAnalysis = true;

            try
            {
                CrashAnalysis analysis = await CrashAnalyzer
                    .AnalyzeAsync(GameDirectory, VersionId, CancellationToken.None)
                    .ConfigureAwait(true);

                var builder = new System.Text.StringBuilder();
                builder.AppendLine("来源：" + (string.IsNullOrEmpty(analysis.Source) ? "（无）" : analysis.Source));
                builder.AppendLine(analysis.Summary);

                foreach (CrashMatch match in analysis.Matches)
                {
                    builder.AppendLine();
                    builder.AppendLine("● " + match.Title);
                    builder.AppendLine("  证据：" + match.Detail);
                    builder.AppendLine("  建议：" + match.Advice);
                }

                if (analysis.Suspects.Count > 0)
                {
                    builder.AppendLine();
                    builder.AppendLine("可疑模组：" + string.Join("、", analysis.Suspects));
                }

                if (!string.IsNullOrEmpty(analysis.Tail))
                {
                    builder.AppendLine();
                    builder.AppendLine("── 日志尾部 ──");
                    builder.AppendLine(analysis.Tail);
                }

                AnalysisText = builder.ToString();
            }
            catch (Exception ex)
            {
                AnalysisText = "分析失败：" + ex.Message;
            }
        }
    }
}
