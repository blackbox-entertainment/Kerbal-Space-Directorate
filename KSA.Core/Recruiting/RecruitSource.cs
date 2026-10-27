using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Personnel;

namespace KSA.Core.Recruiting
{
    /// <summary>Where applicants come from, loaded from KSA_RECRUIT_SOURCE nodes (Jira KSA-29).</summary>
    public sealed class RecruitSource
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<CrewRole> Roles { get; } = new List<CrewRole>();
        public double DriveCost { get; set; }
        public double HireFee { get; set; }
        public int ApplicantsPerDrive { get; set; }
        public int StarsMin { get; set; }
        public int StarsMax { get; set; }
        public float CourageMin { get; set; }
        public float CourageMax { get; set; }
        public float StupidityMin { get; set; }
        public float StupidityMax { get; set; }
        /// <summary>Achievements granted on hire to represent prior experience, e.g. "Flight:Kerbin".</summary>
        public List<string> Achievements { get; } = new List<string>();
        /// <summary>Relative chance this source produces the quarterly walk-in. 0 = never.</summary>
        public int WalkInWeight { get; set; }

        public RecruitSource()
        {
            Id = string.Empty;
            Name = string.Empty;
            Description = string.Empty;
            ApplicantsPerDrive = 3;
            StarsMax = 1;
            CourageMin = 0.2f; CourageMax = 0.8f;
            StupidityMin = 0.2f; StupidityMax = 0.8f;
            WalkInWeight = 0;
        }

        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    public sealed class RecruitCatalog
    {
        public const string NodeName = "KSA_RECRUIT_SOURCE";

        private readonly List<RecruitSource> _sources = new List<RecruitSource>();

        public IReadOnlyList<RecruitSource> Sources { get { return _sources; } }
        public int Count { get { return _sources.Count; } }

        public void Add(RecruitSource s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            _sources.Add(s);
        }

        public RecruitSource Get(string id)
        {
            for (int i = 0; i < _sources.Count; i++)
                if (string.Equals(_sources[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _sources[i];
            return null;
        }

        public string NameOf(string id)
        {
            RecruitSource s = Get(id);
            return s != null ? s.Name : id;
        }

        public static RecruitCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var catalog = new RecruitCatalog();
            if (nodes == null) return catalog;
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                string id = node.GetValue("id", string.Empty);
                if (id.Length == 0) { if (warnings != null) warnings.Add("KSA_RECRUIT_SOURCE without id skipped"); continue; }

                var s = new RecruitSource
                {
                    Id = id,
                    Name = node.GetValue("name", id),
                    Description = node.GetValue("description", string.Empty),
                    DriveCost = node.GetDouble("driveCost", 10000),
                    HireFee = node.GetDouble("hireFee", 5000),
                    ApplicantsPerDrive = Math.Max(1, node.GetInt("applicantsPerDrive", 3)),
                    StarsMin = node.GetInt("starsMin", 0),
                    StarsMax = node.GetInt("starsMax", 1),
                    CourageMin = (float)node.GetDouble("courageMin", 0.2),
                    CourageMax = (float)node.GetDouble("courageMax", 0.8),
                    StupidityMin = (float)node.GetDouble("stupidityMin", 0.2),
                    StupidityMax = (float)node.GetDouble("stupidityMax", 0.8),
                    WalkInWeight = node.GetInt("walkInWeight", 0)
                };
                foreach (string roleName in node.GetList("roles"))
                {
                    CrewRole role;
                    if (Enum.TryParse(roleName, true, out role) && Enum.IsDefined(typeof(CrewRole), role)) s.Roles.Add(role);
                    else if (warnings != null) warnings.Add("Source " + id + ": unknown role '" + roleName + "'");
                }
                if (s.Roles.Count == 0) { s.Roles.Add(CrewRole.Pilot); s.Roles.Add(CrewRole.Engineer); s.Roles.Add(CrewRole.Scientist); }
                s.Achievements.AddRange(node.GetValues("achievement"));
                catalog.Add(s);
            }
            return catalog;
        }
    }
}
