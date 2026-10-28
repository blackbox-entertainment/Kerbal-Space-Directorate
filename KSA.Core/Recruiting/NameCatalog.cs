using System;
using System.Collections.Generic;
using KSA.Core.Data;

namespace KSA.Core.Recruiting
{
    /// <summary>
    /// Applicant names (names.cfg, KSA_NAMES nodes): male and female first names and family names. A share of
    /// applicants keep the traditional family name (defaultFamily, kept in config rather than code); the rest draw
    /// from the family list. Several KSA_NAMES nodes merge, so mods and players can add their own.
    /// </summary>
    public sealed class NameCatalog
    {
        public const string NodeName = "KSA_NAMES";

        public List<string> Male { get; } = new List<string>();
        public List<string> Female { get; } = new List<string>();
        public List<string> Family { get; } = new List<string>();
        /// <summary>Family name everyone had before KSA-66; this share of applicants still gets it.</summary>
        public string DefaultFamily { get; set; } = string.Empty;
        public double DefaultFamilyShare { get; set; } = 0.35;

        public bool IsEmpty { get { return Male.Count == 0 && Female.Count == 0; } }

        /// <summary>A fresh name for the given gender that <paramref name="accept"/> allows (null = anything), or null.</summary>
        public string Pick(Random rng, bool female, Func<string, bool> accept)
        {
            List<string> firsts = female ? Female : Male;
            if (rng == null || firsts.Count == 0) return null;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                string first = firsts[rng.Next(firsts.Count)];
                string family = Family.Count == 0 || (DefaultFamily.Length > 0 && rng.NextDouble() < DefaultFamilyShare)
                    ? DefaultFamily
                    : Family[rng.Next(Family.Count)];
                string name = family.Length > 0 ? first + " " + family : first;
                if (accept == null || accept(name)) return name;
            }
            return null;
        }

        public static NameCatalog Parse(IEnumerable<DataNode> nodes, List<string> warnings = null)
        {
            var c = new NameCatalog();
            if (nodes == null) return c;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataNode node in nodes)
            {
                if (node == null || !string.Equals(node.Name, NodeName, StringComparison.OrdinalIgnoreCase)) continue;
                Add(node, "male", c.Male, seen, warnings);
                Add(node, "female", c.Female, seen, warnings);
                Add(node, "family", c.Family, new HashSet<string>(c.Family, StringComparer.OrdinalIgnoreCase), warnings);
                string def = node.GetValue("defaultFamily", string.Empty);
                if (def.Length > 0) c.DefaultFamily = def;
                c.DefaultFamilyShare = Math.Max(0, Math.Min(1, node.GetDouble("defaultFamilyShare", c.DefaultFamilyShare)));
            }
            return c;
        }

        private static void Add(DataNode node, string key, List<string> into, HashSet<string> seen, List<string> warnings)
        {
            foreach (string line in node.GetValues(key))
                foreach (string part in line.Split(','))
                {
                    string n = part.Trim();
                    if (n.Length == 0) continue;
                    if (!seen.Add(n)) { if (warnings != null) warnings.Add("Name '" + n + "' listed twice; kept once"); continue; }
                    into.Add(n);
                }
        }
    }
}
