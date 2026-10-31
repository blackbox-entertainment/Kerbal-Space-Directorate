using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Missions;

namespace KSA.Core.Rivals
{
    /// <summary>A rival agency as defined in rivals.cfg.</summary>
    public sealed class RivalDefinition
    {
        public const string NodeName = "KSA_RIVAL";

        public string Id { get; set; }
        public string Name { get; set; }
        public string Style { get; set; }
        public string Blurb { get; set; }
        public double Budget { get; set; }
        public double Aggression { get; set; }
        public double Safety { get; set; }
        public double StartPrestige { get; set; }

        public RivalDefinition() { Id = string.Empty; Name = string.Empty; Style = string.Empty; Blurb = string.Empty; Budget = 100000; Aggression = 0.5; Safety = 0.6; StartPrestige = 40; }

        public static List<RivalDefinition> Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var list = new List<RivalDefinition>();
            if (nodes == null) return list;
            foreach (DataNode n in nodes)
            {
                if (n == null || !string.Equals(n.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                string id = n.GetValue("id", string.Empty);
                if (id.Length == 0) { if (warnings != null) warnings.Add("KSA_RIVAL without id skipped"); continue; }
                list.Add(new RivalDefinition
                {
                    Id = id,
                    Name = n.GetValue("name", id),
                    Style = n.GetValue("style", "Steady"),
                    Blurb = n.GetValue("blurb", string.Empty),
                    Budget = n.GetDouble("budget", 100000),
                    Aggression = Clamp01(n.GetDouble("aggression", 0.5)),
                    Safety = Clamp01(n.GetDouble("safety", 0.6)),
                    StartPrestige = n.GetDouble("prestige", 40)
                });
            }
            return list;
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }
    }

    /// <summary>One rival's year, for the Analytics window.</summary>
    public sealed class RivalYear
    {
        public int Year { get; set; }
        public int Missions { get; set; }
        public int Successes { get; set; }
        public int Deaths { get; set; }
        public double Prestige { get; set; }
        public List<string> Milestones { get; } = new List<string>();
    }

    /// <summary>A rival's running state (persisted).</summary>
    public sealed class RivalAgency
    {
        public string Id { get; set; }
        public double Prestige { get; set; }
        public int Missions { get; set; }
        public int Successes { get; set; }
        public int Deaths { get; set; }
        /// <summary>Achievement keys ("Orbit:Kerbin") claimed so far, in order.</summary>
        public List<string> Milestones { get; } = new List<string>();
        public List<RivalYear> Years { get; } = new List<RivalYear>();
        /// <summary>Fiscal year the in-year flights below belong to, and how many of that year's flights have flown (2026-10-28).</summary>
        public int FlownYear { get; set; }
        public int FlownCount { get; set; }

        public RivalAgency() { Id = string.Empty; }

        /// <summary>The record for a year, created when its first flight goes.</summary>
        public RivalYear YearRecord(int year)
        {
            foreach (RivalYear y in Years) if (y.Year == year) return y;
            var r = new RivalYear { Year = year, Prestige = Prestige };
            Years.Add(r);
            return r;
        }

