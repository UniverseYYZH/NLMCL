namespace NLMCL.App.ViewModels
{
    /// <summary>版本详情浮层中的一行标签值。</summary>
    public sealed class VersionDetailsLine
    {
        public VersionDetailsLine(string label, string value)
        {
            Label = label;
            Value = value;
        }

        public string Label { get; }

        public string Value { get; }
    }
}
