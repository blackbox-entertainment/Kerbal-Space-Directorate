using System;
using System.Collections.Generic;
using System.IO;
using KSA.Core;
using KSA.Core.Missions;
using KSA.Core.Projects;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// KSA-64: what a project's craft carries, for base growth. With Kerbalism (KSA-67) it also counts greenhouses, scrubber and
    /// water recycler capacity and the food and water aboard, and reads Kerbalism's own drill, converter and RTG modules. Reads the craft the project flew (the approved AI design,
    /// else its template) and counts parts by what they do: drills, ore converters, power sources, seats, and whether it
    /// is a rocket. The same file serves live and simulated flights, so simulated players are never shut out.
    /// </summary>
    public static class KspHardwareCheck
    {
        public static ModuleHardware For(Project p)
        {
            Agency agency = KsaCore.Agency;
            if (p == null || agency == null) return null;
            string path = CraftPathFor(agency, p);
            if (path == null) return null;
            ConfigNode craft;
            try { craft = ConfigNode.Load(path); } catch { return null; }
            if (craft == null) return null;
            var h = new ModuleHardware();
            bool kerbalism = KspLifeSupport.KerbalismInstalled;
            bool command = false, engine = false;
            foreach (ConfigNode part in craft.GetNodes("PART"))
            {
                string raw = part.GetValue("part") ?? string.Empty;
                int us = raw.LastIndexOf('_');
                string name = us > 0 ? raw.Substring(0, us) : raw;
                AvailablePart ap = PartLoader.getPartInfoByName(name) ?? PartLoader.getPartInfoByName(name.Replace('_', '.')) ?? PartLoader.getPartInfoByName(name.Replace('.', '_'));
                if (ap == null || ap.partPrefab == null) continue;
                h.Seats += Math.Max(0, ap.partPrefab.CrewCapacity);
                if (kerbalism) CountSupplies(ap.partPrefab, h);
                bool industry = false, power = false;
                foreach (PartModule m in ap.partPrefab.Modules)
                {
                    if (m == null) continue;
                    if (kerbalism)
                    {
                        // Kerbalism swaps the stock drill, converter and RTG modules for its own (KSA-67).
                        if (KspLifeSupport.IsHarvester(m)) { h.Drills++; continue; }
                        if (KspLifeSupport.IsGreenhouse(m)) { h.Greenhouses++; continue; }
                        string process = KspLifeSupport.ProcessOf(m);
                        if (process.Length > 0)
                        {
                            if (KspLifeSupport.IsIndustryProcess(process)) industry = true;
                            else if (KspLifeSupport.IsPowerProcess(process)) power = true;
                            else if (string.Equals(process, "_Scrubber", StringComparison.OrdinalIgnoreCase)) h.Scrubbers += (int)Math.Round(KspLifeSupport.CapacityOf(m));
                            else if (string.Equals(process, "_WaterRecycler", StringComparison.OrdinalIgnoreCase)) h.WaterRecyclers += (int)Math.Round(KspLifeSupport.CapacityOf(m));
                            continue;
                        }
                    }
                    if (m is ModuleResourceHarvester || m is ModuleAsteroidDrill) h.Drills++;
                    else if (m is ModuleResourceConverter) { var c = (ModuleResourceConverter)m; if (Uses(c.inputList, "Ore")) h.Isru++; else if (Uses(c.outputList, "ElectricCharge")) h.Power++; }
                    else if (m is ModuleDeployableSolarPanel) h.Power++;
                    else if (m is ModuleGenerator && Generates((ModuleGenerator)m)) h.Power++;
                    else if (m is ModuleCommand) command = true;
                    else if (m is ModuleEngines) engine = true;
                }
                if (industry) h.Isru++;   // one per part, however many processes it can run
                if (power) h.Power++;
            }
            h.Rocket = command && engine;
            return h;
        }

        /// <summary>Food and water the part carries as loaded (Kerbalism gives every crew pod a few days' worth).</summary>
        private static void CountSupplies(Part prefab, ModuleHardware h)
        {
            if (prefab.Resources == null) return;
            foreach (PartResource r in prefab.Resources)
            {
                if (r == null) continue;
                if (r.resourceName == "Food") h.Food += r.amount;
                else if (r.resourceName == "Water") h.Water += r.amount;
            }
        }

        private static bool Uses(List<ResourceRatio> list, string resource)
        {
            if (list == null) return false;
            foreach (ResourceRatio r in list) if (string.Equals(r.ResourceName, resource, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>An always-on generator with no fuel to burn (stock RTG).</summary>
        private static bool Generates(ModuleGenerator g)
        {
            try
            {
                if (g.resHandler == null) return false;
                bool makesPower = false;
                foreach (ModuleResource r in g.resHandler.outputResources) if (string.Equals(r.name, "ElectricCharge", StringComparison.OrdinalIgnoreCase)) makesPower = true;
                return makesPower && (g.resHandler.inputResources == null || g.resHandler.inputResources.Count == 0);
            }
            catch { return false; }
        }

        private static string CraftPathFor(Agency agency, Project p)
        {
            if (p.Design != null && p.Design.Verdict == Core.Design.DesignVerdict.Approved && !string.IsNullOrEmpty(p.Design.CraftPath) && File.Exists(p.Design.CraftPath)) return p.Design.CraftPath;
            Template t = agency.Catalogs.Templates.Get(p.TemplateId);
            return t != null ? KspTemplateCheck.ResolvePath(t.Craft) : null;
        }
    }
}
