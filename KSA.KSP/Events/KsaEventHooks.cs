using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using KSA.KSP.Roster;
using UnityEngine;

namespace KSA.KSP.Events
{
    /// <summary>
    /// Translates game events into Agency calls (Jira KSA-21). One instance per scene; subscribes in Awake and
    /// unsubscribes in OnDestroy, so handlers never double up across scene changes.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EveryScene, false)]
    public class KsaEventHooks : MonoBehaviour
    {
        private const float TickIntervalSeconds = 5f;
        private float _nextTick;

        private void Update()
        {
            if (KsaCore.Agency == null || !HighLogic.LoadedSceneIsGame) return;
            if (Time.realtimeSinceStartup < _nextTick) return;
            _nextTick = Time.realtimeSinceStartup + TickIntervalSeconds;
            KsaCore.TickAgency("timer");
        }

        private void Awake()
        {
            GameEvents.onLaunch.Add(OnLaunch);
            GameEvents.onVesselRecovered.Add(OnVesselRecovered);
            GameEvents.onCrewKilled.Add(OnCrewKilled);
            GameEvents.onCrewOnEva.Add(OnCrewOnEva);
            GameEvents.onCrash.Add(OnCrash);
            GameEvents.onKerbalAdded.Add(OnRosterChanged);
            GameEvents.onKerbalRemoved.Add(OnRosterChanged);
            GameEvents.onKerbalTypeChange.Add(OnKerbalTypeChange);
            GameEvents.OnTechnologyResearched.Add(OnTechChanged);
            GameEvents.OnPartPurchased.Add(OnPartPurchased);
        }

        private void OnDestroy()
        {
            GameEvents.onLaunch.Remove(OnLaunch);
            GameEvents.onVesselRecovered.Remove(OnVesselRecovered);
            GameEvents.onCrewKilled.Remove(OnCrewKilled);
            GameEvents.onCrewOnEva.Remove(OnCrewOnEva);
            GameEvents.onCrash.Remove(OnCrash);
            GameEvents.onKerbalAdded.Remove(OnRosterChanged);
            GameEvents.onKerbalRemoved.Remove(OnRosterChanged);
            GameEvents.onKerbalTypeChange.Remove(OnKerbalTypeChange);
            GameEvents.OnTechnologyResearched.Remove(OnTechChanged);
            GameEvents.OnPartPurchased.Remove(OnPartPurchased);
        }

        private static Agency Agency { get { return KsaCore.Agency; } }

        // ---------------------------------------------------------------- launch

        private void OnLaunch(EventReport report)
        {
            if (Agency == null) return;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return;
            List<string> ids = CrewIds(v.GetVesselCrew());
            if (ids.Count == 0) return;
            string missionId = Agency.BeginMission(ids, v.vesselName);
            KsaCore.Log.Info("Launch: " + v.vesselName + " with " + ids.Count + " crew, mission " + missionId);
        }

        // ---------------------------------------------------------------- recovery

        private void OnVesselRecovered(ProtoVessel pv, bool quick)
        {
            if (Agency == null || pv == null) return;
            List<ProtoCrewMember> crew = pv.GetVesselCrew();
            foreach (ProtoCrewMember pcm in crew)
            {
                if (pcm == null) continue;
                Agency.AddAchievements(pcm.name, KspRosterSource.Achievements(pcm));
                MissionOutcome outcome = HadCrashThisMission(pcm.name) ? MissionOutcome.PartialSuccess : MissionOutcome.Success;
                List<string> notices = Agency.EndMission(pcm.name, outcome);
                KsaCore.Log.Info("Recovered " + pcm.name + " from " + pv.vesselName + " (" + notices.Count + " notices)");
            }
            KsaCore.SyncRoster("recovery");
        }

        private static bool HadCrashThisMission(string crewId)
        {
            CrewProfile p = Agency.Crew.Get(crewId);
            if (p == null) return false;
            MissionEntry m = p.Record.CurrentMission;
            if (m == null) return false;
            foreach (IncidentEntry i in p.Record.Incidents)
                if (i.Kind == IncidentKind.Crash && i.MissionId == m.MissionId) return true;
            return false;
        }

