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

        private static System.Reflection.Assembly _kerbalism;
        private static bool _kerbalismLooked;

        /// <summary>The assembly that defines KERBALISM.API (the loader's Kerbalism.dll is a bootstrap for it), or null.</summary>
        private static System.Reflection.Assembly KerbalismAssembly
        {
            get
            {
                if (_kerbalismLooked) return _kerbalism;
                _kerbalismLooked = true;
                try { foreach (System.Reflection.Assembly a in AppDomain.CurrentDomain.GetAssemblies()) if (a.GetType("KERBALISM.API") != null) { _kerbalism = a; break; } }
                catch (Exception e) { KsaCore.Log.Warn("Kerbalism lookup: " + e.Message); }
                return _kerbalism;
            }
        }

        /// <summary>
        /// Kerbalism's science transmission for the vessel on or off (its "Transmit" device): off saves the antenna's power when the
        /// battery is low (0.8.176). True when the setting changed; false without Kerbalism or when it was already so.
        /// </summary>
        public static bool SetTransmit(Vessel v, bool on)
        {
            try
            {
                System.Reflection.Assembly asm = KerbalismAssembly;
                if (asm == null || v == null) return false;
                Type db = asm.GetType("KERBALISM.DB");
                System.Reflection.MethodInfo get = db != null ? db.GetMethod("KerbalismData", new[] { typeof(Vessel) }) : null;
                object vd = get != null ? get.Invoke(null, new object[] { v }) : null;
                System.Reflection.FieldInfo field = vd != null ? vd.GetType().GetField("deviceTransmit") : null;
                if (field == null || (bool)field.GetValue(vd) == on) return false;
                field.SetValue(vd, on);
                return true;
            }
            catch (Exception e) { KsaCore.Log.Warn("Kerbalism transmit: " + e.Message); return false; }
        }

        /// <summary>What Kerbalism says is making and using power aboard, EC/s, biggest first (0.8.176); null without Kerbalism.</summary>
        public static string PowerReport(Vessel v)
        {
            try
            {
                System.Reflection.Assembly asm = KerbalismAssembly;
                if (asm == null || v == null) return null;
                Type api = asm.GetType("KERBALISM.API");
                System.Reflection.MethodInfo m = api != null ? api.GetMethod("ResourceBrokers", new[] { typeof(Vessel), typeof(string) }) : null;
                System.Collections.IEnumerable list = m != null ? m.Invoke(null, new object[] { v, "ElectricCharge" }) as System.Collections.IEnumerable : null;
                if (list == null) return null;
                var items = new List<KeyValuePair<string, double>>();
                double net = 0;
                foreach (object o in list)
                {
                    string[] key = o.GetType().GetProperty("Key").GetValue(o, null) as string[];
                    double rate = Convert.ToDouble(o.GetType().GetProperty("Value").GetValue(o, null));
                    string name = key != null && key.Length > 0 ? key[key.Length - 1] : "?";
                    items.Add(new KeyValuePair<string, double>(name, rate));
                    net += rate;
                }
                items.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
                var sb = new System.Text.StringBuilder("Power: net " + net.ToString("+0.00;-0.00") + " EC/s");
                int n = 0;
                foreach (KeyValuePair<string, double> kv in items) { if (n >= 6) break; sb.Append(n == 0 ? ": " : ", ").Append(kv.Key).Append(' ').Append(kv.Value.ToString("+0.00;-0.00")); n++; }
                return sb.Append('.').ToString();
            }
            catch (Exception e) { KsaCore.Log.Warn("Kerbalism power report: " + e.Message); return null; }
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
