using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KSA.Core.Parts;
using CorePart = KSA.Core.Parts.PartInfo;
using UnityEngine;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// Builds Core's part catalog from the game's PartLoader (Jira KSA-42). In Career and Science games only
    /// researched, purchased parts are offered. DLC parts are flagged so stock-only designs stay possible.
    /// </summary>
    public static class KspPartCatalog
    {
        private static PartCatalog _cache;
        private static string _cacheKey;

        /// <summary>Part names as used in craft files and prompts: the AvailablePart.name (dots, not underscores).</summary>
        public static PartCatalog Current
        {
            get
            {
                string key = (HighLogic.CurrentGame != null ? HighLogic.CurrentGame.Mode.ToString() : "none") + ":" + TechStamp();
                if (_cache != null && _cacheKey == key) return _cache;
                _cache = Build();
                _cacheKey = key;
                return _cache;
            }
        }

        private static int _techVersion;
        /// <summary>Changes whenever researched parts change; cache key for anything that depends on the tech tree.</summary>
        public static int TechVersion { get { return _techVersion; } }

        /// <summary>Call when parts are researched or purchased so the next read rebuilds the catalog.</summary>
        public static void Invalidate() { _cache = null; _cacheKey = null; _techVersion++; }

        private static Dictionary<string, double> _techCosts;

        /// <summary>The Science a part's tech node costs, from the game's tech tree: how early the part is (2026-10-30). 0 when unknown.</summary>
        private static double TechCostOf(string techId)
        {
            if (_techCosts == null)
            {
                _techCosts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (ConfigNode tree in GameDatabase.Instance.GetConfigNodes("TechTree"))
                    foreach (ConfigNode n in tree.GetNodes("RDNode"))
                    {
                        double cost;
                        string id = n.GetValue("id");
                        if (!string.IsNullOrEmpty(id) && double.TryParse(n.GetValue("cost"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cost)) _techCosts[id] = cost;
                    }
                KsaCore.Log.Info("Tech tree read for part ages: " + _techCosts.Count + " nodes");
            }
            double found;
            return techId != null && _techCosts.TryGetValue(techId, out found) ? found : 0;
        }

        /// <summary>The first "sizeN" in a part's bulkhead profiles, in metres; 0 when it has none.</summary>
        private static double ProfileDiameter(string profiles)
        {
            if (string.IsNullOrEmpty(profiles)) return 0;
            foreach (string raw in profiles.Split(','))
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "size0": return 0.625;
                    case "size1": return 1.25;
                    case "size1p5": return 1.875;
                    case "size2": return 2.5;
                    case "size3": return 3.75;
                    case "size4": return 5;
                }
            }
            return 0;
        }

        private static string TechStamp()
        {
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX || ResearchAndDevelopment.Instance == null) return "all";
            return ResearchAndDevelopment.Instance.Science.ToString("0") + "/" + _techVersion;
        }

        public static PartCatalog Build()
        {
            var catalog = new PartCatalog();
            if (PartLoader.LoadedPartsList == null) { KsaCore.Log.Warn("PartLoader not ready; empty part catalog"); return catalog; }
            bool gated = HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX && ResearchAndDevelopment.Instance != null;
            int skippedTech = 0, skippedKind = 0;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                try
                {
                    if (ap == null || ap.partPrefab == null) continue;
                    if (ap.category == PartCategories.none || ap.TechHidden) continue;
                    if (ap.name.StartsWith("kerbalEVA", StringComparison.OrdinalIgnoreCase) || ap.name.StartsWith("flag", StringComparison.OrdinalIgnoreCase)) continue;
                    if (gated && !(ResearchAndDevelopment.PartTechAvailable(ap) && ResearchAndDevelopment.PartModelPurchased(ap))) { skippedTech++; continue; }
                    CorePart info = Describe(ap);
                    if (info == null) { skippedKind++; continue; }
                    catalog.Add(info);
                }
                catch (Exception e)
                {
                    KsaCore.Log.Warn("Part " + (ap != null ? ap.name : "?") + " skipped: " + e.Message);
                }
            }
            KsaCore.Log.Info("Part catalog: " + catalog.Count + " parts usable (" + skippedTech + " not researched, " + skippedKind + " unsuitable kinds)");
            return catalog;
        }

        public static CorePart Describe(AvailablePart ap)
        {
            Part p = ap.partPrefab;
            PartKind kind = Classify(ap, p);
            if (kind == PartKind.Other) return null;

            var info = new CorePart
            {
                Name = ap.name,
                Title = ap.title ?? ap.name,
                Kind = kind,
                Mass = p.mass,
                Cost = ap.cost,
                CrewCapacity = p.CrewCapacity,
                Mod = ModOf(ap.partUrl),
                TechCost = TechCostOf(ap.TechRequired),
                RequiresDlc = IsDlc(ap.partUrl)
            };

            // Propellant and resources.
            double fuel = 0;
            if (p.Resources != null)
            {
                foreach (PartResource r in p.Resources)
                {
                    PartResourceDefinition def = PartResourceLibrary.Instance != null ? PartResourceLibrary.Instance.GetDefinition(r.resourceName) : null;
                    if (def == null || def.density <= 0) continue;
                    if (r.resourceName == "ElectricCharge") continue;
                    fuel += r.maxAmount * def.density;
                }
            }
            info.FuelMass = fuel;
            info.Cost = ap.cost; // stock cost already includes full resources

            // Engines.
            ModuleEngines eng = p.FindModuleImplementing<ModuleEngines>();
            if (eng != null)
            {
                info.Thrust = eng.maxThrust;
                if (eng.atmosphereCurve != null)
                {
                    info.IspVacuum = eng.atmosphereCurve.Evaluate(0f);
                    info.IspSeaLevel = eng.atmosphereCurve.Evaluate(1f);
                }
            }

            // Nodes and size.
            AttachNode top = null, bottom = null;
            if (p.attachNodes != null)
            {
                foreach (AttachNode n in p.attachNodes)
                {
                    if (n == null || n.nodeType != AttachNode.NodeType.Stack) continue;
                    if (n.id == "top") top = n;
                    else if (n.id == "bottom") bottom = n;
                }
            }
            info.HasTopNode = top != null;
            info.HasBottomNode = bottom != null;
            int size = top != null ? top.size : (bottom != null ? bottom.size : -1);
            info.Diameter = size < 0 ? 0 : (size == 0 ? 0.625 : 1.25 * size);
            // The part's own profile is the true size: the 0.625 m heat shield has size 1 nodes and passed for 1.25 m (2026-10-30).
            double profile = ProfileDiameter(ap.bulkheadProfiles);
            if (profile > 0) info.Diameter = profile;
            if (p.srfAttachNode != null && p.attachRules != null && p.attachRules.srfAttach) info.Tags.Add("radial");
            // Drogues slow a fast capsule but cannot land it (2026-10-29: a Kerbin orbit design carried only a Mk25 drogue).
            if (p.Modules != null && p.Modules.Contains("ModuleParachute") && ap.name.ToLowerInvariant().Contains("drogue")) info.Tags.Add("drogue");
            if (p.CrewCapacity > 0) info.Tags.Add("crew" + p.CrewCapacity);
            // Kerbalism sample experiments (goo, materials bay) take 10 minutes or more and need sample storage
            // (2026-10-30: the goo on a Suborbital hop showed "no storage space" and never ran). Read by name: no reference.
            if (IsKerbalismSampler(p)) info.Tags.Add("sample");
            // Engines that swivel steer the rocket (2026-10-30: two gimbal-less Reliants could not turn an orbital rocket).
            ModuleGimbal gimbal = p.FindModuleImplementing<ModuleGimbal>();
            if (gimbal != null && gimbal.gimbalRange > 0) info.Tags.Add("gimbal");
            // A crew pod with its own ablator (the Making History KV "Onion" pods) needs no separate heat shield.
            if (p.CrewCapacity > 0 && p.Modules != null && p.Modules.Contains("ModuleAblator")) info.Tags.Add("heatshielded");
            return info;
        }

        private static bool IsKerbalismSampler(Part p)
        {
            if (p == null || p.Modules == null) return false;
            foreach (PartModule m in p.Modules)
            {
                if (m == null || m.moduleName != "Experiment") continue;
                try
                {
                    System.Reflection.FieldInfo f = m.GetType().GetField("sample_amount");
                    if (f != null && Convert.ToDouble(f.GetValue(m)) > 0) return true;
                }
                catch { }
            }
            return false;
        }

        internal static PartKind Classify(AvailablePart ap, Part p)
        {
            PartModuleList mods = p.Modules;
            bool has(string moduleName) { return mods != null && mods.Contains(moduleName); }
            string n = ap.name.ToLowerInvariant();

            if (has("HLEnvelopePartModule") || n.Contains("envelope") || n.Contains("airship") || n.Contains("blimp")) return PartKind.Envelope;
            ModuleEngines eng = p.FindModuleImplementing<ModuleEngines>();
            if (eng != null)
            {
                // Air-breathers (2026-10-30): their own kind, for aircraft missions; the rocket designers never use them.
                foreach (Propellant pr in eng.propellants) if (pr.name == "IntakeAir") return PartKind.Jet;
                if (has("ModuleResourceIntake") || n.Contains("jet") || n.Contains("rapier") || n.Contains("turbo") || n.Contains("ion")) return PartKind.Other;
                bool solid = false;
                foreach (Propellant pr in eng.propellants) if (pr.name == "SolidFuel") solid = true;
                return solid ? PartKind.Booster : PartKind.Engine;
            }
            if (has("ModuleCommand"))
            {
                if (p.CrewCapacity > 0) return PartKind.Command;
                return ap.category == PartCategories.Pods ? PartKind.Command : PartKind.Other; // probe cores: uncrewed, not offered in v1
            }
            // Heat shields before decouplers: stock heat shields can decouple themselves (staging off), and the 0.625 m one
            // was taken for the lightest decoupler, so stages never came apart (2026-10-29).
            if (has("ModuleAblator") || n.Contains("heatshield")) return PartKind.HeatShield;
            if (has("ModuleDecouple") || has("ModuleAnchoredDecoupler")) return PartKind.Decoupler;
            if (has("ModuleParachute")) return PartKind.Parachute;
            if (has("ModuleProceduralFairing")) return PartKind.Fairing;
            if (has("ModuleDockingNode")) return PartKind.Docking;
            if (has("ModuleRCS") || has("ModuleRCSFX")) return PartKind.Rcs;
            // KSA-67: drills and ore converters (stock and Kerbalism), and Kerbalism greenhouses and supply parts.
            if (has("ModuleResourceHarvester") || has("Harvester") || n == "miniisru" || n == "isru" || n.StartsWith("kerbalism-chemicalplant")) return PartKind.Industry;
            if (!has("Harvester") && HasOreConverter(p)) return PartKind.Industry;
            if (has("Greenhouse") || n.StartsWith("kerbalism-container") || n.StartsWith("kerbalism-lifesupport")) return PartKind.LifeSupport;
            if (has("ModuleDeployableSolarPanel") || has("ModuleGenerator") || (ap.category == PartCategories.Electrical)) return PartKind.Power;
            if (has("ModuleDataTransmitter") && ap.category == PartCategories.Communication) return PartKind.Antenna;
            if (has("ModuleScienceLab") || has("ModuleScienceExperiment") || has("Experiment") || ap.category == PartCategories.Science) return PartKind.Science;
            // Rocket fins (stock Basic Fin, AV-T1, AV-R8, Delta-Deluxe winglets; mod fins by name), not wings (2026-10-29).
            if (ap.category == PartCategories.Aero && (has("ModuleLiftingSurface") || has("ModuleControlSurface")) && (n.Contains("fin") || n.Contains("winglet"))) return PartKind.Fin;
            // Wings and elevons (2026-10-30): plane parts, so the agency knows when aircraft missions are in reach.
            if ((has("ModuleLiftingSurface") || has("ModuleControlSurface")) && (ap.category == PartCategories.Aero || n.Contains("wing"))) return PartKind.Wing;
            if (ap.category == PartCategories.Ground || has("ModuleWheelBase"))
            {
                if (n.Contains("leg") || n.Contains("gear")) return PartKind.LandingLeg;
                return n.Contains("wheel") ? PartKind.Wheel : PartKind.LandingLeg;
            }
            if (has("ModuleLifeSupport") || has("LifeSupportModule") || has("ModuleTACLifeSupport") || has("USILS")) return PartKind.LifeSupport;
            if (p.CrewCapacity > 0) return PartKind.Habitat;
            if (ap.category == PartCategories.FuelTank || ap.category == PartCategories.Propulsion)
                return p.Resources != null && p.Resources.Count > 0 ? PartKind.Tank : PartKind.Other;
            if (ap.category == PartCategories.Structural || ap.category == PartCategories.Coupling)
            {
                if (n.Contains("strut") || n.Contains("ladder") || n.Contains("clamp") || n.Contains("wing")) return PartKind.Other;
                return PartKind.Structural;
            }
            if (ap.category == PartCategories.Payload && has("ModuleCargoBay")) return PartKind.Structural;
            return PartKind.Other;
        }

        private static bool HasOreConverter(Part p)
        {
            foreach (PartModule m in p.Modules)
            {
                var c = m as ModuleResourceConverter;
                if (c == null || c.inputList == null) continue;
                foreach (ResourceRatio r in c.inputList) if (r.ResourceName == "Ore") return true;
            }
            return false;
        }

        public static string ModOf(string partUrl)
        {
            if (string.IsNullOrEmpty(partUrl)) return "Stock";
            int slash = partUrl.IndexOf('/');
            string first = slash > 0 ? partUrl.Substring(0, slash) : partUrl;
            return first == "Squad" || first == "SquadExpansion" ? "Stock" : first;
        }

        public static bool IsDlc(string partUrl)
        {
            return !string.IsNullOrEmpty(partUrl) && partUrl.StartsWith("SquadExpansion", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Writes the catalog to PluginData/parts_catalog.txt so part names can be checked without the game.</summary>
        public static string Dump(PartCatalog catalog)
        {
            string path = Path.Combine(KsaSettingsFileDir(), "parts_catalog.txt");
            var sb = new StringBuilder();
            sb.AppendLine("name | title | kind | mass t | fuel t | thrust kN | isp vac | crew | cost | diameter | nodes | mod | dlc");
            foreach (CorePart p in catalog.All)
                sb.AppendLine(p.Name + " | " + p.Title + " | " + p.Kind + " | " + p.Mass.ToString("0.###") + " | " + p.FuelMass.ToString("0.###") + " | " + p.Thrust.ToString("0")
                              + " | " + p.IspVacuum.ToString("0") + " | " + p.CrewCapacity + " | " + p.Cost.ToString("0") + " | " + p.Diameter.ToString("0.###")
                              + " | " + (p.HasTopNode ? "T" : "-") + (p.HasBottomNode ? "B" : "-") + " | " + p.Mod + " | " + (p.RequiresDlc ? "DLC" : "-"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, sb.ToString());
                KsaCore.Log.Info("Part catalog written to " + path);
            }
            catch (Exception e) { KsaCore.Log.Warn("Could not write part catalog: " + e.Message); }
            return path;
        }

        private static string KsaSettingsFileDir() { return Ai.KsaSettingsFile.PluginDataDir; }
    }
}
