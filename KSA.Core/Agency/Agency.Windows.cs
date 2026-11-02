using System;
using KSA.Core.Missions;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// Transfer windows (2026-11-01, T: missions to other planets go only when the window is near enough that the crew spends no
    /// more than 10 days in orbit). The host works out the date; a launch waits for it, whoever ordered the flight. Rescues do not wait.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>Days to the next transfer window to a world, or NaN when it needs none (Kerbin and its moons). Set by the host.</summary>
        public Func<string, double> DaysToWindow { get; set; }

        /// <summary>Why this mission waits for its window, or null.</summary>
        public string WindowBlocker(MissionType type)
        {
            if (type == null || type.IsRescue || Settings.WindowWaitDays <= 0) return null;
            Func<string, double> days = DaysToWindow;
            if (days == null) return null;
            double d;
            try { d = days(type.Body); }
            catch (Exception e) { Log.Warn("Window check failed for " + type.Body + ": " + e.Message); return null; }
            if (double.IsNaN(d) || d <= Settings.WindowWaitDays) return null;
            return "waiting for the " + type.Body + " window, " + Math.Ceiling(d).ToString("0") + " days away";
        }

        /// <summary>True while anyone at all is on the staff.</summary>
        private bool HasActiveCrew()
        {
            foreach (Personnel.CrewProfile c in Crew.Active) return true;
            return false;
        }

        /// <summary>Why this project waits for its window, or null.</summary>
        public string WindowBlocker(Project p)
        {
            return p != null ? WindowBlocker(Catalogs.Missions.Get(p.MissionTypeId)) : null;
        }
    }
}