        public bool Has(string achievement)
        {
            foreach (string m in Milestones) if (string.Equals(m, achievement, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public DataNode ToDataNode()
        {
            var n = new DataNode("RIVAL");
            n.AddValue("id", Id).AddValue("prestige", Prestige).AddValue("missions", Missions).AddValue("successes", Successes).AddValue("deaths", Deaths);
            n.AddValue("flownYear", FlownYear).AddValue("flownCount", FlownCount);
            foreach (string m in Milestones) n.AddValue("milestone", m);
            foreach (RivalYear y in Years)
            {
                DataNode yn = n.AddChild("YEAR");
                yn.AddValue("year", y.Year).AddValue("missions", y.Missions).AddValue("successes", y.Successes).AddValue("deaths", y.Deaths).AddValue("prestige", y.Prestige);
                foreach (string m in y.Milestones) yn.AddValue("milestone", m);
            }
            return n;
        }

        public static RivalAgency FromDataNode(DataNode n)
        {
            var r = new RivalAgency
            {
                Id = n.GetValue("id", string.Empty),
                Prestige = n.GetDouble("prestige", 40),
                Missions = n.GetInt("missions", 0),
                Successes = n.GetInt("successes", 0),
                Deaths = n.GetInt("deaths", 0),
                FlownYear = n.GetInt("flownYear", 0),
                FlownCount = n.GetInt("flownCount", 0)
            };
            r.Milestones.AddRange(n.GetValues("milestone"));
            foreach (DataNode yn in n.GetChildren("YEAR"))
            {
                var y = new RivalYear { Year = yn.GetInt("year", 0), Missions = yn.GetInt("missions", 0), Successes = yn.GetInt("successes", 0), Deaths = yn.GetInt("deaths", 0), Prestige = yn.GetDouble("prestige", 0) };
                y.Milestones.AddRange(yn.GetValues("milestone"));
                r.Years.Add(y);
            }
            return r;
        }
    }

    /// <summary>
    /// The milestone ladder every agency climbs: the primary achievement of each crewed mission type, easiest first.
    /// Built from missions.cfg so new mission types extend the race automatically.
    /// </summary>
    public static class MilestoneLadder
    {
        public sealed class Rung
        {
            public string Achievement;
            public string MissionName;
            public double Difficulty;
            public MissionTier Tier;
        }

        public static List<Rung> Build(MissionCatalog missions)
        {
            var rungs = new List<Rung>();
            if (missions == null) return rungs;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (MissionType t in missions.Types)
            {
                if (!string.IsNullOrEmpty(t.ModuleRole)) continue;
                string a = t.PrimaryAchievement;
                if (string.IsNullOrEmpty(a) || !seen.Add(a)) continue;
                rungs.Add(new Rung { Achievement = a, MissionName = t.Name, Difficulty = t.Difficulty, Tier = t.Tier });
            }
            rungs.Sort((x, y) => x.Tier != y.Tier ? x.Tier.CompareTo(y.Tier) : x.Difficulty.CompareTo(y.Difficulty));
            return rungs;
        }

        /// <summary>The world an achievement belongs to ("Orbit:Mun" -> "Mun"; no world -> "Kerbin").</summary>
        public static string BodyOf(string achievement)
        {
            int colon = achievement != null ? achievement.IndexOf(':') : -1;
            string body = colon > 0 ? achievement.Substring(colon + 1) : string.Empty;
            return body.Length == 0 || body == "-" || body == "*" ? "Kerbin" : body;
        }

        /// <summary>
        /// The record-book name of a first (2026-10-30, T: "first like first kerbal to space ... first for each planet"):
        /// "First kerbal in space", "First kerbal on the Mun".
        /// </summary>
        public static string Title(string achievement)
        {
            if (string.IsNullOrEmpty(achievement)) return string.Empty;
            int colon = achievement.IndexOf(':');
            string kind = (colon > 0 ? achievement.Substring(0, colon) : achievement).ToLowerInvariant();
            string body = BodyOf(achievement);
            string the = body == "Mun" ? "the Mun" : body;
            bool home = body == "Kerbin";
            switch (kind)
            {
                case "flight": return "First flight";
                case "suborbit": return "First kerbal in space";
                case "orbit": return home ? "First kerbal in orbit" : "First orbit of " + the;
                case "flyby": return "First flyby of " + the;
                case "land": return "First kerbal on " + the;
                case "eva": return home ? "First spacewalk" : "First walk at " + the;
                case "science": return home ? "First science brought home" : "First science from " + the;
                case "base": return home ? "First space station" : "First base on " + the;
                case "resupply": return home ? "First station resupply" : "First resupply run to " + the;
                case "escape": return "First escape from " + the;
                case "aircraft": return "First aircraft flight";
                case "survey": return "First aerial survey";
                case "highair": return "First high-altitude flight";
                case "rocketplane": return "First rocket plane flight";
                default: return "First " + Describe(achievement);
            }
        }

        public static string Describe(string achievement)
        {
            if (string.IsNullOrEmpty(achievement)) return string.Empty;
            int colon = achievement.IndexOf(':');
            string kind = colon > 0 ? achievement.Substring(0, colon) : achievement;
            string body = colon > 0 ? achievement.Substring(colon + 1) : string.Empty;
            switch (kind.ToLowerInvariant())
            {
                case "suborbit": return "suborbital flight";
                case "orbit": return body == "Kerbin" ? "orbit" : "orbit of " + body;
                case "flyby": return "flyby of " + body;
                case "land": return "landing on " + body;
                case "eva": return "spacewalk at " + body;
                case "science": return "a science return from " + body;
                case "base": return body == "Kerbin" ? "a crewed station" : "a crewed base at " + body;
                case "resupply": return body == "Kerbin" ? "a station resupply" : "a resupply run to " + body;
                case "escape": return "escape from " + body;
                case "flight": return "first flight";
                default: return kind.ToLowerInvariant() + (body.Length > 0 ? " of " + body : string.Empty);
            }
        }
    }

    /// <summary>Simulates one rival year. Pure: takes the state and the rng, returns what happened.</summary>
    public static class RivalSimulator
    {
        public sealed class YearOutcome
        {
            public RivalYear Year = new RivalYear();
            public List<string> Headlines = new List<string>();
        }

        /// <summary>Flights a rival makes in a fiscal year: 2 + aggression x 4 (2 to 6; raised 2026-10-28 so the race is visible).</summary>
        public static int MissionsPerYear(RivalDefinition def) { return 2 + (int)Math.Round(def.Aggression * 4); }

        /// <summary>
        /// Day of the fiscal year a rival's k-th flight of n goes: evenly spread, each rival a little offset from the
        /// others so they do not all fly on the same day (2026-10-28: rivals used to act only at year end).
        /// </summary>
        public static int MissionDay(int k, int n, int daysPerYear, int rivalIndex)
        {
            int spacing = daysPerYear / (n + 1);
            int offset = ((rivalIndex * 37) % Math.Max(1, spacing / 2)) - spacing / 4;
            int day = (k + 1) * spacing + offset;
            return Math.Max(1, Math.Min(daysPerYear - 2, day));
        }

        /// <summary>Flies one rival mission up the ladder into the year's record; milestones and headlines are added to the lists.</summary>
        public static void RunMission(RivalDefinition def, RivalAgency state, List<MilestoneLadder.Rung> ladder, RivalYear record, Random rng, List<string> milestones, List<string> headlines)
        {
            MilestoneLadder.Rung next = NextRung(state, ladder);
            double difficulty = next != null ? next.Difficulty : 0.5;
            double p = 0.35 + 0.4 * def.Safety + 0.15 * (state.Prestige / 100.0) - 0.3 * difficulty;
            p = p < 0.08 ? 0.08 : (p > 0.92 ? 0.92 : p);
            state.Missions++; record.Missions++;
            if (rng.NextDouble() < p)
            {
                state.Successes++; record.Successes++;
                state.Prestige = Math.Min(100, state.Prestige + 2);
                if (next != null)
                {
                    state.Milestones.Add(next.Achievement);
                    record.Milestones.Add(next.Achievement);
                    milestones.Add(next.Achievement);
                    state.Prestige = Math.Min(100, state.Prestige + 3);
                    headlines.Add(def.Name + " completes " + MilestoneLadder.Describe(next.Achievement) + ".");
                }
                else headlines.Add(def.Name + " flies another successful mission.");
            }
            else
            {
                state.Prestige = Math.Max(0, state.Prestige - 3);
                double deathChance = (1 - def.Safety) * 0.35;
                if (rng.NextDouble() < deathChance)
                {
                    state.Deaths++; record.Deaths++;
                    state.Prestige = Math.Max(0, state.Prestige - 6);
                    headlines.Add(def.Name + " loses a crew member in a failed " + (next != null ? next.MissionName.ToLowerInvariant() : "flight") + ".");
                }
                else headlines.Add(def.Name + " aborts a " + (next != null ? next.MissionName.ToLowerInvariant() : "flight") + "; crew safe.");
            }
            record.Prestige = state.Prestige;
        }

        /// <summary>A whole year at once (tests, and catching up a year nobody ticked through).</summary>
        public static YearOutcome RunYear(RivalDefinition def, RivalAgency state, List<MilestoneLadder.Rung> ladder, int year, Random rng)
        {
            var o = new YearOutcome();
            RivalYear record = state.YearRecord(year);
            o.Year = record;
            int missions = MissionsPerYear(def);
            for (int i = 0; i < missions; i++) RunMission(def, state, ladder, record, rng, new List<string>(), o.Headlines);
            record.Prestige = state.Prestige;
            return o;
        }

        public static MilestoneLadder.Rung NextRung(RivalAgency state, List<MilestoneLadder.Rung> ladder)
        {
            foreach (MilestoneLadder.Rung r in ladder) if (!state.Has(r.Achievement)) return r;
            return null;
        }
    }
}
