using System;
using System.Collections.Generic;
using System.Reflection;

namespace KSA.KSP.Parts
{
    /// <summary>
    /// HL Airships Core by reflection (KSA-91, no DLL reference, like Kerbalism): the envelope module's altitude control and
    /// buoyancy target, and its anchor, which is what keeps a floating city alive on rails (an unanchored vessel flying in an
    /// atmosphere is deleted when it leaves physics range). Field names from HLAirships.dll: HLEnvelopePartModule with
    /// altitudeControl, targetVerticalVelocity, targetBuoyantVessel, buoyancy, maxBuoyancy, anchored, ToggleAnchor.
    /// </summary>
    public static class KspAirships
    {
        public const string ModuleName = "HLEnvelopePartModule";
        private static bool? _installed;

        /// <summary>HL Airships Core is loaded (its envelope module type exists).</summary>
        public static bool Installed
        {
            get
            {
                if (_installed.HasValue) return _installed.Value;
                bool found = false;
                try
                {
                    foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                    {
                        if (a == null || a.assembly == null) continue;
                        if (a.assembly.GetType("HLAirships." + ModuleName, false) != null || a.assembly.GetType(ModuleName, false) != null) { found = true; break; }
                        foreach (Type t in SafeTypes(a.assembly)) if (t.Name == ModuleName) { found = true; break; }
                        if (found) break;
                    }
                }
                catch (Exception e) { KsaCore.Log.Warn("HL Airships check failed: " + e.Message); }
                _installed = found;
                return found;
            }
        }

        private static IEnumerable<Type> SafeTypes(Assembly a)
        {
            try { return a.GetTypes(); }
            catch (ReflectionTypeLoadException e) { var list = new List<Type>(); foreach (Type t in e.Types) if (t != null) list.Add(t); return list; }
            catch { return new Type[0]; }
        }

        public static List<PartModule> Envelopes(Vessel v)
        {
            var list = new List<PartModule>();
            if (v == null || v.parts == null) return list;
            foreach (Part p in v.parts)
            {
                if (p == null || p.Modules == null) continue;
                foreach (PartModule m in p.Modules) if (m != null && m.moduleName == ModuleName) list.Add(m);
            }
            return list;
        }

        public static bool HasEnvelope(Vessel v) { return Envelopes(v).Count > 0; }

        /// <summary>Asks the envelopes to hold a vertical speed through their own altitude control. False when the module has no such control.</summary>
        public static bool HoldVerticalSpeed(Vessel v, double metresPerSecond)
        {
            bool any = false;
            foreach (PartModule m in Envelopes(v))
            {
                if (!Set(m, "altitudeControl", true)) continue;
                Set(m, "targetVerticalVelocity", (float)metresPerSecond);
                any = true;
            }
            return any;
        }

        /// <summary>Sets the lift target directly, 0 (none) to 1 (all the envelopes give).</summary>
        public static bool SetLift(Vessel v, double fraction)
        {
            bool any = false;
            float f = (float)Math.Max(0, Math.Min(1, fraction));
            foreach (PartModule m in Envelopes(v))
            {
                Set(m, "altitudeControl", false);
                if (Set(m, "targetBuoyantVessel", f)) any = true;
            }
            return any;
        }

        /// <summary>Lift in use as a share of the most the envelopes can give (0 to 1), or -1 when unreadable.</summary>
        public static double LiftFraction(Vessel v)
        {
            double have = 0, max = 0;
            foreach (PartModule m in Envelopes(v))
            {
                double b = Get(m, "buoyancy"), mb = Get(m, "maxBuoyancy");
                if (double.IsNaN(b) || double.IsNaN(mb)) continue;
                have += b; max += mb;
            }
            return max > 0 ? have / max : -1;
        }

        public static bool IsAnchored(Vessel v)
        {
            foreach (PartModule m in Envelopes(v)) { object o = GetObject(m, "anchored"); if (o is bool && (bool)o) return true; }
            return false;
        }

        /// <summary>Anchors the airship where it floats (HL's anchor marks the vessel landed, so it survives on rails).</summary>
        public static bool Anchor(Vessel v, bool on)
        {
            foreach (PartModule m in Envelopes(v))
            {
                object o = GetObject(m, "anchored");
                bool now = o is bool && (bool)o;
                if (now == on) return true;
                MethodInfo toggle = m.GetType().GetMethod("ToggleAnchor", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (toggle == null) return false;
                try { toggle.Invoke(m, null); return true; }
                catch (Exception e) { KsaCore.Log.Warn("Airship anchor: " + e.Message); return false; }
            }
            return false;
        }

        private static bool Set(PartModule m, string field, object value)
        {
            try
            {
                BaseField f = m.Fields != null ? m.Fields[field] : null;
                if (f != null) { f.SetValue(Convert.ChangeType(value, f.FieldInfo.FieldType), m); return true; }
                FieldInfo fi = m.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (fi == null) return false;
                fi.SetValue(m, Convert.ChangeType(value, fi.FieldType));
                return true;
            }
            catch (Exception e) { KsaCore.Log.Warn("Airship field " + field + ": " + e.Message); return false; }
        }

        private static object GetObject(PartModule m, string field)
        {
            try
            {
                BaseField f = m.Fields != null ? m.Fields[field] : null;
                if (f != null) return f.GetValue(m);
                FieldInfo fi = m.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return fi != null ? fi.GetValue(m) : null;
            }
            catch { return null; }
        }

        private static double Get(PartModule m, string field)
        {
            object o = GetObject(m, field);
            if (o == null) return double.NaN;
            try { return Convert.ToDouble(o); } catch { return double.NaN; }
        }
    }
}
