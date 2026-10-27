namespace KSA.Core
{
    /// <summary>Build information for the portable agency core.</summary>
    public static class CoreInfo
    {
        /// <summary>Semantic version of KSA.Core as "major.minor.patch", e.g. "0.1.0".</summary>
        public static string Version
        {
            get
            {
                System.Version v = typeof(CoreInfo).Assembly.GetName().Version;
                return v.ToString(3);
            }
        }
    }
}
