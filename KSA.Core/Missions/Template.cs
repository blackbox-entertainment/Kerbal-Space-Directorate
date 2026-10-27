using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Missions
{
    /// <summary>A pre-built design the engineering team can fly a mission with (Jira KSA-34). The offline fallback for AI design (M4).</summary>
    public sealed class Template
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> MissionTypes { get; } = new List<string>();
        /// <summary>Craft reference: "stock:Kerbal X" for a stock craft, or a GameData-relative path. Used by Live mode (M5).</summary>
        public string Craft { get; set; }
        /// <summary>0..1 quality of the design; drives simulated outcomes.</summary>
        public double DesignScore { get; set; }
        public double CostMultiplier { get; set; }
        public bool StockOnly { get; set; }
        /// <summary>Crew seats on the craft (0 = unknown). Shown in the design picker so "Kerbal 2" is not read as two crew.</summary>
        public int Seats { get; set; }
        public List<string> RequiredMods { get; } = new List<string>();

        public Template()
        {
            Id = string.Empty; Name = string.Empty; Craft = string.Empty; DesignScore = 0.5; CostMultiplier = 1.0; StockOnly = true;
        }

        public bool Flies(string missionTypeId)
        {
            for (int i = 0; i < MissionTypes.Count; i++)
                if (string.Equals(MissionTypes[i], missionTypeId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public bool IsAvailable(ICollection<string> installedMods)
        {
            if (RequiredMods.Count == 0) return true;
            if (installedMods == null) return false;
            foreach (string m in RequiredMods)
            {
                bool found = false;
                foreach (string have in installedMods)
                    if (string.Equals(have, m, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    public sealed class TemplateCatalog
    {
        public const string NodeName = "KSA_TEMPLATE";

        private readonly List<Template> _templates = new List<Template>();

        public IReadOnlyList<Template> Templates { get { return _templates; } }
        public int Count { get { return _templates.Count; } }

        public void Add(Template t) { if (t == null) throw new ArgumentNullException(nameof(t)); _templates.Add(t); }

        public Template Get(string id)
        {
            for (int i = 0; i < _templates.Count; i++)
                if (string.Equals(_templates[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _templates[i];
            return null;
        }

        public string NameOf(string id)
        {
            Template t = Get(id);
            return t != null ? t.Name : id;
        }

        /// <summary>Every template that can fly the mission type with the mods installed, best design first.</summary>
        public List<Template> For(string missionTypeId, ICollection<string> installedMods)
        {
            var list = new List<Template>();
            foreach (Template t in _templates)
                if (t.Flies(missionTypeId) && t.IsAvailable(installedMods)) list.Add(t);
            list.Sort((a, b) => b.DesignScore.CompareTo(a.DesignScore));
            return list;
        }

        public Template Best(string missionTypeId, ICollection<string> installedMods)
        {
            List<Template> list = For(missionTypeId, installedMods);
            return list.Count > 0 ? list[0] : null;
        }

        public static TemplateCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var catalog = new TemplateCatalog();
            if (nodes == null) return catalog;
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                string id = node.GetValue("id", string.Empty);
                if (id.Length == 0) { if (warnings != null) warnings.Add("KSA_TEMPLATE without id skipped"); continue; }
                var t = new Template
                {
                    Id = id,
                    Name = node.GetValue("name", id),
                    Craft = node.GetValue("craft", string.Empty),
                    DesignScore = Math.Max(0, Math.Min(1, node.GetDouble("designScore", 0.5))),
                    CostMultiplier = Math.Max(0.1, node.GetDouble("costMultiplier", 1.0)),
                    StockOnly = node.GetBool("stockOnly", true),
                    Seats = Math.Max(0, node.GetInt("seats", 0))
                };
                t.MissionTypes.AddRange(node.GetList("missionTypes"));
                t.RequiredMods.AddRange(node.GetList("requiredMods"));
                if (t.RequiredMods.Count > 0) t.StockOnly = false;
                if (t.MissionTypes.Count == 0) { if (warnings != null) warnings.Add("Template " + id + " flies no mission types; skipped"); continue; }
                catalog.Add(t);
            }
            return catalog;
        }
    }
}
