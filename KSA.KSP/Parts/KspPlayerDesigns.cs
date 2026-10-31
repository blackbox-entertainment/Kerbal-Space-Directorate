using System;
using System.Collections.Generic;
using System.IO;
using KSA.Core.Design;
using UnityEngine;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// The Director's own rockets (2026-10-28): every craft saved in this game's VAB folder (not KSA's own "KSA ..." craft
    /// or the KSA Templates folder) is read from its file and offered to projects that it can fly. Delta-v and liftoff
    /// thrust are worked out from the parts, staging and decouplers in the file, since KSP only calculates them for a
    /// craft loaded in the editor or in flight. Results are cached until the file changes.
    /// </summary>
    public static class KspPlayerDesigns
    {
        private const double G0 = 9.80665;
        private sealed class Cached { public DateTime Time; public PlayerDesign Design; }
        private static readonly Dictionary<string, Cached> Cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);

        private static bool EngineBreathes(ModuleEngines m)
        {
            foreach (Propellant pr in m.propellants) if (pr.name == "IntakeAir") return true;
            return false;
        }

        public static string Folder { get { return FolderFor("VAB"); } }

        /// <summary>This game's craft folder for a building: "VAB" (rockets) or "SPH" (planes, 2026-10-28).</summary>
        public static string FolderFor(string building)
        {
            return HighLogic.SaveFolder == null ? null : Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("saves", Path.Combine(HighLogic.SaveFolder, Path.Combine("Ships", building))));
        }

        public static List<PlayerDesign> List()
        {
            var list = new List<PlayerDesign>();
            var files = new List<string>();
            bool shieldResearched = CrewShieldResearched();
            foreach (string building in new[] { "VAB", "SPH" })
            {
                string folder = FolderFor(building);
                if (folder != null && Directory.Exists(folder)) files.AddRange(Directory.GetFiles(folder, "*.craft", SearchOption.TopDirectoryOnly));
            }
            foreach (string path in files)
            {
                string name = Path.GetFileNameWithoutExtension(path);
                if (name.StartsWith("KSA ", StringComparison.OrdinalIgnoreCase)) continue; // the engineering team's own craft
                DateTime time;
                try { time = File.GetLastWriteTimeUtc(path); } catch { continue; }
                Cached c;
                if (!Cache.TryGetValue(path, out c) || c.Time != time)
                {
                    PlayerDesign d = null;
                    try { d = Analyze(path); } catch (Exception e) { KsaCore.Log.Warn("Your design '" + name + "' could not be read: " + e.Message); }
                    c = new Cached { Time = time, Design = d };
                    Cache[path] = c;
                    if (d != null) KsaCore.Log.Info("Your design '" + d.Name + "': " + Math.Round(d.DeltaV) + " m/s, liftoff TWR " + d.LiftoffTwr.ToString("0.00") + ", " + d.Seats + " seat(s)" + (d.HasParachute ? ", parachutes for " + d.ChuteCapacity.ToString("0.0") + " t (capsule " + d.LandingMass.ToString("0.0") + " t)" : ", no main parachute") + (d.ResearchBlocker != null ? ", " + d.ResearchBlocker : string.Empty));
                }
                if (c.Design == null) continue;
                // Research changes without the file changing: check it every time (cheap).
                c.Design.ResearchBlocker = ResearchBlocker(c.Design.CraftPath);
                c.Design.HeatShieldResearched = shieldResearched;
                list.Add(c.Design);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>
        /// Reads the craft open in the editor right now, saved or not (2026-10-30, "What can this fly?"): it is written to a
        /// scratch file and read like a saved design. Null when the editor holds nothing.
        /// </summary>
        public static PlayerDesign CurrentEditorCraft()
        {
            if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null || EditorLogic.fetch.ship.parts.Count == 0) return null;
            ShipConstruct ship = EditorLogic.fetch.ship;
            string path = Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", Path.Combine("KSA", Path.Combine("PluginData", "editor_craft.craft"))));
            ConfigNode node = ship.SaveShip();
            if (node == null) return null;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            node.Save(path);
            PlayerDesign d = Analyze(path);
            if (d == null) return null;
            if (!string.IsNullOrEmpty(ship.shipName)) d.Name = ship.shipName;
            d.ResearchBlocker = ResearchBlocker(path);
            d.HeatShieldResearched = CrewShieldResearched();
            return d;
        }

        // ------------------------------------------------------------------ craft analysis

        private sealed class P
        {
            public string Id;
            public AvailablePart Ap;
            public int Stage;           // istg: the stage that activates it
            public string Parent;
            public bool Decoupler;
            public bool Engine;
            public double ThrustVac, IspVac, IspSl;
            public double DryMass;
            public readonly Dictionary<string, double> Fuel = new Dictionary<string, double>();
            public HashSet<string> Propellants;
            public int DropStage = -1;  // the stage at which a decoupler takes it away, -1 = stays to the end
        }

        private static AvailablePart Find(string raw)
        {
            int us = raw.LastIndexOf('_');
            string name = us > 0 ? raw.Substring(0, us) : raw;
            return PartLoader.getPartInfoByName(name) ?? PartLoader.getPartInfoByName(name.Replace('_', '.')) ?? PartLoader.getPartInfoByName(name.Replace('.', '_'));
        }

        private static PlayerDesign Analyze(string path)
        {
            ConfigNode craft = ConfigNode.Load(path);
            if (craft == null) return null;
            var parts = new Dictionary<string, P>();
            var order = new List<P>();
            var d = new PlayerDesign { CraftPath = path, Name = craft.GetValue("ship") ?? Path.GetFileNameWithoutExtension(path) };
            d.IsPlane = string.Equals(craft.GetValue("type"), "SPH", StringComparison.OrdinalIgnoreCase);
            int podSize = -1, shieldSize = -1;
            bool selfShielded = false;
            foreach (ConfigNode n in craft.GetNodes("PART"))
            {
                string raw = n.GetValue("part") ?? string.Empty;
                AvailablePart ap = Find(raw);
                if (ap == null || ap.partPrefab == null) continue;
                var p = new P { Id = raw, Ap = ap, DryMass = ap.partPrefab.mass };
                int istg; p.Stage = int.TryParse(n.GetValue("istg"), out istg) ? istg : -1;
                Part prefab = ap.partPrefab;
                p.Decoupler = prefab.FindModuleImplementing<ModuleDecouple>() != null || prefab.FindModuleImplementing<ModuleAnchoredDecoupler>() != null;
                // A dual-mode engine (RAPIER) is a jet and a rocket: rate it by its rocket mode, and mark the plane as
                // having jets (2026-10-30: its first module is the air-breathing one, so its rocket delta-v was ignored).
                ModuleEngines e = null;
                bool breathes = false;
                foreach (ModuleEngines m in prefab.FindModulesImplementing<ModuleEngines>())
                {
                    bool air = false;
                    foreach (Propellant pr in m.propellants) if (pr.name == "IntakeAir") air = true;
                    if (air) { breathes = true; if (e == null) e = m; }
                    else if (e == null || EngineBreathes(e)) e = m;
                }
                if (breathes) d.HasJets = true;
                if (e != null && e.atmosphereCurve != null)
                {
                    p.Engine = true;
                    double limit = 1.0;
                    foreach (ConfigNode m in n.GetNodes("MODULE"))
                    {
                        string mn = m.GetValue("name") ?? string.Empty;
                        float pct;
                        if (mn.StartsWith("ModuleEngines", StringComparison.Ordinal) && float.TryParse(m.GetValue("thrustPercentage"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pct)) limit = Mathf.Clamp(pct, 0f, 100f) / 100.0;
                    }
                    p.ThrustVac = e.maxThrust * limit;
                    p.IspVac = e.atmosphereCurve.Evaluate(0f);
                    p.IspSl = e.atmosphereCurve.Evaluate(1f);
                    p.Propellants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (Propellant pr in e.propellants) if (pr.name != "ElectricCharge" && pr.name != "IntakeAir") p.Propellants.Add(pr.name);
                    // Jets breathe air: their thrust is not rocket delta-v (a plane gets a climb credit instead).
                    if (EngineBreathes(e)) p.Engine = false;
                }
                foreach (ConfigNode r in n.GetNodes("RESOURCE"))
                {
                    string rn = r.GetValue("name") ?? string.Empty;
                    double amount;
                    if (!double.TryParse(r.GetValue("amount"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out amount)) continue;
                    PartResourceDefinition def = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition(rn) : null;
                    if (def == null || def.density <= 0) continue;
                    p.Fuel[rn] = (p.Fuel.ContainsKey(rn) ? p.Fuel[rn] : 0) + amount * def.density;
                }
                d.Seats += prefab.CrewCapacity;
                d.Cost += ap.cost;
                // A drogue slows the pod but cannot land it: only main parachutes count (2026-10-29).
                if (prefab.FindModuleImplementing<ModuleParachute>() != null && !ap.name.ToLowerInvariant().Contains("drogue")) d.HasParachute = true;
                // Heat shields: one at least as wide as the widest crew pod, or a pod with its own ablator.
                bool ablator = prefab.FindModuleImplementing<ModuleAblator>() != null;
                if (prefab.CrewCapacity > 0) { podSize = Math.Max(podSize, StackSize(prefab)); if (ablator) selfShielded = true; }
                else if (ablator) shieldSize = Math.Max(shieldSize, StackSize(prefab));
                if (prefab.FindModuleImplementing<ModuleWheelBase>() != null) d.HasWheels = true;
                parts[raw] = p;
                order.Add(p);
            }
            if (order.Count == 0) return null;
            d.HasHeatShield = selfShielded || (shieldSize >= 0 && shieldSize >= podSize);
            d.PartCount = order.Count;
            // Tree: a part lists its children with "link".
            foreach (ConfigNode n in craft.GetNodes("PART"))
            {
                string raw = n.GetValue("part") ?? string.Empty;
                if (!parts.ContainsKey(raw)) continue;
                foreach (string child in n.GetValues("link")) { P c; if (parts.TryGetValue(child, out c)) c.Parent = raw; }
            }
            // A part leaves when the nearest decoupler between it and the root fires (the decoupler goes with it).
            foreach (P p in order)
            {
                int drop = -1;
                for (P q = p; q != null; q = q.Parent != null && parts.ContainsKey(q.Parent) ? parts[q.Parent] : null)
                    if (q.Decoupler && q.Stage >= 0) { drop = Math.Max(drop, q.Stage); }
                p.DropStage = drop;
            }
            // What comes down under the chutes: every part still attached when all the decouplers have fired, when that
            // includes the crew pod (2026-10-29). Main parachutes land about 22 times their own mass softly.
            {
                double land = 0, cap = 0;
                bool crewed = false;
                foreach (P p in order)
                {
                    if (p.DropStage >= 0) continue;
                    land += p.DryMass;
                    foreach (double f in p.Fuel.Values) land += f;
                    Part prefab = p.Ap.partPrefab;
                    if (prefab.CrewCapacity > 0) crewed = true;
                    if (prefab.FindModuleImplementing<ModuleParachute>() != null && !p.Ap.name.ToLowerInvariant().Contains("drogue")) cap += Math.Max(0.05, prefab.mass) * Parachutes.TonnesPerChuteTonne;
                }
                if (crewed) { d.LandingMass = Math.Round(land, 2); d.ChuteCapacity = Math.Round(cap, 2); }
            }

            int maxStage = 0;
            foreach (P p in order) maxStage = Math.Max(maxStage, p.Stage);
            double total = 0;
            bool liftoffSet = false;
            for (int s = maxStage; s >= 0; s--)
            {
                var present = order.FindAll(p => p.DropStage < s);
                var engines = present.FindAll(p => p.Engine && p.Stage >= s && p.ThrustVac > 0);
                if (engines.Count == 0) continue;
                double mass = 0;
                foreach (P p in present) { mass += p.DryMass; foreach (double f in p.Fuel.Values) mass += f; }
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                double thrust = 0, flow = 0, thrustSl = 0;
                foreach (P e in engines)
                {
                    foreach (string pr in e.Propellants) used.Add(pr);
                    thrust += e.ThrustVac;
                    flow += e.ThrustVac / Math.Max(1, e.IspVac);
                    thrustSl += e.ThrustVac * (e.IspVac > 0 ? e.IspSl / e.IspVac : 1);
                }
                if (!liftoffSet) { d.LiftoffTwr = thrustSl / Math.Max(0.001, mass * G0); d.Mass = mass; liftoffSet = true; }
                // Burn the propellant of the group that goes next (the highest drop stage still aboard that holds some).
                int next = -2;
                foreach (P p in present) if (HasFuel(p, used)) next = Math.Max(next, p.DropStage);
                if (next == -2) continue;
                double burned = 0;
                foreach (P p in present)
                {
                    if (p.DropStage != next) continue;
                    foreach (string rn in new List<string>(p.Fuel.Keys)) if (used.Contains(rn)) { burned += p.Fuel[rn]; p.Fuel[rn] = 0; }
                }
                double isp = thrust / Math.Max(1e-6, flow);
                if (burned > 0 && mass - burned > 0) total += isp * G0 * Math.Log(mass / (mass - burned));
            }
            d.DeltaV = total;
            d.ResearchBlocker = ResearchBlocker(path);
            return d;
        }

        private static bool HasFuel(P p, HashSet<string> used)
        {
            foreach (KeyValuePair<string, double> kv in p.Fuel) if (kv.Value > 0 && used.Contains(kv.Key)) return true;
            return false;
        }

        /// <summary>The part's stack node size (0 = 0.625 m, 1 = 1.25 m, 2 = 2.5 m), -1 when it has none.</summary>
        private static int StackSize(Part prefab)
        {
            int size = -1;
            if (prefab.attachNodes != null)
                foreach (AttachNode n in prefab.attachNodes)
                    if (n != null && n.nodeType == AttachNode.NodeType.Stack) size = Math.Max(size, n.size);
            return size;
        }

        /// <summary>True when a heat shield at least 1.25 m wide is researched and bought (career and science games).</summary>
        private static bool CrewShieldResearched()
        {
            try
            {
                bool research = HighLogic.CurrentGame != null && (HighLogic.CurrentGame.Mode == Game.Modes.CAREER || HighLogic.CurrentGame.Mode == Game.Modes.SCIENCE_SANDBOX) && ResearchAndDevelopment.Instance != null;
                foreach (AvailablePart ap in PartLoader.LoadedPartsList)
                {
                    if (ap == null || ap.partPrefab == null || ap.partPrefab.FindModuleImplementing<ModuleAblator>() == null || ap.partPrefab.CrewCapacity > 0) continue;
                    if (StackSize(ap.partPrefab) < 1) continue;
                    if (!research || (ResearchAndDevelopment.PartTechAvailable(ap) && ResearchAndDevelopment.PartModelPurchased(ap))) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Null when every part is researched and bought, else which are not.</summary>
        private static string ResearchBlocker(string path)
        {
            if (HighLogic.CurrentGame == null || (HighLogic.CurrentGame.Mode != Game.Modes.CAREER && HighLogic.CurrentGame.Mode != Game.Modes.SCIENCE_SANDBOX)) return null;
            if (ResearchAndDevelopment.Instance == null) return null;
            ConfigNode craft;
            try { craft = ConfigNode.Load(path); } catch { return null; }
            if (craft == null) return null;
            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode n in craft.GetNodes("PART"))
            {
                AvailablePart ap = Find(n.GetValue("part") ?? string.Empty);
                if (ap == null) { missing.Add("an unknown part"); continue; }
                if (!seen.Add(ap.name)) continue;
                if (!(ResearchAndDevelopment.PartTechAvailable(ap) && ResearchAndDevelopment.PartModelPurchased(ap))) missing.Add(ap.title);
            }
            if (missing.Count == 0) return null;
            return "needs research: " + string.Join(", ", missing.ToArray(), 0, Math.Min(3, missing.Count)) + (missing.Count > 3 ? " and " + (missing.Count - 3) + " more" : string.Empty);
        }
    }
}
