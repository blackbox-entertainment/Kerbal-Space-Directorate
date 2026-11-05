using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// The crew move between the pod and the habitat as the flight goes (0.8.206, 2026-11-04, T: "moving kerbals back and forth from pod
    /// to habitat and vice versa during flight?"). Into the roomiest habitat for every long coast, back into the pod before anything that
    /// sheds parts, enters air or lands. The pilot stays at the controls: a pod with nobody in it is a ship with nobody flying it, unless
    /// a probe core is aboard. Stock crew transfer, so Kerbalism's living space and stress follow the move.
    /// </summary>
    public static class CrewQuarters
    {
        /// <summary>Legs flown as a coast: the crew ride them in the habitat.</summary>
        public static bool IsCoast(IFlightLeg leg)
        {
            return leg is WaitForSoiLeg || leg is CorrectionLeg || leg is InterplanetaryTransferLeg || leg is HoldOrbitLeg || leg is ReturnLeg || leg is TransferLeg || leg is CaptureLeg;
        }

        /// <summary>Legs that shed parts, enter air, dock, land or step outside: everyone in the pod first.</summary>
        public static bool IsPodWork(IFlightLeg leg)
        {
            return leg is ReentryLeg || leg is LandingLeg || leg is AirbrakeLeg || leg is PlaneReturnLeg || leg is SurfaceOpsLeg || leg is ScienceSweepLeg
                || leg is AirlessAscentLeg || leg is AscentLeg || leg is DockingLeg || leg is RendezvousLeg || leg is DeliverLeg || leg is PassPeriapsisLeg;
        }

        /// <summary>Arranges the crew for the leg that just began. Returns a log line or null.</summary>
        public static string Arrange(FlightContext ctx, IFlightLeg leg)
        {
            try
            {
                Vessel v = ctx != null ? ctx.Vessel : null;
                if (v == null || leg == null || v.GetCrewCount() == 0) return null;
                if (IsPodWork(leg)) return ToPod(v, LegName(leg));
                if (IsCoast(leg)) return ToQuarters(v, ctx.Crew != null ? ctx.Crew.PilotName : null, LegName(leg));
            }
            catch (Exception e) { KsaCore.Log.Warn("CrewQuarters.Arrange: " + e.Message); }
            return null;
        }

        private static string LegName(IFlightLeg leg) { return leg.Name ?? leg.GetType().Name; }

        /// <summary>Everyone into the pod (the command part nearest the root with seats). Null when nobody moved.</summary>
        public static string ToPod(Vessel v, string why)
        {
            Part pod = Pod(v);
            if (pod == null) return null;
            var moved = new List<string>();
            foreach (Part p in new List<Part>(v.parts))
            {
                if (p == pod || p.protoModuleCrew == null || p.protoModuleCrew.Count == 0) continue;
                foreach (ProtoCrewMember c in new List<ProtoCrewMember>(p.protoModuleCrew))
                {
                    if (pod.protoModuleCrew.Count >= pod.CrewCapacity) break;
                    if (Move(v, p, pod, c)) moved.Add(First(c.name));
                }
            }
            if (moved.Count == 0) return null;
            Finish(v);
            return Join(moved) + (moved.Count == 1 ? " moves" : " move") + " back into the " + pod.partInfo.title + " for " + why + ".";
        }

        /// <summary>Everyone but the pilot into the roomiest habitat; the pilot too when a probe core can fly the ship. Null when nobody moved.</summary>
        public static string ToQuarters(Vessel v, string pilotName, string why)
        {
            Part pod = Pod(v);
            if (pod == null) return null;
            Part hab = Habitat(v, pod);
            if (hab == null) return null;
            bool probe = HasProbeCore(v, pod);
            ProtoCrewMember pilot = null;
            if (!probe)
            {
                foreach (ProtoCrewMember c in pod.protoModuleCrew) if (pilotName != null && string.Equals(c.name, pilotName, StringComparison.OrdinalIgnoreCase)) { pilot = c; break; }
                if (pilot == null && pod.protoModuleCrew.Count > 0) pilot = pod.protoModuleCrew[0];
            }
            var moved = new List<string>();
            foreach (ProtoCrewMember c in new List<ProtoCrewMember>(pod.protoModuleCrew))
            {
                if (c == pilot) continue;
                if (hab.protoModuleCrew.Count >= hab.CrewCapacity) break;
                if (Move(v, pod, hab, c)) moved.Add(First(c.name));
            }
            if (moved.Count == 0) return null;
            Finish(v);
            return Join(moved) + (moved.Count == 1 ? " moves" : " move") + " into the " + hab.partInfo.title + " for " + why + (pilot != null ? "; " + First(pilot.name) + " stays at the controls." : ".");
        }

        /// <summary>The command part with seats nearest the root: the capsule with the shield and the chutes.</summary>
        private static Part Pod(Vessel v)
        {
            var queue = new Queue<Part>();
            if (v.rootPart != null) queue.Enqueue(v.rootPart);
            var seen = new HashSet<Part>();
            while (queue.Count > 0)
            {
                Part p = queue.Dequeue();
                if (p == null || !seen.Add(p)) continue;
                if (p.CrewCapacity > 0 && p.FindModuleImplementing<ModuleCommand>() != null) return p;
                if (p.parent != null) queue.Enqueue(p.parent);
                foreach (Part c in p.children) queue.Enqueue(c);
            }
            return null;
        }

        /// <summary>The roomiest other crewed part: Kerbalism's habitat volume per seat when it says, else the seats. Null when the pod is the roomiest.</summary>
        private static Part Habitat(Vessel v, Part pod)
        {
            Part best = null;
            double bestRoom = Room(pod);
            foreach (Part p in v.parts)
            {
                if (p == pod || p.CrewCapacity <= 0) continue;
                if (!HabitatEnabled(p)) continue;
                double room = Room(p);
                if (room > bestRoom * 1.2) { bestRoom = room; best = p; }
            }
            return best;
        }

        private static double Room(Part p)
        {
            double volume = 0;
            foreach (PartModule m in p.Modules)
            {
                if (m == null || m.moduleName != "Habitat") continue;
                object vol = Field(m, "volume");
                if (vol is double) volume += (double)vol; else if (vol is float) volume += (float)vol;
            }
            return volume > 0 ? volume : p.CrewCapacity;
        }

        /// <summary>A Kerbalism habitat switched off is no place to put anyone.</summary>
        private static bool HabitatEnabled(Part p)
        {
            foreach (PartModule m in p.Modules)
            {
                if (m == null || m.moduleName != "Habitat") continue;
                object state = Field(m, "state");
                if (state != null && state.ToString().IndexOf("disabled", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            return true;
        }

        private static bool HasProbeCore(Vessel v, Part pod)
        {
            foreach (Part p in v.parts)
            {
                if (p == pod) continue;
                ModuleCommand mc = p.FindModuleImplementing<ModuleCommand>();
                if (mc != null && mc.minimumCrew == 0) return true;
            }
            return false;
        }

        private static bool Move(Vessel v, Part from, Part to, ProtoCrewMember c)
        {
            try
            {
                if (to.protoModuleCrew.Count >= to.CrewCapacity) return false;
                from.RemoveCrewmember(c);
                if (!to.AddCrewmember(c)) { from.AddCrewmember(c); return false; }
                return true;
            }
            catch (Exception e) { KsaCore.Log.Warn("CrewQuarters.Move " + c.name + ": " + e.Message); return false; }
        }

        /// <summary>
        /// The crew list is rebuilt and the kerbals respawned a frame later (0.8.218, T: "I don't see the kerbals"): a despawn and a
        /// spawn in the same frame left the portraits empty, since the old kerbals' destruction, deferred by Unity to the end of the frame,
        /// unregistered the portraits the new ones had just registered. Then the portrait gallery is asked to refresh.
        /// </summary>
        private static void Finish(Vessel v)
        {
            try
            {
                v.CrewListSetDirty();
                v.DespawnCrew();
                UnityEngine.MonoBehaviour host = KsaFlightDirector.Instance;
                if (host != null && host.isActiveAndEnabled) host.StartCoroutine(Respawn(v));
                else { v.SpawnCrew(); GameEvents.onVesselCrewWasModified.Fire(v); RefreshPortraits(v); }
            }
            catch (Exception e) { KsaCore.Log.Warn("CrewQuarters.Finish: " + e.Message); }
        }

        private static IEnumerator Respawn(Vessel v)
        {
            yield return null;
            if (v == null) yield break;
            try { v.SpawnCrew(); GameEvents.onVesselCrewWasModified.Fire(v); }
            catch (Exception e) { KsaCore.Log.Warn("CrewQuarters.Respawn: " + e.Message); }
            yield return null;
            if (v != null) RefreshPortraits(v);
        }

        private static bool _portraitNoted;

        /// <summary>Asks the stock portrait gallery to redraw the active vessel's crew, by reflection: the method's name has moved between versions.</summary>
        private static void RefreshPortraits(Vessel v)
        {
            try
            {
                if (FlightGlobals.ActiveVessel != v) return;
                Type t = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = a.GetType("KerbalPortraitGallery"); } catch { t = null; }
                    if (t != null) break;
                }
                if (t == null) { Note("no KerbalPortraitGallery type"); return; }
                PropertyInfo ip = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object inst = ip != null ? ip.GetValue(null, null) : null;
                if (inst == null) { Note("no KerbalPortraitGallery instance"); return; }
                foreach (string name in new[] { "StartReset", "StartRefresh" })
                {
                    MethodInfo m = t.GetMethod(name, new[] { typeof(Vessel) });
                    if (m == null) continue;
                    m.Invoke(inst, new object[] { v });
                    Note("portraits refreshed by " + name);
                    return;
                }
                Note("no portrait refresh method on KerbalPortraitGallery");
            }
            catch (Exception e) { KsaCore.Log.Warn("CrewQuarters.RefreshPortraits: " + e.Message); }
        }

        private static void Note(string what)
        {
            if (_portraitNoted) return;
            _portraitNoted = true;
            KsaCore.Log.Info("CrewQuarters: " + what);
        }

        private static object Field(PartModule m, string name)
        {
            try { System.Reflection.FieldInfo f = m.GetType().GetField(name); return f != null ? f.GetValue(m) : null; }
            catch { return null; }
        }

        private static string First(string name) { int i = name != null ? name.IndexOf(' ') : -1; return i > 0 ? name.Substring(0, i) : name; }

        private static string Join(List<string> names)
        {
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
        }
    }
}
