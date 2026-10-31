using System;
using System.Collections.Generic;
using System.Globalization;
using KSA.Core.Missions;
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

        /// <summary>
        /// Saves before 0.7.12 measured a live flight's farthest point from Kerbin's centre, so an 87 km hop read 687 km
        /// (2026-10-30, T: "not sure these records hold anymore?"). Once per save: take the planet's radius off those flights,
        /// then rebuild every crew member's farthest distance and the record from the mission reports.
        /// </summary>
        internal void FixFarthestFromSurface()
        {
            if (State.FarthestFromSurface) return;
            State.FarthestFromSurface = true;
            foreach (MissionReport r in Reports)
                if (r.Mode == MissionMode.WatchLive && r.FarthestKm > 0) r.FarthestKm = Math.Max(0, r.FarthestKm - BodyDistances.HomeRadiusKm);
            foreach (CrewProfile c in Crew.All)
            {
                double far = 0;
                foreach (MissionReport r in Reports) if (r.Crew.Contains(c.Id) && !r.Deaths.Contains(c.Id)) far = Math.Max(far, FarthestOf(r));
                c.Record.FarthestKm = far;
            }
            MissionReport best = null;
            foreach (MissionReport r in Reports) if (r.Crew.Count > 0 && FarthestOf(r) > 0 && (best == null || FarthestOf(r) > FarthestOf(best))) best = r;
            if (best == null) { Records.Remove("Farthest"); return; }
            var names = new List<string>();
            foreach (string id in best.Crew) { CrewProfile c = Crew.Get(id); names.Add(c != null ? c.Name : id); }
            Records["Farthest"] = new RecordEntry { Key = "Farthest", Value = FarthestOf(best), Holder = string.Join(", ", names.ToArray()), Mission = best.ProjectName, Day = Calendar.DayOfYear(best.Day), Year = Calendar.YearOf(best.Day) };
        }

        // ------------------------------------------------------------------ records by world
        // 2026-10-30, T: "records like distances days in space etc ... records on each planet". Worked out from the mission
        // reports each time, so older saves have them at once.

        /// <summary>One line of a world's record book: what, how much, who, on which mission and when.</summary>
        public sealed class WorldRecord
        {
            public string Title = string.Empty, Value = string.Empty, Holder = string.Empty, Mission = string.Empty;
            public int Year, Day;
        }

        /// <summary>The order worlds are listed in: Kerbin, its moons, then outward.</summary>
        public static int WorldOrder(string body)
        {
            double km = BodyDistances.FromHomeKm(body);
            return string.Equals(body, "Kerbin", StringComparison.OrdinalIgnoreCase) ? -1 : km > 0 ? (int)Math.Min(int.MaxValue - 1, km / 1000) : int.MaxValue;
        }

        private string WorldOfReport(MissionReport r)
        {
            MissionType t = Catalogs.Missions.Get(r.MissionTypeId);
            return t != null && t.Body.Length > 0 ? t.Body : null;
        }

        private string CrewNamesOf(IEnumerable<string> ids)
        {
            var names = new List<string>();
            foreach (string id in ids) { CrewProfile c = Crew.Get(id); names.Add(c != null ? c.Name : id); }
            return string.Join(", ", names.ToArray());
        }

        /// <summary>Worlds the agency has flown crewed missions to, in <see cref="WorldOrder"/>.</summary>
        public List<string> WorldsFlown()
        {
            var worlds = new List<string>();
            foreach (MissionReport r in Reports)
            {
                string w = WorldOfReport(r);
                if (w != null && r.Crew.Count > 0 && !worlds.Contains(w)) worlds.Add(w);
            }
            worlds.Sort((a, b) => WorldOrder(a) != WorldOrder(b) ? WorldOrder(a).CompareTo(WorldOrder(b)) : string.CompareOrdinal(a, b));
            return worlds;
        }

        /// <summary>A world's record book: missions flown, longest mission and surface stay, and the kerbals who lead there.</summary>
        public List<WorldRecord> WorldRecords(string world)
        {
            var list = new List<WorldRecord>();
            bool home = string.Equals(world, "Kerbin", StringComparison.OrdinalIgnoreCase);
            int flown = 0, succeeded = 0;
            MissionReport longest = null, longestStay = null;
            double stay = 0;
            var missionsBy = new Dictionary<string, int>();
            var surfaceBy = new Dictionary<string, double>();
            foreach (MissionReport r in Reports)
            {
                if (r.Crew.Count == 0 || !string.Equals(WorldOfReport(r), world, StringComparison.OrdinalIgnoreCase)) continue;
                flown++;
                if (r.IsSuccess || r.Outcome == MissionOutcome.PartialSuccess) succeeded++;
                if (longest == null || r.DaysInSpace > longest.DaysInSpace) longest = r;
                double s = SurfaceDaysOf(r);
                if (s > stay) { stay = s; longestStay = r; }
                foreach (string id in r.Crew)
                {
                    missionsBy[id] = (missionsBy.ContainsKey(id) ? missionsBy[id] : 0) + 1;
                    if (s > 0 && !r.Deaths.Contains(id)) surfaceBy[id] = (surfaceBy.ContainsKey(id) ? surfaceBy[id] : 0) + s;
                }
            }
            if (flown == 0) return list;
            list.Add(new WorldRecord { Title = "Crewed missions", Value = flown + " flown, " + succeeded + " succeeded" });
            if (longest != null && longest.DaysInSpace > 0) list.Add(FromReport("Longest mission", Days(longest.DaysInSpace), longest));
            if (!home && longestStay != null) list.Add(FromReport("Longest stay on the surface", Days(stay), longestStay));
            string topId = null;
            foreach (KeyValuePair<string, int> kv in missionsBy) if (topId == null || kv.Value > missionsBy[topId]) topId = kv.Key;
            if (topId != null) list.Add(new WorldRecord { Title = home ? "Most missions (career)" : "Most missions to " + (world == "Mun" ? "the Mun" : world) + " (career)", Value = missionsBy[topId] + " mission" + (missionsBy[topId] == 1 ? "" : "s"), Holder = CrewNamesOf(new[] { topId }) });
            string walker = null;
            foreach (KeyValuePair<string, double> kv in surfaceBy) if (walker == null || kv.Value > surfaceBy[walker]) walker = kv.Key;
            if (!home && walker != null) list.Add(new WorldRecord { Title = "Most days on the surface (career)", Value = Days(surfaceBy[walker]), Holder = CrewNamesOf(new[] { walker }) });
            return list;
        }

        private double SurfaceDaysOf(MissionReport r)
        {
            if (r.SurfaceDays > 0) return r.SurfaceDays;
            MissionType t = Catalogs.Missions.Get(r.MissionTypeId);
            return r.IsSuccess && t != null && !string.Equals(t.Body, "Kerbin", StringComparison.OrdinalIgnoreCase) ? BodyDistances.SurfaceDaysFor(t) : 0;
        }

        private WorldRecord FromReport(string title, string value, MissionReport r)
        {
            return new WorldRecord { Title = title, Value = value, Holder = CrewNamesOf(r.Crew), Mission = r.ProjectName, Year = Calendar.YearOf(r.Day), Day = Calendar.DayOfYear(r.Day) };
        }

        private static string Days(double d) { return d.ToString("0.#", CultureInfo.InvariantCulture) + " day" + (Math.Abs(d - 1) < 0.05 ? "" : "s"); }

        /// <summary>An older save: build the career records from the crew's service records, quietly.</summary>
        internal void BackfillRecords()
        {
            if (Records.Count > 0) return;
            foreach (CrewProfile c in Crew.All) OfferCareer(c, string.Empty, null, false);
        }
    }
}
