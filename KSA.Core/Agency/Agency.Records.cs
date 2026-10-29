using System;
using System.Collections.Generic;
using System.Globalization;
using KSA.Core.Narrative;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.Core.Rivals;

namespace KSA.Core
{
    /// <summary>One entry in the agency's record book (2026-10-28).</summary>
    public sealed class RecordEntry
    {
        public string Key { get; set; }
        public double Value { get; set; }
        /// <summary>Who holds it: the crew of the flight, or the kerbal for a career record.</summary>
        public string Holder { get; set; }
        public string Mission { get; set; }
        public int Day { get; set; }
        public int Year { get; set; }

        public RecordEntry() { Key = string.Empty; Holder = string.Empty; Mission = string.Empty; }
    }

    /// <summary>
    /// Records (2026-10-28): firsts are credited to the kerbals who flew them, and an agency record book keeps the best
    /// flight and career figures with who holds them and when. A broken record makes the news.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>A record kind: key, title, unit, and whether it is a career figure (per kerbal) or a flight figure.</summary>
        public sealed class RecordKind
        {
            public string Key, Title, Unit;
            public bool Career;
            public string Format(double v)
            {
                switch (Unit)
                {
                    case "km": return v.ToString("#,##0.#", CultureInfo.InvariantCulture) + " km";
                    case "m/s": return v.ToString("#,##0", CultureInfo.InvariantCulture) + " m/s";
                    case "days": return v.ToString("0.#", CultureInfo.InvariantCulture) + " days";
                    default: return v.ToString("0", CultureInfo.InvariantCulture) + (Unit.Length > 0 ? " " + Unit : string.Empty);
                }
            }
        }

        public static readonly RecordKind[] RecordKinds =
        {
            new RecordKind { Key = "HighestAltitude", Title = "Highest altitude", Unit = "km" },
            new RecordKind { Key = "Fastest", Title = "Fastest flight", Unit = "m/s" },
            new RecordKind { Key = "Farthest", Title = "Farthest from Kerbin", Unit = "km" },
            new RecordKind { Key = "LongestMission", Title = "Longest mission", Unit = "days" },
            new RecordKind { Key = "MostDaysInSpace", Title = "Most days in space (career)", Unit = "days", Career = true },
            new RecordKind { Key = "MostMissions", Title = "Most missions flown (career)", Unit = "missions", Career = true },
            new RecordKind { Key = "MostWorlds", Title = "Most worlds visited (career)", Unit = "worlds", Career = true },
        };

        /// <summary>The record book, by key. Saved.</summary>
        public Dictionary<string, RecordEntry> Records { get; } = new Dictionary<string, RecordEntry>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The mission whose firsts are being claimed, so the claim names its crew.</summary>
        private MissionReport _firstContext;

        /// <summary>Fills in who flew a first the player just claimed, from the mission being resolved.</summary>
        internal void CreditFirst(FirstClaim f)
        {
            MissionReport r = _firstContext;
            if (f == null || r == null) return;
            bool flewIt = false;
            foreach (string a in r.Achievements) if (string.Equals(a, f.Achievement, StringComparison.OrdinalIgnoreCase)) { flewIt = true; break; }
            if (!flewIt) return;
            foreach (string id in r.Crew) { CrewProfile c = Crew.Get(id); f.Crew.Add(c != null ? c.Name : id); }
            f.Mission = r.ProjectName;
            f.Day = DayOfYear;
        }

        /// <summary>Text for a first: who did it and when, or the rival that got there first.</summary>
        public string FirstByText(FirstClaim f)
        {
            if (f == null) return string.Empty;
            if (f.ClaimedBy != PlayerId)
            {
                RivalDefinition d = RivalDefinition(f.ClaimedBy);
                return (d != null ? d.Name : f.ClaimedBy) + " (year " + f.Year + ")";
            }
            string who = f.Crew.Count > 0 ? string.Join(", ", f.Crew.ToArray()) : AgencyName;
            return who + (f.Mission.Length > 0 ? ", " + f.Mission : string.Empty) + " (year " + f.Year + (f.Day > 0 ? ", day " + f.Day : string.Empty) + ")";
        }

        private void UpdateRecords(MissionReport r, List<string> notices, bool announce = true)
        {
            if (r == null || r.Crew.Count == 0) return;
            var names = new List<string>();
            foreach (string id in r.Crew) { CrewProfile c = Crew.Get(id); names.Add(c != null ? c.Name : id); }
            string crew = string.Join(", ", names.ToArray());
            Offer("HighestAltitude", r.MaxAltitude / 1000.0, crew, r.ProjectName, notices, announce);
            Offer("Fastest", r.MaxSpeed, crew, r.ProjectName, notices, announce);
            Offer("Farthest", r.FarthestKm, crew, r.ProjectName, notices, announce);
            Offer("LongestMission", r.DaysInSpace, crew, r.ProjectName, notices, announce);
            foreach (string id in r.Crew)
            {
                CrewProfile c = Crew.Get(id);
                if (c != null) OfferCareer(c, r.ProjectName, notices, announce);
            }
        }

        private void OfferCareer(CrewProfile c, string mission, List<string> notices, bool announce)
        {
            Offer("MostDaysInSpace", c.Record.DaysInSpace, c.Name, mission, notices, announce);
            Offer("MostMissions", c.Record.Missions.Count, c.Name, mission, notices, announce);
            Offer("MostWorlds", c.Record.WorldsVisited("Kerbin"), c.Name, mission, notices, announce);
        }

        /// <summary>Sets a record when beaten. News when someone takes it from an earlier holder (or improves a flight record).</summary>
        private void Offer(string key, double value, string holder, string mission, List<string> notices, bool announce)
        {
            if (value <= 0) return;
            RecordKind kind = Array.Find(RecordKinds, k => k.Key == key);
            if (kind == null) return;
            RecordEntry old;
            Records.TryGetValue(key, out old);
            if (old != null && value <= old.Value + 1e-6) return;
            var entry = new RecordEntry { Key = key, Value = value, Holder = holder, Mission = mission, Day = DayOfYear, Year = CurrentYear };
            Records[key] = entry;
            if (!announce || old == null) return;
            // Career records grow every mission: only news when the record changes hands.
            if (kind.Career && string.Equals(old.Holder, holder, StringComparison.OrdinalIgnoreCase)) return;
            string headline = holder + " sets a new record: " + kind.Title.ToLowerInvariant() + ", " + kind.Format(value);
            string was = " (was " + kind.Format(old.Value) + ", " + old.Holder + ")";
            Push(notices, headline + was);
            Publish(new NewsItem { Kind = NewsKind.PressRelease, Byline = AgencyName, Related = "record:" + key, Headline = headline, Body = headline + " on " + mission + was + "." }, false);
        }

        /// <summary>An older save: build the career records from the crew's service records, quietly.</summary>
        internal void BackfillRecords()
        {
            if (Records.Count > 0) return;
            foreach (CrewProfile c in Crew.All) OfferCareer(c, string.Empty, null, false);
        }
    }
}