        // ---------------------------------------------------------------- death

        private void OnCrewKilled(EventReport report)
        {
            if (Agency == null || report == null || string.IsNullOrEmpty(report.sender)) return;
            string where = report.origin != null && report.origin.vessel != null ? report.origin.vessel.vesselName : "unknown vessel";
            Agency.RecordDeath(report.sender, "Killed aboard " + where + (string.IsNullOrEmpty(report.msg) ? string.Empty : ": " + report.msg));
            KsaCore.Log.Info("Crew killed: " + report.sender + " (" + where + ")");
        }

        // ---------------------------------------------------------------- EVA

        private void OnCrewOnEva(GameEvents.FromToAction<Part, Part> data)
        {
            if (Agency == null || data.to == null || data.to.protoModuleCrew == null || data.to.protoModuleCrew.Count == 0) return;
            ProtoCrewMember pcm = data.to.protoModuleCrew[0];
            Vessel ship = data.from != null ? data.from.vessel : null;
            CelestialBody body = data.to.vessel != null ? data.to.vessel.mainBody : (ship != null ? ship.mainBody : null);
            string bodyName = body != null ? body.bodyName : "-";

            var achievements = KspRosterSource.Achievements(pcm);
            achievements.Add(KspRosterSource.AchievementKey("EVA", bodyName));
            if (ship != null && (ship.Landed || ship.Splashed)) achievements.Add(KspRosterSource.AchievementKey("Land", bodyName));

            List<string> notices = Agency.AddAchievements(pcm.name, achievements);
            KsaCore.Log.Info("EVA: " + pcm.name + " at " + bodyName + (ship != null && ship.Landed ? " (surface)" : string.Empty) + ", " + notices.Count + " notices");
        }

        // ---------------------------------------------------------------- crashes

        private void OnCrash(EventReport report)
        {
            if (Agency == null || report == null || report.origin == null || report.origin.vessel == null) return;
            Vessel v = report.origin.vessel;
            foreach (ProtoCrewMember pcm in v.GetVesselCrew())
            {
                if (pcm == null) continue;
                CrewProfile p = Agency.Crew.Get(pcm.name);
                if (p == null) continue;
                MissionEntry m = p.Record.CurrentMission;
                // One crash incident per mission is enough; the game fires onCrash per part.
                bool already = false;
                foreach (IncidentEntry i in p.Record.Incidents)
                    if (i.Kind == IncidentKind.Crash && m != null && i.MissionId == m.MissionId) { already = true; break; }
                if (already) continue;
                Agency.RecordIncident(pcm.name, IncidentKind.Crash, "Crash: " + v.vesselName + (string.IsNullOrEmpty(report.msg) ? string.Empty : " (" + report.msg + ")"));
                KsaCore.Log.Info("Crash recorded for " + pcm.name + " on " + v.vesselName);
            }
        }

        // ---------------------------------------------------------------- roster

        private void OnRosterChanged(ProtoCrewMember pcm) { KsaCore.SyncRoster("roster change"); }

        private void OnKerbalTypeChange(ProtoCrewMember pcm, ProtoCrewMember.KerbalType from, ProtoCrewMember.KerbalType to) { KsaCore.SyncRoster("kerbal type change"); }
        private void OnTechChanged(GameEvents.HostTargetAction<RDTech, RDTech.OperationResult> data) { KSA.KSP.Parts.KspPartCatalog.Invalidate(); }
        private void OnPartPurchased(AvailablePart ap) { KSA.KSP.Parts.KspPartCatalog.Invalidate(); }

        private static List<string> CrewIds(List<ProtoCrewMember> crew)
        {
            var ids = new List<string>();
            if (crew == null) return ids;
            foreach (ProtoCrewMember pcm in crew) if (pcm != null) ids.Add(pcm.name);
            return ids;
        }
    }
}
