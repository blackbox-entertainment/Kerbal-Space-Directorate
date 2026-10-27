using System;
using System.Collections.Generic;
using System.Linq;

namespace KSA.Core.Personnel
{
    /// <summary>Pure presentation helpers for the roster window. No UI framework here.</summary>
    public static class RosterView
    {
        /// <summary>Orders crew for display: Available first, then Assigned, Missing, Dead; by name within each group.</summary>
        public static List<CrewRecord> Sort(IEnumerable<CrewRecord> crew)
        {
            if (crew == null) throw new ArgumentNullException(nameof(crew));
            return crew
                .OrderBy(c => (int)c.Status)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Renders stars as a fixed-width ASCII bar, e.g. 3 -> "***--". Plain ASCII so every game font can show it.</summary>
        public static string StarsText(int stars)
        {
            int filled = stars < 0 ? 0 : (stars > CrewRecord.MaxStars ? CrewRecord.MaxStars : stars);
            return new string('*', filled) + new string('-', CrewRecord.MaxStars - filled);
        }

        public static string RoleText(CrewRole role)
        {
            switch (role)
            {
                case CrewRole.Pilot: return "Pilot";
                case CrewRole.Engineer: return "Engineer";
                case CrewRole.Scientist: return "Scientist";
                case CrewRole.Tourist: return "Tourist";
                default: return "Unknown";
            }
        }

        public static string StatusText(CrewStatus status)
        {
            switch (status)
            {
                case CrewStatus.Available: return "Available";
                case CrewStatus.Assigned: return "Assigned";
                case CrewStatus.Missing: return "Missing";
                case CrewStatus.Dead: return "Dead";
                default: return status.ToString();
            }
        }
    }
}
