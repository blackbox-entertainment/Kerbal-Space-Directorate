using System;
using System.Collections.Generic;
using System.IO;
using KSA.Core.Missions;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// Career only: a template can be built when every part in its craft file is researched and purchased.
    /// Reads the craft once per template per tech change and reports the missing parts by their display names.
    /// </summary>
    public static class KspTemplateCheck
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _techKey = string.Empty;

        public static string Blocker(Template t)
        {
            if (t == null) return null;
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || ResearchAndDevelopment.Instance == null) return null;
            string key = ResearchAndDevelopment.Instance.Science.ToString("0") + "/" + KspPartCatalog.TechVersion;
            if (key != _techKey) { Cache.Clear(); _techKey = key; }
            string cached;
            if (Cache.TryGetValue(t.Id, out cached)) return cached.Length == 0 ? null : cached;
            string result = Compute(t) ?? string.Empty;
            Cache[t.Id] = result;
            return result.Length == 0 ? null : result;
        }

        private static string Compute(Template t)
        {
            string path = ResolvePath(t.Craft);
            if (path == null) return null; // nothing to read: leave it to the launcher's own fallback
            ConfigNode craft;
            try { craft = ConfigNode.Load(path); } catch { return null; }
            if (craft == null) return null;
            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode part in craft.GetNodes("PART"))
            {
                string raw = part.GetValue("part") ?? string.Empty;
                int us = raw.LastIndexOf('_');
                string name = us > 0 ? raw.Substring(0, us) : raw;
                name = name.Replace('.', '_');
                if (name.Length == 0 || !seen.Add(name)) continue;
                AvailablePart ap = PartLoader.getPartInfoByName(name);
                if (ap == null) { missing.Add(name + " (unknown part)"); continue; }
                if (!(ResearchAndDevelopment.PartTechAvailable(ap) && ResearchAndDevelopment.PartModelPurchased(ap))) missing.Add(ap.title);
            }
            if (missing.Count == 0) return null;
            string list = string.Join(", ", missing.ToArray(), 0, Math.Min(4, missing.Count)) + (missing.Count > 4 ? " and " + (missing.Count - 4) + " more" : string.Empty);
            return "needs research: " + list;
        }

        private static string ResolvePath(string craft)
        {
            if (string.IsNullOrEmpty(craft)) return null;
            string root = KSPUtil.ApplicationRootPath;
            string path = craft.StartsWith("stock:", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(root, Path.Combine("Ships", Path.Combine("VAB", craft.Substring(6).Trim() + ".craft")))
                : Path.Combine(root, Path.Combine("GameData", craft.Replace('/', Path.DirectorySeparatorChar)));
            return File.Exists(path) ? path : null;
        }
    }
}
