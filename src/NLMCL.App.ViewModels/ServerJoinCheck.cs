using System;

namespace NLMCL.App.ViewModels
{
    /// <summary>
    /// "进服前检查"的一行。
    ///
    /// 目的：让新玩家在点按钮之前就看出缺什么（账号 / 客户端 / 模组 / 内存 / 整合包），
    /// 而不是点下去等半天才看到一句"进模组服失败"。
    /// </summary>
    public sealed class ServerJoinCheck
    {
        public ServerJoinCheck(string label, string detail, bool ok)
        {
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            IsOk = ok;
        }

        /// <summary>检查项名称，例如"账号"。</summary>
        public string Label { get; }

        /// <summary>具体说明，例如"ZW_Official（外置）"。</summary>
        public string Detail { get; }

        public bool IsOk { get; }

        /// <summary>✓（通过）。</summary>
        public bool IsOkGlyph => IsOk;

        /// <summary>⚠（待处理）。</summary>
        public bool IsWarnGlyph => !IsOk;

        /// <summary>整行的显示文本。</summary>
        public string Text => Label + "：" + Detail;

        public override string ToString() => (IsOk ? "✓ " : "⚠ ") + Text;
    }
}
