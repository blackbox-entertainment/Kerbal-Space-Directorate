using System;
using System.Collections.Generic;
using System.IO;
using KSA.Core;
using UnityEngine;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// KSA-67: the bridge to Kerbalism, the optional life support mod. KSA never references Kerbalism's DLL: it finds
    /// the mod by its loaded assembly or GameData folder, reads its part modules by name, and works on plain KSP
    /// resources (Food, Water, Oxygen, Nitrogen). Without Kerbalism every method here does nothing.
    /// </summary>
    public static class KspLifeSupport
    {
        public const string Kerbalism = "Kerbalism";

        /// <summary>Resources kept topped up on supplied program vessels. Kerbalism's default profile names.</summary>
        private static readonly string[] Supplies = { "Food", "Water", "Oxygen", "Nitrogen" };

        private static bool? _installed;
        private static double _lastTopUpUT = -1;
        private static float _nextCheck;

        /// <summary>Kerbalism is loaded (its plugin assembly, or its GameData folder).</summary>
        public static bool KerbalismInstalled
        {
            get
            {
                if (_installed.HasValue) return _installed.Value;
                bool found = false;
                foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies)
                    if (la != null && !string.IsNullOrEmpty(la.name) && la.name.StartsWith(Kerbalism, StringComparison.OrdinalIgnoreCase)
                        && !la.name.StartsWith("KerbalismConfig", StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found)
                {
                    try { found = Directory.Exists(Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", Kerbalism))); }
                    catch { found = false; }
                }
                _installed = found;
                return found;
            }
        }

        /// <summary>"Kerbalism" when it is installed, else empty: what the Agency's LifeSupportMod is set to.</summary>
        public static string ModName { get { return KerbalismInstalled ? Kerbalism : string.Empty; } }

        /// <summary>
        /// Called every frame from the event hooks. At most twice a real second, and only when at least an in-game hour
        /// has passed, tops up the food, water and air of every vessel carrying a crew the Agency keeps supplied
        /// (their program is supplied or self-sufficient). Cheap: a set lookup per crew member.
        /// </summary>
        public static void Update(Agency agency)
        {
            if (agency == null || !agency.LifeSupportOn || !HighLogic.LoadedSceneIsGame || FlightGlobals.Vessels == null) return;
            if (Time.realtimeSinceStartup < _nextCheck) return;
            _nextCheck = Time.realtimeSinceStartup + 0.5f;
            double now = Planetarium.GetUniversalTime();
            if (_lastTopUpUT >= 0 && now - _lastTopUpUT < 3600 && now >= _lastTopUpUT) return;
            _lastTopUpUT = now;
            HashSet<string> keep;
            try { keep = agency.CrewKeptSupplied(); } catch (Exception e) { KsaCore.Log.Warn("Life support: " + e.Message); return; }
            if (keep.Count == 0) return;
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || !CarriesAny(v, keep)) continue;
                try { TopUp(v); } catch (Exception e) { KsaCore.Log.Warn("Life support top-up of " + v.vesselName + " failed: " + e.Message); }
            }
        }

        private static bool CarriesAny(Vessel v, HashSet<string> keep)
        {
            if (v.loaded)
            {
                foreach (ProtoCrewMember c in v.GetVesselCrew()) if (c != null && keep.Contains(c.name)) return true;
                return false;
            }
            if (v.protoVessel == null) return false;
            foreach (ProtoCrewMember c in v.protoVessel.GetVesselCrew()) if (c != null && keep.Contains(c.name)) return true;
            return false;
        }

        private static void TopUp(Vessel v)
        {
            if (v.loaded)
            {
                foreach (Part p in v.parts)
                    foreach (PartResource r in p.Resources)
                        if (IsSupply(r.resourceName) && r.amount < r.maxAmount) r.amount = r.maxAmount;
                return;
            }
            foreach (ProtoPartSnapshot pp in v.protoVessel.protoPartSnapshots)
                foreach (ProtoPartResourceSnapshot r in pp.resources)
                    if (IsSupply(r.resourceName) && r.amount < r.maxAmount) r.amount = r.maxAmount;
        }

        private static bool IsSupply(string name)
        {
            foreach (string s in Supplies) if (string.Equals(s, name, StringComparison.Ordinal)) return true;
            return false;
        }

        // ------------------------------------------------------------------ part modules, read by name

        private static readonly HashSet<string> IndustryProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_WaterElectrolysis", "_Sabatier", "_Haber", "_Anthraquinone", "_OxDecomp", "_HydrazineProduction", "_NitroHydrazine", "_MRE", "_SOE", "_SCO"
        };

        private static readonly HashSet<string> PowerProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "_RTG", "_FuelCell" };

        /// <summary>The pseudo-resource a Kerbalism ProcessController runs ("_Scrubber"...), or empty.</summary>
        public static string ProcessOf(PartModule m)
        {
            if (m == null || m.moduleName != "ProcessController") return string.Empty;
            object v = Field(m, "resource");
            return v as string ?? string.Empty;
        }

        public static double CapacityOf(PartModule m)
        {
            object v = Field(m, "capacity");
            if (v is double) return (double)v;
            if (v is float) return (float)v;
            return 1.0;
        }

        public static bool IsIndustryProcess(string process) { return IndustryProcesses.Contains(process); }
        public static bool IsPowerProcess(string process) { return PowerProcesses.Contains(process); }
        public static bool IsGreenhouse(PartModule m) { return m != null && m.moduleName == "Greenhouse"; }
        public static bool IsHarvester(PartModule m) { return m != null && m.moduleName == "Harvester"; }

        private static object Field(PartModule m, string name)
        {
            try
            {
                System.Reflection.FieldInfo f = m.GetType().GetField(name);
                return f != null ? f.GetValue(m) : null;
            }
            catch { return null; }
        }
    }
}
