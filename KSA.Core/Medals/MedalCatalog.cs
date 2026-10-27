using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Personnel;

namespace KSA.Core.Medals
{
    /// <summary>All medal definitions, parsed from config nodes named KSA_MEDAL.</summary>
    public sealed class MedalCatalog
    {
        public const string NodeName = "KSA_MEDAL";

        private readonly List<MedalDefinition> _medals = new List<MedalDefinition>();

        public IReadOnlyList<MedalDefinition> Medals { get { return _medals; } }
        public int Count { get { return _medals.Count; } }

        public void Add(MedalDefinition medal)
        {
            if (medal == null) throw new ArgumentNullException(nameof(medal));
            _medals.Add(medal);
        }

        public MedalDefinition Get(string id)
        {
            for (int i = 0; i < _medals.Count; i++)
                if (string.Equals(_medals[i].Id, id, StringComparison.OrdinalIgnoreCase)) return _medals[i];
            return null;
        }

        public string NameOf(string id)
        {
            MedalDefinition m = Get(id);
            return m != null ? m.Name : id;
        }

        public static MedalCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var catalog = new MedalCatalog();
            if (nodes == null) return catalog;

            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;

                string id = node.GetValue("id", string.Empty);
                if (id.Length == 0)
                {
                    if (warnings != null) warnings.Add("KSA_MEDAL without id skipped");
                    continue;
                }

                MedalTrigger trigger;
                string triggerText = node.GetValue("trigger", "Achievement");
                if (!Enum.TryParse(triggerText, true, out trigger) || !Enum.IsDefined(typeof(MedalTrigger), trigger))
                {
                    if (warnings != null) warnings.Add("Medal " + id + ": unknown trigger '" + triggerText + "'; skipped");
                    continue;
                }

                IncidentKind kind = IncidentKind.Other;
                string kindText = node.GetValue("incident", null);
                if (kindText != null && (!Enum.TryParse(kindText, true, out kind) || !Enum.IsDefined(typeof(IncidentKind), kind)))
                {
                    if (warnings != null) warnings.Add("Medal " + id + ": unknown incident kind '" + kindText + "'; skipped");
                    continue;
                }

                var medal = new MedalDefinition
                {
                    Id = id,
                    Name = node.GetValue("name", id),
                    Description = node.GetValue("description", string.Empty),
                    Trigger = trigger,
                    Threshold = node.GetDouble("threshold", 0),
                    IncidentKind = kind,
                    FirstInAgency = node.GetBool("firstInAgency", false),
                    Posthumous = node.GetBool("posthumous", false),
                    RequiresAlive = node.GetBool("requiresAlive", false)
                };
                medal.Achievements.AddRange(node.GetValues("achievement"));

                if (trigger == MedalTrigger.Achievement && medal.Achievements.Count == 0)
                {
                    if (warnings != null) warnings.Add("Medal " + id + ": Achievement trigger needs at least one 'achievement' value; skipped");
                    continue;
                }
                catalog.Add(medal);
            }
            return catalog;
        }
    }
}
