using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Runs a live project's flight (M5): binds the vessel, flies the built-in ascent/reentry program for Kerbin
    /// missions, logs the timeline and achievements to the agency, shows a small HUD, and offers Take Control
    /// when the agency setting allows it. Resolution happens through the recovery / destruction hooks.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class KsaFlightDirector : MonoBehaviour
    {
        public static KsaFlightDirector Instance { get; private set; }

        /// <summary>Static so it survives scene changes; cleared when the project resolves.</summary>
        public static LiveFlightState Live { get; private set; }

        public sealed class LiveFlightState
        {
            public string ProjectId;
            public uint VesselId;
            public double LaunchUT;
            public double MaxAltitude;
            public double MaxSpeed;
            public bool Crashed;
            public List<string> Deaths = new List<string>();
            public bool DirectorFlown;
            public bool AutopilotOn = true;
            public double TargetOrbit = 85000;
            public bool ManualAllowed;
            public bool WantsOrbit;
            public HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        private AscentProgram _program;
        private Rect _hud = new Rect(Screen.width - 330, 120, 320, 190);
        private string _status = "Standing by.";
        private Vessel _vessel;
        private double _nextSample;

        private void Start()
        {
            Instance = this;
            Agency agency = KsaCore.Agency;
            if (agency == null) return;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return;

            LaunchSpec pending = LiveFlightLauncher.Pending;
            if (pending != null)
            {
                LiveFlightLauncher.ClearPending();
                Live = new LiveFlightState
                {
                    ProjectId = pending.Project.Id,
                    VesselId = v.persistentId,
                    LaunchUT = Planetarium.GetUniversalTime(),
                    TargetOrbit = pending.TargetOrbitAltitude,
                    ManualAllowed = pending.ManualOverrideAllowed,
                    WantsOrbit = pending.Mission != null && pending.Mission.Profile != MissionProfile.Suborbital
                };
                agency.BindVessel(pending.Project.Id, v.persistentId.ToString());
                agency.LogFlightEvent(pending.Project.Id, "On the pad: " + v.vesselName + " with " + v.GetCrewCount() + " crew.");
                KsaCore.Log.Info("Flight director adopted " + v.vesselName + " for " + pending.Project.Name);
            }
            else if (Live == null)
            {
                // Resuming after a scene change or reload: is this vessel a live project?
                Project p = agency.ProjectForVessel(v.persistentId.ToString());
                if (p == null) return;
                Live = new LiveFlightState
                {
                    ProjectId = p.Id,
                    VesselId = v.persistentId,
                    LaunchUT = p.LaunchUT >= 0 ? p.LaunchUT : Planetarium.GetUniversalTime(),
                    TargetOrbit = agency.Settings.LiveOrbitAltitude,
                    ManualAllowed = agency.Settings.ManualOverride,
                    DirectorFlown = p.DirectorFlown,
                    WantsOrbit = IsOrbitMission(agency, p)
                };
                foreach (string a in p.FlightAchievements) Live.Logged.Add(a);
                Live.AutopilotOn = !p.DirectorFlown;
                KsaCore.Log.Info("Flight director resumed " + v.vesselName + " for " + p.Name);
            }
            if (Live == null || Live.VesselId != v.persistentId) return;
            _vessel = v;
            _program = new AscentProgram(v, Live.TargetOrbit, Live.WantsOrbit, Log);
            if (Live.AutopilotOn) _program.Engage();
            GameEvents.onVesselSOIChanged.Add(OnSoiChanged);
            GameEvents.onVesselSituationChange.Add(OnSituationChange);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onCrewKilled.Add(OnCrewKilled);
            GameEvents.onVesselChange.Add(OnVesselChange);
        }

        private static bool IsOrbitMission(Agency agency, Project p)
        {
            MissionType t = agency.Catalogs.Missions.Get(p.MissionTypeId);
            return t == null || t.Profile != MissionProfile.Suborbital;
        }

        private void OnDestroy()
        {
            if (_program != null) _program.Disengage();
            GameEvents.onVesselSOIChanged.Remove(OnSoiChanged);
            GameEvents.onVesselSituationChange.Remove(OnSituationChange);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onCrewKilled.Remove(OnCrewKilled);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            if (Instance == this) Instance = null;
        }

        public bool IsLive(Vessel v) { return Live != null && v != null && v.persistentId == Live.VesselId; }
        public static bool IsLiveVessel(uint persistentId) { return Live != null && Live.VesselId == persistentId; }

        private void Log(string text, string achievement = null)
        {
            if (Live == null || KsaCore.Agency == null) return;
            if (achievement != null)
            {
                if (Live.Logged.Contains(achievement)) achievement = null;
                else Live.Logged.Add(achievement);
            }
            KsaCore.Agency.LogFlightEvent(Live.ProjectId, Stamp() + text, achievement);
            _status = text;
        }

        private string Stamp()
        {
            if (Live == null) return string.Empty;
            double t = Planetarium.GetUniversalTime() - Live.LaunchUT;
            return "T+" + KSPUtil.PrintTimeCompact(t, false) + " ";
        }

        private void FixedUpdate()
        {
            if (Live == null || _vessel == null || _vessel != FlightGlobals.ActiveVessel) return;
            double alt = _vessel.altitude;
            double speed = _vessel.srfSpeed;
            if (alt > Live.MaxAltitude) Live.MaxAltitude = alt;
            if (speed > Live.MaxSpeed) Live.MaxSpeed = speed;
            if (Live.AutopilotOn && _program != null)
            {
                string line = _program.Step();
                if (line != null) Log(line);
                _status = _program.Status;
            }
            if (Planetarium.GetUniversalTime() > _nextSample)
            {
                _nextSample = Planetarium.GetUniversalTime() + 30;
                CheckOrbit();
            }
        }

        private void CheckOrbit()
        {
            if (_vessel == null || _vessel.orbit == null || _vessel.mainBody == null) return;
            if (_vessel.situation == Vessel.Situations.ORBITING && _vessel.orbit.PeA > _vessel.mainBody.atmosphereDepth)
                Log("Orbit achieved: " + LiveFlightResolver.FormatKm(_vessel.orbit.ApA) + " x " + LiveFlightResolver.FormatKm(_vessel.orbit.PeA) + ".", "Orbit:" + _vessel.mainBody.bodyName);
        }

        // ------------------------------------------------------------------ events

        private void OnSituationChange(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> data)
        {
            if (!IsLive(data.host)) return;
            string body = data.host.mainBody != null ? data.host.mainBody.bodyName : "Kerbin";
            switch (data.to)
            {
                case Vessel.Situations.FLYING:
                    if (data.from == Vessel.Situations.PRELAUNCH || data.from == Vessel.Situations.LANDED) Log("Liftoff.", "Flight:" + body);
                    break;
                case Vessel.Situations.SUB_ORBITAL:
                    Log("Suborbital: above the atmosphere.", "Suborbit:" + body);
                    break;
                case Vessel.Situations.ORBITING:
                    CheckOrbit();
                    break;
                case Vessel.Situations.ESCAPING:
                    Log("Escape trajectory from " + body + ".", "Escape:" + body);
                    break;
                case Vessel.Situations.LANDED:
                    if (data.from != Vessel.Situations.PRELAUNCH) Log("Landed on " + body + " at " + Math.Round(data.host.srfSpeed, 1) + " m/s.", "Land:" + body);
                    break;
                case Vessel.Situations.SPLASHED:
                    Log("Splashdown on " + body + " at " + Math.Round(data.host.srfSpeed, 1) + " m/s.", "Land:" + body);
                    break;
            }
        }

        private void OnSoiChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> data)
        {
            if (!IsLive(data.host) || data.to == null) return;
            Log("Entered the sphere of influence of " + data.to.bodyName + ".", "Flyby:" + data.to.bodyName);
        }

        private void OnCrewKilled(EventReport report)
        {
            if (Live == null || report == null || string.IsNullOrEmpty(report.sender)) return;
            Project p = KsaCore.Agency != null ? KsaCore.Agency.GetProject(Live.ProjectId) : null;
            if (p == null || !p.Crew.Contains(report.sender)) return;
            if (!Live.Deaths.Contains(report.sender)) Live.Deaths.Add(report.sender);
            Live.Crashed = true;
            Log(report.sender + " was killed" + (string.IsNullOrEmpty(report.msg) ? "." : ": " + report.msg));
        }

        public static void NoteCrash(Vessel v, string detail)
        {
            if (Live == null || v == null || v.persistentId != Live.VesselId) return;
            if (!Live.Crashed && Instance != null) Instance.Log("Impact: " + detail);
            Live.Crashed = true;
        }

        private void OnVesselWillDestroy(Vessel v)
        {
            if (!IsLive(v) || KsaCore.Agency == null) return;
            Project p = KsaCore.Agency.GetProject(Live.ProjectId);
            if (p == null || p.Status != ProjectStatus.InFlight) return;
            // Destroyed without recovery: crash or lost. Recovery fires its own event before this one and resolves first.
            if (v.state == Vessel.State.DEAD || Live.Crashed || Live.Deaths.Count > 0)
            {
                Log("Vessel destroyed.");
                Resolve(false);
            }
        }

        private void OnVesselChange(Vessel v)
        {
            // Switching to debris or another craft: keep flying only our vessel.
            if (Live != null && v != null && v.persistentId == Live.VesselId) _vessel = v;
        }

        /// <summary>Called by the recovery hook for the live vessel; also by destruction. Clears the live state.</summary>
        public static void Resolve(bool recovered)
        {
            if (Live == null || KsaCore.Agency == null) return;
            var result = new LiveFlightResult
            {
                Recovered = recovered,
                Crashed = Live.Crashed,
                DirectorFlown = Live.DirectorFlown,
                MaxAltitude = Live.MaxAltitude,
                MaxSpeed = Live.MaxSpeed,
                DaysInSpace = Math.Max(0.02, (Planetarium.GetUniversalTime() - Live.LaunchUT) / KsaCore.Clock.SecondsPerDay)
            };
            result.Deaths.AddRange(Live.Deaths);
            foreach (string a in Live.Logged) result.Achievements.Add(a);
            if (recovered) result.Achievements.Add("Recover:-");
            string pid = Live.ProjectId;
            Live = null;
            KSA.Core.Projects.MissionReport r = KsaCore.Agency.ResolveLive(pid, result);
            if (r != null) KsaCore.Log.Info("Live flight resolved: " + r.ProjectName + " " + Agency.OutcomeText(r.Outcome));
            if (Instance != null && Instance._program != null) Instance._program.Disengage();
        }

        // ------------------------------------------------------------------ controls

        public void TakeControl()
        {
            if (Live == null || !Live.ManualAllowed) return;
            Live.AutopilotOn = false;
            Live.DirectorFlown = true;
            if (_program != null) _program.Disengage();
            KsaCore.Agency.SetDirectorFlown(Live.ProjectId, true);
            _status = "Director flying.";
        }

        public void HandBack()
        {
            if (Live == null) return;
            Live.AutopilotOn = true;
            if (_program != null) _program.Engage();
            KsaCore.Agency.SetDirectorFlown(Live.ProjectId, false);
        }

        private void OnGUI()
        {
            if (Live == null || _vessel == null || KsaCore.Agency == null) return;
            _hud = GUILayout.Window(GetInstanceID(), _hud, DrawHud, "KSA Flight Director");
        }

        private void DrawHud(int id)
        {
            Project p = KsaCore.Agency.GetProject(Live.ProjectId);
            GUILayout.Label(p != null ? p.Name : Live.ProjectId);
            GUILayout.Label("Phase: " + (Live.AutopilotOn && _program != null ? _program.Phase.ToString() : "Manual"));
            GUILayout.Label(_status);
            GUILayout.Label("Alt " + LiveFlightResolver.FormatKm(_vessel.altitude) + "   Ap " + LiveFlightResolver.FormatKm(_vessel.orbit != null ? _vessel.orbit.ApA : 0) + "   Pe " + LiveFlightResolver.FormatKm(_vessel.orbit != null ? _vessel.orbit.PeA : 0));
            GUILayout.Label("Speed " + Math.Round(_vessel.srfSpeed) + " m/s   Stage " + _vessel.currentStage + "   " + Stamp());
            GUILayout.BeginHorizontal();
            if (Live.ManualAllowed)
            {
                if (Live.AutopilotOn) { if (GUILayout.Button("Take control")) TakeControl(); }
                else if (GUILayout.Button("Hand back to crew")) HandBack();
            }
            else GUILayout.Label("Crew flying (manual override off in agency.cfg)");
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }
    }
}
