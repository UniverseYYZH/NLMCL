namespace NLMCL.Abstractions
{
    /// <summary>
    /// PCL 的 ToolDownloadSpeed 编码与 MB/s 的映射。
    /// 0–14 → 0.1–1.5 MB/s；15–31 → 2–10 MB/s；32–41 → 11–20 MB/s；42 → 不限速。
    /// </summary>
    public static class DownloadSpeedLimits
    {
        public const int Unlimited = 0;

        public static int ToPclSetting(int megabytesPerSecond)
        {
            switch (megabytesPerSecond)
            {
                case 0:
                    return 42;
                case 1:
                    return 9;
                case 2:
                    return 15;
                case 5:
                    return 21;
                case 10:
                    return 31;
                default:
                    return 42;
            }
        }

        public static int ToMegabytesPerSecond(int pclSetting)
        {
            if (pclSetting <= 0 || pclSetting > 41)
            {
                return 0;
            }

            if (pclSetting <= 14)
            {
                return (pclSetting + 1) / 10;
            }

            if (pclSetting <= 31)
            {
                return (pclSetting - 11) / 2;
            }

            return pclSetting - 21;
        }
    }
}
