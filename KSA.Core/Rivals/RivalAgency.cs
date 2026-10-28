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

        public RivalAgency() { Id = string.Empty; }

        public bool Has(string achievement)
        {
            foreach (string m in Milestones) if (string.Equals(m, achievement, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public DataNode ToDataNode()
        {
            var n = new DataNode("RIVAL");
            n.AddValue("id", Id).AddValue("prestige", Prestige).AddValue("missions", Missions).AddValue("successes", Successes).AddValue("deaths", Deaths);
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
                Deaths = n.GetInt("deaths", 0)
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

        public static YearOutcome RunYear(RivalDefinition def, RivalAgency state, List<MilestoneLadder.Rung> ladder, int year, Random rng)
        {
            var o = new YearOutcome();
            o.Year.Year = year;
            int missions = 1 + (int)Math.Round(def.Aggression * 3);
            for (int i = 0; i < missions; i++)
            {
                MilestoneLadder.Rung next = NextRung(state, ladder);
                double difficulty = next != null ? next.Difficulty : 0.5;
                double p = 0.35 + 0.4 * def.Safety + 0.15 * (state.Prestige / 100.0) - 0.3 * difficulty;
                p = p < 0.08 ? 0.08 : (p > 0.92 ? 0.92 : p);
                state.Missions++; o.Year.Missions++;
                if (rng.NextDouble() < p)
                {
                    state.Successes++; o.Year.Successes++;
                    state.Prestige = Math.Min(100, state.Prestige + 2);
                    if (next != null)
                    {
                        state.Milestones.Add(next.Achievement);
                        o.Year.Milestones.Add(next.Achievement);
                        state.Prestige = Math.Min(100, state.Prestige + 3);
                        o.Headlines.Add(def.Name + " completes " + MilestoneLadder.Describe(next.Achievement) + ".");
                    }
                }
                else
                {
                    state.Prestige = Math.Max(0, state.Prestige - 3);
                    double deathChance = (1 - def.Safety) * 0.35;
                    if (rng.NextDouble() < deathChance)
                    {
                        state.Deaths++; o.Year.Deaths++;
                        state.Prestige = Math.Max(0, state.Prestige - 6);
                        o.Headlines.Add(def.Name + " loses a crew member in a failed " + (next != null ? next.MissionName.ToLowerInvariant() : "flight") + ".");
                    }
                    else if (rng.NextDouble() < 0.5)
                    {
                        o.Headlines.Add(def.Name + " aborts a " + (next != null ? next.MissionName.ToLowerInvariant() : "flight") + "; crew safe.");
                    }
                }
            }
            o.Year.Prestige = state.Prestige;
            state.Years.Add(o.Year);
            return o;
        }

        public static MilestoneLadder.Rung NextRung(RivalAgency state, List<MilestoneLadder.Rung> ladder)
        {
            foreach (MilestoneLadder.Rung r in ladder) if (!state.Has(r.Achievement)) return r;
            return null;
        }
    }
}
