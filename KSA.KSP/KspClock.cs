namespace KSA.KSP
{
    /// <summary>Core's clock backed by the game's universal time and the player's calendar setting (Kerbin or Earth days).</summary>
    public sealed class KspClock : Core.IClock
    {
        public double UniversalTime
        {
            get { return HighLogic.LoadedScene == GameScenes.MAINMENU || HighLogic.CurrentGame == null ? 0 : Planetarium.GetUniversalTime(); }
        }

        public double SecondsPerDay
        {
            get { return KSPUtil.dateTimeFormatter != null ? KSPUtil.dateTimeFormatter.Day : 21600; }
        }

        public int DaysPerYear
        {
            get
            {
                if (KSPUtil.dateTimeFormatter == null || KSPUtil.dateTimeFormatter.Day <= 0) return 426;
                return KSPUtil.dateTimeFormatter.Year / KSPUtil.dateTimeFormatter.Day;
            }
        }
    }
}
