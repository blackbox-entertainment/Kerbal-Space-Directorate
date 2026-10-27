using System;
using System.Collections.Generic;

namespace KSA.Core.Personnel
{
    public enum MissionOutcome
    {
        InProgress = 0,
        Success = 1,
        PartialSuccess = 2,
        Abort = 3,
        LossOfVehicle = 4,
        LossOfCrew = 5
    }

    public enum IncidentKind
    {
        Other = 0,
        Crash = 1,
        Injury = 2,
        Death = 3,
        Rescue = 4,
        Stranded = 5
    }

    /// <summary>One mission a crew member took part in.</summary>
    public sealed class MissionEntry
    {
        public string MissionId { get; set; }
        public string Name { get; set; }
        public int StartDay { get; set; }
        public int EndDay { get; set; }
        public MissionOutcome Outcome { get; set; }
        /// <summary>True when the player took the controls (see GDD 14.1 item 7).</summary>
        public bool DirectorFlown { get; set; }

        public MissionEntry() { MissionId = string.Empty; Name = string.Empty; EndDay = -1; }
    }

    public sealed class MedalAward
    {
        public string MedalId { get; set; }
        public int Day { get; set; }
        public string MissionId { get; set; }
        public string Note { get; set; }

        public MedalAward() { MedalId = string.Empty; MissionId = string.Empty; Note = string.Empty; }
    }

    public sealed class IncidentEntry
    {
        public IncidentKind Kind { get; set; }
        public int Day { get; set; }
        public string MissionId { get; set; }
        public string Description { get; set; }

        public IncidentEntry() { MissionId = string.Empty; Description = string.Empty; }
    }

    /// <summary>Everything a crew member has done: missions, medals, incidents, quotes, achievements.</summary>
    public sealed class ServiceRecord
    {
        public List<MissionEntry> Missions { get; } = new List<MissionEntry>();
        public List<MedalAward> Medals { get; } = new List<MedalAward>();
        public List<IncidentEntry> Incidents { get; } = new List<IncidentEntry>();
        public List<string> Quotes { get; } = new List<string>();

        /// <summary>
        /// Achievement keys in "Type:Body" form, e.g. "Orbit:Kerbin", "Land:Mun", "EVA:Mun".
        /// The host merges these in from its own flight logs; medals and ranks test against them.
        /// </summary>
        public HashSet<string> Achievements { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Cumulative days spent on missions, accumulated at recovery.</summary>
        public double DaysInSpace { get; set; }

        public int CompletedMissionCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Missions.Count; i++)
                    if (Missions[i].Outcome == MissionOutcome.Success || Missions[i].Outcome == MissionOutcome.PartialSuccess) n++;
                return n;
            }
        }

        public bool HasMedal(string medalId)
        {
            for (int i = 0; i < Medals.Count; i++)
                if (string.Equals(Medals[i].MedalId, medalId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool HasIncident(IncidentKind kind)
        {
            for (int i = 0; i < Incidents.Count; i++)
                if (Incidents[i].Kind == kind) return true;
            return false;
        }

        /// <summary>
        /// Achievement match with optional wildcard body: "Orbit:*" matches any "Orbit:X"; "Orbit" alone means "Orbit:*".
        /// </summary>
        public bool HasAchievement(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            string type = pattern;
            string body = "*";
            int colon = pattern.IndexOf(':');
            if (colon >= 0)
            {
                type = pattern.Substring(0, colon);
                body = pattern.Substring(colon + 1);
            }
            if (body != "*") return Achievements.Contains(type + ":" + body);

            string prefix = type + ":";
            foreach (string a in Achievements)
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public MissionEntry CurrentMission
        {
            get
            {
                for (int i = Missions.Count - 1; i >= 0; i--)
                    if (Missions[i].Outcome == MissionOutcome.InProgress) return Missions[i];
                return null;
            }
        }
    }
}
