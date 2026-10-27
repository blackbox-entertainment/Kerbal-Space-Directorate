namespace KSA.Core
{
    /// <summary>Game time as the host sees it. KSA.KSP reads the game's universal time; tests use a fake.</summary>
    public interface IClock
    {
        /// <summary>Seconds since the campaign began.</summary>
        double UniversalTime { get; }
        /// <summary>Seconds in one day on the home world (21600 for a 6-hour day).</summary>
        double SecondsPerDay { get; }
        /// <summary>Days in one year on the home world (426 for the stock calendar).</summary>
        int DaysPerYear { get; }
    }

    public static class ClockExtensions
    {
        /// <summary>Whole days elapsed since the campaign began.</summary>
        public static int CurrentDay(this IClock clock)
        {
            if (clock == null || clock.SecondsPerDay <= 0) return 0;
            return (int)(clock.UniversalTime / clock.SecondsPerDay);
        }
    }
}
