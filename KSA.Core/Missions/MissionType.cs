using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Missions
{
    public enum MissionTier { Early = 0, Mid = 1, Late = 2 }

    public enum MissionProfile { Suborbital = 0, Orbit = 1, Flyby = 2, OrbitBody = 3, Land = 4, Station = 5, Base = 6 }

    /// <summary>How a project's mission is resolved.</summary>
    public enum MissionMode { Simulate = 0, WatchLive = 1 }

    /// <summary>Agency-wide default for new projects (GDD 14.1 decision 5).</summary>
    public enum DefaultMissionMode { Simulate = 0, WatchLive = 1, Hybrid = 2 }

    /// <summary>A kind of mission the agency can fly, loaded from KSA_MISSION_TYPE (Jira KSA-34).</summary>
    public sealed class MissionType
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public MissionTier Tier { get; set; }
        public string Body { get; set; }
        public MissionProfile Profile { get; set; }
        public int CrewSize { get; set; }
        /// <summary>Empty seats the craft must also carry: a rescue's stranded crew coming home (2026-10-28).</summary>
        public int PassengerSeats { get; set; }
        /// <summary>Seats the craft needs: the crew plus the passengers.</summary>
        public int Seats { get { return Math.Max(1, CrewSize) + Math.Max(0, PassengerSeats); } }
        public int MinStars { get; set; }
        public int DurationDays { get; set; }
        public double BaseCost { get; set; }
        /// <summary>0 (trivial) .. 1 (brutal).</summary>
        public double Difficulty { get; set; }
        /// <summary>Achievements in order of difficulty; the last one is what the mission is for.</summary>
        public List<string> Achievements { get; } = new List<string>();
        /// <summary>A "First": under the Hybrid default it is flown live if the agency has never done it.</summary>
        public bool IsFirst { get; set; }
        /// <summary>Non-empty for program module missions (core, hab, power, lab, docking ...).</summary>
        public string ModuleRole { get; set; }
        /// <summary>"Science" for missions whose point is the data they bring home; empty for everything else.</summary>
        public string Purpose { get; set; }
        /// <summary>Science missions: the points a textbook flight returns before crew and department multipliers.</summary>
        public double ScienceYield { get; set; }
        /// <summary>Tanker flights (2026-10-29): tonnes of propellant pumped into the fuel depot on arrival.</summary>
        public double CargoFuel { get; set; }
        public bool IsTanker { get { return CargoFuel > 0; } }
        /// <summary>Custom and landing missions: orbits to complete around the target (0 = the profile default).</summary>
        public int Orbits { get; set; }
        /// <summary>Days to spend on the surface (0 = the profile default).</summary>
        public double SurfaceDays { get; set; }
        /// <summary>Director-defined mission (KSA-59): saved with the game rather than read from missions.cfg.</summary>
        public bool IsCustom { get; set; }
        /// <summary>A cloud-city mission: it floats, it never touches a surface (KSA-64). Never flown live.</summary>
        public bool Floating { get; set; }
        /// <summary>A rescue flight for a stranded crew (KSA-65).</summary>
        public bool IsRescue { get { return string.Equals(Purpose, "Rescue", StringComparison.OrdinalIgnoreCase); } }
        public bool IsScience { get { return string.Equals(Purpose, "Science", StringComparison.OrdinalIgnoreCase); } }

        public MissionType()
        {
            Id = string.Empty; Name = string.Empty; Description = string.Empty; Body = "Kerbin"; ModuleRole = string.Empty; Purpose = string.Empty;
            CrewSize = 1; DurationDays = 1; BaseCost = 10000; Difficulty = 0.3;
        }

        /// <summary>The achievement that defines success: the last one that is not a plain recovery. Empty if none.</summary>
        public string PrimaryAchievement
        {
            get
            {
                for (int i = Achievements.Count - 1; i >= 0; i--)
                    if (!Achievements[i].StartsWith("Recover", StringComparison.OrdinalIgnoreCase)) return Achievements[i];
                return string.Empty;
            }
        }

        public bool IsLanding { get { return Profile == MissionProfile.Land || Profile == MissionProfile.Base; } }

        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    public sealed class MissionCatalog
    {
        public const string NodeName = "KSA_MISSION_TYPE";

        private readonly List<MissionType> _types = new List<MissionType>();

        public IReadOnlyList<MissionType> Types { get { return _types; } }
        public int Count { get { return _types.Count; } }

        public void Add(MissionType t) { if (t == null) throw new ArgumentNullException(nameof(t)); _types.Add(t); }
        public bool Remove(string id) { return _types.RemoveAll(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)) > 0; }

        public MissionType Get(string id)
        {
            for (int i = 0; i < _types.Count; i++)
                if (string.Equals(_types[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _types[i];
            return null;
        }

        public string NameOf(string id)
        {
            MissionType t = Get(id);
            return t != null ? t.Name : id;
        }

        public static MissionCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var catalog = new MissionCatalog();
            if (nodes == null) return catalog;
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                string id = node.GetValue("id", string.Empty);
                if (id.Length == 0) { Warn(warnings, "KSA_MISSION_TYPE without id skipped"); continue; }
                MissionTier tier; MissionProfile profile;
                if (!TryEnum(node.GetValue("tier", "Early"), out tier)) { Warn(warnings, "Mission " + id + ": unknown tier"); continue; }
                if (!TryEnum(node.GetValue("profile", "Orbit"), out profile)) { Warn(warnings, "Mission " + id + ": unknown profile"); continue; }
                var t = new MissionType
                {
                    Id = id,
                    Name = node.GetValue("name", id),
                    Description = node.GetValue("description", string.Empty),
                    Tier = tier,
                    Body = node.GetValue("body", "Kerbin"),
                    Profile = profile,
                    CrewSize = Math.Max(1, node.GetInt("crew", 1)),
                    MinStars = node.GetInt("minStars", 0),
                    DurationDays = Math.Max(1, node.GetInt("durationDays", 1)),
                    BaseCost = node.GetDouble("baseCost", 10000),
                    Difficulty = Clamp01(node.GetDouble("difficulty", 0.3)),
                    IsFirst = node.GetBool("first", false),
                    ModuleRole = node.GetValue("moduleRole", string.Empty),
                    Purpose = node.GetValue("purpose", string.Empty),
                    ScienceYield = node.GetDouble("scienceYield", 0),
                    CargoFuel = node.GetDouble("cargoFuel", 0),
                    Orbits = node.GetInt("orbits", 0),
                    SurfaceDays = node.GetDouble("surfaceDays", 0),
                    Floating = node.GetBool("floating", false)
                };
                t.Achievements.AddRange(node.GetList("achievements"));
                catalog.Add(t);
            }
            return catalog;
        }

        private static bool TryEnum<T>(string text, out T value) where T : struct
        {
            return Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value);
        }

        private static double Clamp01(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        private static void Warn(List<string> warnings, string message) { if (warnings != null) warnings.Add(message); }
    }
}
