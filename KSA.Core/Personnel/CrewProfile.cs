using System;
using System.Collections.Generic;

namespace KSA.Core.Personnel
{
    /// <summary>
    /// The agency's own file on one crew member (Jira KSA-17). Stock data (role, stars, courage, stupidity, status)
    /// is refreshed from the host every sync; everything else is KSA's and is persisted by the host.
    /// </summary>
    public sealed class CrewProfile
    {
        public const int DefaultMorale = 70;

        /// <summary>Stable identity. The host guarantees uniqueness (KSP crew names are unique per save).</summary>
        public string Id { get; }
        public string Name { get; set; }

        // --- Mirrored from the host each sync ---
        public CrewRole Role { get; set; }
        public int Stars { get; set; }
        /// <summary>0..1</summary>
        public float Courage { get; set; }
        /// <summary>0..1</summary>
        public float Stupidity { get; set; }
        public CrewStatus Status { get; set; }

        // --- Owned by KSA ---
        public CareerTrack Track { get; set; }
        /// <summary>Rank id from the career catalog, e.g. "TestPilot". Empty until assigned.</summary>
        public string RankId { get; set; }
        public List<string> PersonalityTags { get; } = new List<string>();
        /// <summary>0..100</summary>
        public int Morale { get; set; }
        /// <summary>Funds per fiscal year.</summary>
        public int Salary { get; set; }
        /// <summary>Day number (agency calendar) of birth; negative means before the campaign began.</summary>
        public int BornDay { get; set; }
        public int HiredDay { get; set; }
        /// <summary>Universal time (seconds) when the current mission started, or negative when not flying.</summary>
        public double InFlightSinceUT { get; set; }
        /// <summary>Set once the crew member has left the agency (quit, retired, died); kept for the hall of fame.</summary>
        public bool Departed { get; set; }

        public ServiceRecord Record { get; } = new ServiceRecord();

        public CrewProfile(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is required", nameof(id));
            Id = id;
            Name = name ?? id;
            RankId = string.Empty;
            Morale = DefaultMorale;
            InFlightSinceUT = -1;
        }

        public bool IsFlying { get { return InFlightSinceUT >= 0; } }

        /// <summary>Age in whole years for a given current day and year length.</summary>
        public int AgeOn(int currentDay, int daysPerYear)
        {
            if (daysPerYear <= 0) return 0;
            int days = currentDay - BornDay;
            return days < 0 ? 0 : days / daysPerYear;
        }

        public override string ToString()
        {
            return Name + " [" + Track + "/" + RankId + ", " + Stars + "*, " + Status + "]";
        }
    }
}
