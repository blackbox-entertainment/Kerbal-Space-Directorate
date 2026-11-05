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

        /// <summary>Kerbalism's brokers of electric charge aboard, (name, EC/s); null without Kerbalism or with nothing to read.</summary>
        private static List<KeyValuePair<string, double>> Brokers(Vessel v)
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
                foreach (object o in list)
                {
                    string[] key = o.GetType().GetProperty("Key").GetValue(o, null) as string[];
                    double rate = Convert.ToDouble(o.GetType().GetProperty("Value").GetValue(o, null));
                    string name = key != null && key.Length > 0 ? key[key.Length - 1] : "?";
                    items.Add(new KeyValuePair<string, double>(name, rate));
                }
                return items;
            }
            catch (Exception e) { KsaCore.Log.Warn("Kerbalism power brokers: " + e.Message); return null; }
        }

        /// <summary>What Kerbalism says is making and using power aboard, EC/s, biggest first (0.8.176); null without Kerbalism.</summary>
        public static string PowerReport(Vessel v)
        {
            List<KeyValuePair<string, double>> items = Brokers(v);
            if (items == null) return null;
            double net = 0;
            foreach (KeyValuePair<string, double> kv in items) net += kv.Value;
            items.Sort((a, b) => Math.Abs(b.Value).CompareTo(Math.Abs(a.Value)));
            var sb = new System.Text.StringBuilder("Power: net " + net.ToString("+0.00;-0.00") + " EC/s");
            int n = 0;
            foreach (KeyValuePair<string, double> kv in items) { if (n >= 6) break; sb.Append(n == 0 ? ": " : ", ").Append(kv.Key).Append(' ').Append(kv.Value.ToString("+0.00;-0.00")); n++; }
            return sb.Append('.').ToString();
        }

        /// <summary>What the ship is using, EC/s, by Kerbalism's brokers (0.8.219): every drain summed, the panels left out. NaN without Kerbalism.</summary>
        public static double PowerConsumption(Vessel v)
        {
            List<KeyValuePair<string, double>> items = Brokers(v);
            if (items == null) return double.NaN;
            double used = 0;
            foreach (KeyValuePair<string, double> kv in items) if (kv.Value < 0) used -= kv.Value;
            return used;
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

        private static Dictionary<string, bool> _belts;
        /// <summary>
        /// Whether Kerbalism gives this world radiation belts (0.8.222): Kerbin, Jool, Moho and Bop do; Eve, Duna and Dres do not, so a long
        /// wait in a high orbit there is no dose. With no radiation configs to read, every world is taken to have belts, as before.
        /// </summary>
        public static bool HasRadiationBelts(CelestialBody body)
        {
            if (body == null) return false;
            if (_belts == null)
            {
                var belts = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var models = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                    foreach (ConfigNode m in GameDatabase.Instance.GetConfigNodes("RadiationModel"))
                    {
                        string name = m.GetValue("name") ?? string.Empty;
                        bool inner = string.Equals(m.GetValue("has_inner"), "true", StringComparison.OrdinalIgnoreCase);
                        bool outer = string.Equals(m.GetValue("has_outer"), "true", StringComparison.OrdinalIgnoreCase);
                        if (name.Length > 0) models[name] = inner || outer;
                    }
                    foreach (ConfigNode b in GameDatabase.Instance.GetConfigNodes("RadiationBody"))
                    {
                        string name = b.GetValue("name") ?? string.Empty, model = b.GetValue("radiation_model") ?? string.Empty;
                        bool has;
                        if (name.Length > 0) belts[name] = models.TryGetValue(model, out has) && has;
                    }
                }
                catch (Exception e) { KsaCore.Log.Warn("Radiation configs unreadable: " + e.Message); }
                _belts = belts;
                KsaCore.Log.Info("Radiation belts: " + (belts.Count == 0 ? "no configs, every world counts as belted" : string.Join(", ", new List<string>(Belted(belts)).ToArray())));
            }
            if (_belts.Count == 0) return true;
            bool known;
            return _belts.TryGetValue(body.bodyName, out known) && known;
        }

        private static IEnumerable<string> Belted(Dictionary<string, bool> belts)
        {
            foreach (KeyValuePair<string, bool> kv in belts) if (kv.Value) yield return kv.Key;
        }
        public static bool IsGreenhouse(PartModule m) { return m != null && m.moduleName == "Greenhouse"; }
        public static bool IsHarvester(PartModule m) { return m != null && m.moduleName == "Harvester"; }
        /// <summary>What a Kerbalism Harvester setup digs ("Water", "Ore"), or empty.</summary>
        public static string HarvesterResource(PartModule m) { return IsHarvester(m) ? Field(m, "resource") as string ?? string.Empty : string.Empty; }
        /// <summary>Kerbalism harvester type: 0 crustal, 1 oceanic, 2 atmospheric, 3 exospheric, 4 space object. -1 when unknown.</summary>
        public static int HarvesterType(PartModule m)
        {
            object v = IsHarvester(m) ? Field(m, "type") : null;
            if (v is int) return (int)v;
            int t; return v != null && int.TryParse(v.ToString(), out t) ? t : -1;
        }

        /// <summary>
        /// Water in the ground at a point (0 to 1), from the game's resource map as Kerbalism's drills read it (0.8.205). NaN without
        /// Kerbalism or when the map has no answer.
        /// </summary>
        public static double WaterAt(CelestialBody body, double lat, double lon)
        {
            if (!KerbalismInstalled || body == null || ResourceMap.Instance == null) return double.NaN;
            try
            {
                var req = new AbundanceRequest { ResourceType = HarvestTypes.Planetary, ResourceName = "Water", BodyId = body.flightGlobalsIndex, Latitude = lat, Longitude = lon, Altitude = 0, CheckForLock = false };
                double a = ResourceMap.Instance.GetAbundance(req);
                return double.IsNaN(a) ? double.NaN : Math.Max(0, Math.Min(1, a));
            }
            catch (Exception e) { KsaCore.Log.Warn("WaterAt " + body.bodyName + ": " + e.Message); return double.NaN; }
        }

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
