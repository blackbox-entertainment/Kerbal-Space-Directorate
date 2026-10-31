using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.Flight
{
    /// <summary>
    /// Runs a live project's flight (M5, KSA-51/52/53): binds the vessel, builds the mission's leg list through
    /// <see cref="MissionPlanner"/>, flies it, logs the timeline and achievements to the agency, shows a small HUD,
    /// and offers Take Control when the agency setting allows it. Resolution happens through the recovery and
    /// destruction hooks, or when a leg delivers the vessel on station.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class KsaFlightDirector : MonoBehaviour
    {
        public static KsaFlightDirector Instance { get; private set; }

        /// <summary>Static so it survives scene changes; cleared when the project resolves.</summary>
        public static LiveFlightState Live { get; private set; }

        /// <summary>Drops a live flight whose vessel no longer exists, seen from outside the flight scene (2026-10-28).</summary>
        public static void ForgetStale(string projectId)
        {
            if (Live != null && Live.ProjectId == projectId && HighLogic.LoadedScene != GameScenes.FLIGHT) { KsaCore.Log.Warn("Live flight " + projectId + " has no vessel left; closing it."); Live = null; }
        }

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
            public double BodyOrbit = 50000;
            public double ReentryPe = 35000;
            public double MaxSlope = 0.3;
            public bool ManualAllowed;
            public string TargetBody = "Kerbin";
            /// <summary>A spaceplane from the SPH (2026-10-28).</summary>
            public bool IsPlane;
            public string DockTargetVesselId = string.Empty;
            public string ProgramId = string.Empty;
            public HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public double SciencePoints;
            public bool ScienceMission;
            public double MaxDistanceFromHomeKm;
            public double SurfaceSeconds;
            public double LastSurfaceSample = -1;
            // Why it went wrong, for the lesson and the next design (2026-10-30). The first cause wins.
            public KSA.Core.Flight.FailureCause Cause;
            public string CauseDetail = string.Empty;
            public int LaunchParts;
            public double LastHeight = double.MaxValue;
            public double LastSpeed;
            public bool ChutesOpen;
            public string HotDetail;
            public double TumbleSince = -1;
        }

        /// <summary>Records why the live flight is going wrong; the first cause wins (2026-10-30).</summary>
        public static void NoteCause(KSA.Core.Flight.FailureCause cause, string detail)
        {
            if (Live == null || cause == KSA.Core.Flight.FailureCause.None || Live.Cause != KSA.Core.Flight.FailureCause.None) return;
            Live.Cause = cause;
            Live.CauseDetail = detail ?? string.Empty;
            KsaCore.Log.Info("Flight cause noted: " + cause + " (" + Live.CauseDetail + ")");
        }

        /// <summary>
        /// Watches for what goes wrong, so the lesson names the cause (2026-10-30): a rocket that tumbles or flies sideways
        /// while climbing under power through the air, a part near its heat limit coming down, and how high and fast it
        /// was with the parachutes open or not (for a hard landing).
        /// </summary>
        private bool _rollNoted;

        private void WatchForFailure()
        {
            Vessel v = _vessel;
            if (Live == null || v == null) return;
            if (Live.LaunchParts == 0 && !v.LandedOrSplashed) Live.LaunchParts = v.parts.Count;
            double height = v.radarAltitude > 0 ? Math.Min(v.altitude, v.radarAltitude) : v.altitude;
            Live.LastHeight = height;
            Live.LastSpeed = v.srfSpeed;
            bool open = false;
            foreach (Part p in v.parts)
            {
                ModuleParachute chute = p.FindModuleImplementing<ModuleParachute>();
                if (chute != null && (chute.deploymentState == ModuleParachute.deploymentStates.DEPLOYED || chute.deploymentState == ModuleParachute.deploymentStates.SEMIDEPLOYED)) { open = true; break; }
            }
            Live.ChutesOpen = open;
            CelestialBody b = v.mainBody;
            bool inAir = b != null && b.atmosphere && v.altitude < b.atmosphereDepth && !v.LandedOrSplashed;
            if (!inAir) { Live.TumbleSince = -1; return; }
            string where = Math.Round(v.altitude / 1000.0, 1) + " km, " + Math.Round(v.srfSpeed) + " m/s";
            if (Live.Cause == KSA.Core.Flight.FailureCause.None && v.verticalSpeed > 0 && v.srfSpeed > 50 && v.ctrlState.mainThrottle > 0.05f)
            {
                // A roll about the nose is not a tumble (2026-10-30: a rocket rolling at 156 deg/s with its nose dead on the
                // flight path was logged as tumbling, and the fix added fins). Only the nose swinging counts; roll is noted.
                double spin = VesselControl.TurnRate(v);
                double roll = VesselControl.RollRate(v);
                if (roll > 60 && !_rollNoted) { _rollNoted = true; KsaCore.Log.Info("Flight roll: rolling " + Math.Round(roll) + " deg/s about the nose at " + where + " (nose swinging " + Math.Round(spin) + " deg/s)"); }
                double off = Vector3.Angle(v.ReferenceTransform.up, (Vector3)v.srf_velocity);
                bool bad = spin > 60 || (off > 45 && v.dynamicPressurekPa > 3);
                if (!bad) Live.TumbleSince = -1;
                else if (Live.TumbleSince < 0) Live.TumbleSince = Planetarium.GetUniversalTime();
                else if (Planetarium.GetUniversalTime() - Live.TumbleSince > 1.0)
                {
                    string which = Live.LaunchParts > 0 && v.parts.Count < Live.LaunchParts ? "the upper stage" : "the rocket";
                    NoteCause(KSA.Core.Flight.FailureCause.Tumbled, which + " tumbled at " + where + " (nose " + Math.Round(off) + " deg off the flight path, spinning " + Math.Round(spin) + " deg/s)");
                }
            }
            if (Live.HotDetail == null && v.verticalSpeed < 0 && v.srfSpeed > 500)
            {
                foreach (Part p in v.parts)
                {
                    double ratio = Math.Max(p.maxTemp > 0 ? p.temperature / p.maxTemp : 0, p.skinMaxTemp > 0 ? p.skinTemperature / p.skinMaxTemp : 0);
                    if (ratio < 0.95) continue;
                    Live.HotDetail = p.partInfo.title + " reached " + Math.Round(ratio * 100) + "% of its heat limit at " + where + " coming down";
                    KsaCore.Log.Info("Flight heat: " + Live.HotDetail);
                    break;
                }
            }
        }

        /// <summary>A crash with no cause yet: burned up if a part ran hot on the way down, else a hard landing near the ground.</summary>
        private static void CauseOfCrash()
        {
            if (Live == null || Live.Cause != KSA.Core.Flight.FailureCause.None || (!Live.Crashed && Live.Deaths.Count == 0)) return;
            if (Live.HotDetail != null) NoteCause(KSA.Core.Flight.FailureCause.BurnedUp, Live.HotDetail);
            else if (Live.LastHeight < 300)
                NoteCause(KSA.Core.Flight.FailureCause.HardLanding, "the capsule hit the ground at " + Math.Round(Live.LastSpeed) + " m/s"
                    + (Live.ChutesOpen ? " under open parachutes: they are too small for it" : " with no parachute open"));
        }

        private MissionProgram _program;
        private ScienceRunner _science;
        private FlightContext _ctx;
        // Top right, clear of the stock app toolbar down the right edge. Where the Director drags it is remembered
        // for the rest of the session, so every flight opens it in the same place.
        private static Vector2? _hudPlace;
        private Rect _hud = new Rect(_hudPlace.HasValue ? _hudPlace.Value.x : Screen.width - 400, _hudPlace.HasValue ? _hudPlace.Value.y : 36, 340, 210);
        private string _status = "Standing by.";
        private Vessel _vessel;
        private double _nextSample;
        private bool _haltNoted;

        private bool _adopted;
        private int _adoptTries;
        private float _nextAdoptTry;

        // KSA-62: a hangar-built rocket with agency kerbals aboard. The pad offer and the input watcher.
        private bool _offering;
        private Vessel _offerVessel;
        private static readonly HashSet<uint> Declined = new HashSet<uint>();
        private float _inputGraceUntil;

        private void Start()
        {
            Instance = this;
            TryAdopt();
        }

        private float _waited;

        private void Update()
        {
            DirectorAutomation(); // KSA-68: the AI Director gives the go and brings the game home
            if (Live != null && _vessel != null && _adopted && !_offering) WatchForDirectorInput();
            // The active vessel may not exist yet in Start right after a launch; keep trying until the flight is ready.
            if (_adopted) return;
            if (UnityEngine.Time.unscaledTime < _nextAdoptTry) return;
            if (FlightGlobals.ready && FlightGlobals.ActiveVessel != null) { TryAdopt(); return; }
            _waited += UnityEngine.Time.unscaledDeltaTime;
            if (_waited < 8f) return;
            // Eight seconds in the flight scene with no vessel: the craft never loaded. Scrub the launch and go home.
            _adopted = true;
            LaunchSpec pending = LiveFlightLauncher.Pending;
            LiveFlightLauncher.ClearPending();
            if (pending != null && KsaCore.Agency != null)
            {
                string msg;
                KsaCore.Agency.ReturnToPad(pending.Project.Id, out msg);
                KsaCore.Log.Error("Launch of " + pending.Project.Name + " produced no vessel (craft file failed to load); " + msg);
                ScreenMessages.PostScreenMessage("[KSA] The craft failed to load; " + msg + " Returning to the Space Center.", 10f, ScreenMessageStyle.UPPER_CENTER);
            }
            HighLogic.LoadScene(GameScenes.SPACECENTER);
        }

        private void TryAdopt()
        {
            Agency agency = KsaCore.Agency;
            if (agency == null) { _adopted = true; return; }
            Vessel v = FlightGlobals.ActiveVessel;
            if (v == null) return;
            _adopted = true;

            LaunchSpec pending = LiveFlightLauncher.Pending;
            Project project = null;
            if (pending == null && Live != null && Live.VesselId != v.persistentId && FindVessel(Live.VesselId) == null)
            {
                // Reverted or reloaded past the live vessel: it no longer exists, so the flight is lost to the record.
                KsaCore.Log.Warn("Live vessel " + Live.VesselId + " no longer exists; resolving " + Live.ProjectId + " as simulated");
                string why;
                agency.SimulateInstead(Live.ProjectId, out why);
                Live = null;
            }
            if (pending != null)
            {
                LiveFlightLauncher.ClearPending();
                project = pending.Project;
                Live = new LiveFlightState
                {
                    ProjectId = pending.Project.Id,
                    VesselId = v.persistentId,
                    LaunchUT = Planetarium.GetUniversalTime(),
                    TargetOrbit = pending.TargetOrbitAltitude,
                    BodyOrbit = pending.BodyOrbitAltitude > 0 ? pending.BodyOrbitAltitude : agency.Settings.LiveTargetOrbitAltitude,
                    ReentryPe = pending.ReentryPeriapsis > 0 ? pending.ReentryPeriapsis : agency.Settings.LiveReentryPeriapsis,
                    MaxSlope = pending.MaxSiteSlope,
                    // A plane (2026-10-28): the crew flies it with the plane legs; the Director may always take over.
                    ManualAllowed = pending.ManualOverrideAllowed || pending.IsPlane,
                    IsPlane = pending.IsPlane,
                    TargetBody = pending.Mission != null ? pending.Mission.Body : "Kerbin",
                    DockTargetVesselId = pending.DockTargetVesselId ?? string.Empty,
                    ProgramId = pending.ProgramId ?? string.Empty
                };
                agency.BindVessel(pending.Project.Id, v.persistentId.ToString());
                agency.LogFlightEvent(pending.Project.Id, "On the pad: " + v.vesselName + " with " + v.GetCrewCount() + " crew.");
                KsaCore.Log.Info("Flight director adopted " + v.vesselName + " for " + pending.Project.Name);
            }
            else if (Live == null)
            {
                // Resuming after a scene change or reload: is this vessel a live project?
                Project p = agency.ProjectForVessel(v.persistentId.ToString());
                bool nearPad = v.mainBody == FlightGlobals.GetHomeBody() && v.srfSpeed < 2.0 && v.altitude - v.terrainAltitude < 100;
                if (p == null && nearPad) p = AdoptFromVab(agency, v);
                if (p == null)
                {
                    // KSA-62: not a project craft, but agency kerbals are aboard: offer to let them fly it.
                    if (nearPad && !Declined.Contains(v.persistentId) && HangarPilot(agency, v) != null)
                    {
                        _offering = true;
                        _offerVessel = v;
                        return;
                    }
                    // Not ours yet: a craft placed on the pad settles for a second or two, so look again a few times.
                    if (nearPad && _adoptTries++ < 12) { _adopted = false; _nextAdoptTry = UnityEngine.Time.unscaledTime + 0.5f; }
                    return;
                }
                project = p;
                MissionType t = agency.Catalogs.Missions.Get(p.MissionTypeId);
                Program g = p.ProgramId.Length > 0 ? agency.GetProgram(p.ProgramId) : null;
                Live = new LiveFlightState
                {
                    ProjectId = p.Id,
                    VesselId = v.persistentId,
                    LaunchUT = p.LaunchUT >= 0 ? p.LaunchUT : Planetarium.GetUniversalTime(),
                    TargetOrbit = agency.Settings.LiveOrbitAltitude,
                    BodyOrbit = agency.Settings.LiveTargetOrbitAltitude,
                    ReentryPe = agency.Settings.LiveReentryPeriapsis,
                    ManualAllowed = agency.Settings.ManualOverride || (p.Design != null && (p.Design.IsPlane || p.Design.Source == KSA.Core.Design.DesignSource.Player)),
                    IsPlane = p.Design != null && p.Design.IsPlane,
                    DirectorFlown = p.DirectorFlown,
                    TargetBody = t != null ? t.Body : "Kerbin",
                    DockTargetVesselId = g != null ? g.VesselId : string.Empty,
                    ProgramId = p.ProgramId
                };
                foreach (string a in p.FlightAchievements) Live.Logged.Add(a);
                Live.AutopilotOn = !p.DirectorFlown;
                KsaCore.Log.Info("Flight director resumed " + v.vesselName + " for " + p.Name);
            }
            else project = agency.GetProject(Live.ProjectId);
            if (Live == null || Live.VesselId != v.persistentId || project == null) return;
            _vessel = v;
            BuildProgram(agency, project, pending == null);
            bool onPad = v.situation == Vessel.Situations.PRELAUNCH || (v.srfSpeed < 1.0 && v.altitude - v.terrainAltitude < 60 && v.mainBody == FlightGlobals.GetHomeBody());
            _holdForLaunch = onPad && agency.Settings.LaunchOnCommand && pending != null;
            _padStage = v.currentStage;
            if (onPad) PreflightChecks(v);
            if (Live.IsPlane && onPad) Log("On the runway: the crew takes off, climbs on the jets and switches to rockets. Without parachutes, you land it: the crew hands you the controls on final approach.", null);
            if (_holdForLaunch) { _status = "Holding on the pad. Press Launch (or the staging key) when you are ready."; Log("On the pad, holding for the Director's go.", null); }
            else if (Live.AutopilotOn && _program != null) _program.Engage();
            GameEvents.onVesselSOIChanged.Add(OnSoiChanged);
            GameEvents.onVesselSituationChange.Add(OnSituationChange);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onCrewKilled.Add(OnCrewKilled);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onCrewOnEva.Add(OnCrewOnEva);
            GameEvents.onPartCouple.Add(OnPartCouple);
        }

        // ------------------------------------------------------------------ KSA-62: hangar flights

        /// <summary>First agency kerbal aboard who is free to fly, or null.</summary>
        private static CrewProfile HangarPilot(Agency agency, Vessel v)
        {
            CrewProfile best = null;
            foreach (ProtoCrewMember pcm in v.GetVesselCrew())
            {
                CrewProfile c = agency.Crew.Get(pcm.name);
                if (c == null || c.Departed || c.Status == CrewStatus.Dead || c.IsFlying || c.ProjectId.Length > 0) continue;
                if (best == null || (c.Role == CrewRole.Pilot && best.Role != CrewRole.Pilot) || (c.Role == best.Role && c.Stars > best.Stars)) best = c;
            }
            return best;
        }

        private static double VacuumDeltaV(Vessel v)
        {
            try { return v != null && v.VesselDeltaV != null ? v.VesselDeltaV.TotalDeltaVVac : 0; }
            catch { return 0; }
        }

        /// <summary>The Director picked a mission for the hangar rocket: create the project and let the crew fly.</summary>
        private void StartHangarFlight(string missionTypeId)
        {
            Agency agency = KsaCore.Agency;
            Vessel v = _offerVessel;
            if (agency == null || v == null) { _offering = false; return; }
            var aboard = new List<string>();
            foreach (ProtoCrewMember pcm in v.GetVesselCrew()) aboard.Add(pcm.name);
            string msg;
            Project p = agency.AdoptHangarLaunch(missionTypeId, v.persistentId.ToString(), v.vesselName, aboard, out msg);
            if (p == null) { ScreenMessages.PostScreenMessage("[KSA] " + msg, 6f, ScreenMessageStyle.UPPER_CENTER); return; }
            _offering = false;
            MissionType t = agency.Catalogs.Missions.Get(p.MissionTypeId);
            Live = new LiveFlightState
            {
                ProjectId = p.Id,
                VesselId = v.persistentId,
                LaunchUT = Planetarium.GetUniversalTime(),
                TargetOrbit = agency.Settings.LiveOrbitAltitude,
                BodyOrbit = agency.Settings.LiveTargetOrbitAltitude,
                ReentryPe = agency.Settings.LiveReentryPeriapsis,
                ManualAllowed = true, // the Director built it; touching the controls takes it back
                TargetBody = t != null ? t.Body : "Kerbin",
                IsPlane = IsPlaneVessel(v)
            };
            _vessel = v;
            BuildProgram(agency, p, false);
            bool onPad = v.situation == Vessel.Situations.PRELAUNCH || (v.srfSpeed < 1.0 && v.altitude - v.terrainAltitude < 60 && v.mainBody == FlightGlobals.GetHomeBody());
            _holdForLaunch = onPad;
            _padStage = v.currentStage;
            if (onPad) PreflightChecks(v);
            if (_holdForLaunch) { _status = "Holding on the pad. Press Launch (or the staging key) when you are ready."; Log("On the pad, holding for the Director's go.", null); }
            else _program.Engage();
            Log("Hangar flight: " + msg + " Touch the controls at any time to take over.", null);
            ScreenMessages.PostScreenMessage("[KSA] " + msg, 6f, ScreenMessageStyle.UPPER_CENTER);
            KsaCore.Log.Info("Flight director adopted hangar rocket " + v.vesselName + " as " + p.Name);
            GameEvents.onVesselSOIChanged.Add(OnSoiChanged);
            GameEvents.onVesselSituationChange.Add(OnSituationChange);
            GameEvents.onVesselWillDestroy.Add(OnVesselWillDestroy);
            GameEvents.onCrewKilled.Add(OnCrewKilled);
            GameEvents.onVesselChange.Add(OnVesselChange);
            GameEvents.onCrewOnEva.Add(OnCrewOnEva);
            GameEvents.onPartCouple.Add(OnPartCouple);
        }

        /// <summary>A plane: built in the SPH, or sitting on the runway.</summary>
        private static bool IsPlaneVessel(Vessel v)
        {
            if (v == null) return false;
            if (v.vesselType == VesselType.Plane) return true;
            return !string.IsNullOrEmpty(v.landedAt) && v.landedAt.IndexOf("Runway", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Missions a hangar craft can fly: seats for the crew, the delta-v the mission asks for (10% grace), from the home
        /// world. Planes get the aircraft missions and Kerbin hops and orbits; rockets everything else. At most ten, hardest first.
        /// </summary>
        private static List<MissionType> HangarMissions(Agency agency, Vessel v, double dv, bool plane)
        {
            var list = new List<MissionType>();
            int seats = Math.Max(1, v.GetCrewCapacity());
            foreach (MissionType t in agency.Catalogs.Missions.Types)
            {
                if (t.ModuleRole.Length > 0 || t.IsRescue || t.IsCustom || t.Floating) continue;
                if (t.Seats > seats) continue;
                if (agency.EvaBlocker(t) != null) continue; // no spacewalk missions before the Astronaut Complex allows them (2026-10-30)
                bool kerbinRocket = string.Equals(t.Body, "Kerbin", StringComparison.OrdinalIgnoreCase) && (t.Profile == MissionProfile.Suborbital || t.Profile == MissionProfile.Orbit);
                if (plane ? !(t.IsAircraft || kerbinRocket) : t.IsAircraft) continue;
                if (!t.IsAircraft && agency.Requirements.For(t).DeltaV > dv * 1.1) continue;
                list.Add(t);
            }
            list.Sort((a, b) => (b.IsAircraft ? 0 : agency.Requirements.For(b).DeltaV).CompareTo(a.IsAircraft ? 0 : agency.Requirements.For(a).DeltaV));
            if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
            return list;
        }

        private void DeclineHangarFlight()
        {
            if (_offerVessel != null) Declined.Add(_offerVessel.persistentId);
            _offering = false;
            _offerVessel = null;
        }

        private void DrawOffer(int id)
        {
            Agency agency = KsaCore.Agency;
            Vessel v = _offerVessel;
            CrewProfile pilot = agency != null && v != null ? HangarPilot(agency, v) : null;
            if (pilot == null) { _offering = false; return; }
            double dv = VacuumDeltaV(v);
            bool plane = IsPlaneVessel(v);
            GUILayout.Label("<b>" + pilot.Name + "</b> is aboard " + v.vesselName + ". What should the crew fly?");
            GUILayout.Label((plane ? "A plane on the runway. " : string.Empty) + "Vacuum delta-v " + Math.Round(dv) + " m/s.");
            // Every mission this craft can fly (2026-10-30, T: "what if I build a rocket and launch, can I attach it to a mission?").
            List<MissionType> options = HangarMissions(agency, v, dv, plane);
            if (options.Count == 0) GUILayout.Label("Nothing the agency flies fits this craft: too little delta-v or too few seats.");
            for (int i = 0; i < options.Count; i++)
            {
                MissionType t = options[i];
                double need = t.IsAircraft ? 0 : agency.Requirements.For(t).DeltaV;
                if (GUILayout.Button(t.Name + (need > 0 ? " (" + Math.Round(need) + " m/s)" : string.Empty) + (agency.MissionsCompleted(t.Id) > 0 ? ", done before" : string.Empty), GUILayout.Height(26f))) StartHangarFlight(t.Id);
            }
            if (GUILayout.Button("Let me fly", GUILayout.Height(30f))) DeclineHangarFlight();
            GUILayout.Label("The crew flies until you touch the stick, throttle or staging; then it is logged as Director-flown.");
            GUI.DragWindow();
        }

        /// <summary>Stick, throttle, translation or staging from the Director hands the vehicle over.</summary>
        private void WatchForDirectorInput()
        {
            if (Live == null || !Live.AutopilotOn || !Live.ManualAllowed || _holdForLaunch) return;
            if (UnityEngine.Time.unscaledTime < _inputGraceUntil) return;
            if (!DirectorInput()) return;
            TakeControl();
            _status = "Director took the controls.";
            Log("The Director took the controls.", null);
            ScreenMessages.PostScreenMessage("[KSA] You have the vehicle. Hand back to crew from the Flight Director window.", 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static bool DirectorInput()
        {
            if (GUIUtility.keyboardControl != 0) return false; // typing in a text field
            if (InputLockManager.IsLocked(ControlTypes.ALL_SHIP_CONTROLS)) return false;
            KeyBinding[] keys =
            {
                GameSettings.PITCH_UP, GameSettings.PITCH_DOWN, GameSettings.YAW_LEFT, GameSettings.YAW_RIGHT, GameSettings.ROLL_LEFT, GameSettings.ROLL_RIGHT,
                GameSettings.THROTTLE_UP, GameSettings.THROTTLE_DOWN, GameSettings.THROTTLE_FULL, GameSettings.THROTTLE_CUTOFF, GameSettings.LAUNCH_STAGES,
                GameSettings.TRANSLATE_UP, GameSettings.TRANSLATE_DOWN, GameSettings.TRANSLATE_LEFT, GameSettings.TRANSLATE_RIGHT, GameSettings.TRANSLATE_FWD, GameSettings.TRANSLATE_BACK
            };
            foreach (KeyBinding k in keys) if (k != null && k.GetKey()) return true;
            AxisBinding[] axes = { GameSettings.AXIS_PITCH, GameSettings.AXIS_YAW, GameSettings.AXIS_ROLL, GameSettings.AXIS_THROTTLE_INC };
            foreach (AxisBinding a in axes) if (a != null && Math.Abs(a.GetAxis()) > 0.25f) return true;
            return false;
        }

        /// <summary>
        /// The Director loaded a Ready project's craft in the VAB and pressed Launch: the vessel name matches the
        /// approved design (or carries the "KSA P#" stem), so the project adopts it and the crew flies it.
        /// </summary>
        private static Project AdoptFromVab(Agency agency, Vessel v)
        {
            string name = v.vesselName ?? string.Empty;
            foreach (Project p in agency.Projects)
            {
                if (p.Status != ProjectStatus.Ready) continue;
                bool byDesign = p.Design != null && p.Design.DesignName.Length > 0 && string.Equals(name, p.Design.DesignName, StringComparison.OrdinalIgnoreCase);
                bool byStem = name.StartsWith("KSA " + p.Id + " ", StringComparison.OrdinalIgnoreCase);
                bool byTemplate = name.IndexOf(p.Name, StringComparison.OrdinalIgnoreCase) >= 0;
                MissionType mt = agency.Catalogs.Missions.Get(p.MissionTypeId);
                bool byMission = mt != null && name.IndexOf(mt.Name, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!byDesign && !byStem && !byTemplate && !byMission) continue;
                var aboard = new List<string>();
                foreach (ProtoCrewMember pcm in v.GetVesselCrew()) aboard.Add(pcm.name);
                string msg;
                if (!agency.AdoptManualLaunch(p.Id, v.persistentId.ToString(), aboard, out msg)) continue;
                KsaCore.Log.Info("Flight director adopted " + name + " rolled out from the VAB for " + p.Name + " (" + aboard.Count + " crew aboard)");
                ScreenMessages.PostScreenMessage("[KSA] " + msg, 6f, ScreenMessageStyle.UPPER_CENTER);
                return p;
            }
            return null;
        }

        private void BuildProgram(Agency agency, Project project, bool resuming)
        {
            MissionType type = agency.Catalogs.Missions.Get(project.MissionTypeId);
            CelestialBody home = FlightGlobals.GetHomeBody();
            CelestialBody target = MissionPlanner.Body(Live.TargetBody) ?? home;
            _ctx = new FlightContext
            {
                Vessel = _vessel,
                Control = new VesselControl(_vessel),
                Log = Log,
                Home = home,
                Target = target,
                Profile = type != null ? type.Profile : MissionProfile.Suborbital,
                ParkingAltitude = Live.TargetOrbit,
                TargetOrbitAltitude = Live.BodyOrbit,
                ReentryPeriapsis = Live.ReentryPe,
                MaxSiteSlope = Live.MaxSlope,
                Crew = agency.FlightProfileFor(project.Id) ?? KSA.Core.Flight.CrewFlightProfile.Neutral(),
                Rng = new System.Random((int)(Live.LaunchUT * 7919) ^ (int)Live.VesselId)
            };
            string pid = project.Id;
            _ctx.AskCrew = (req, cb) => agency.AskCrew(pid, req, cb);
            _ctx.IsPlane = Live.IsPlane;
            _ctx.NoteCause = NoteCause;
            _ctx.TryOrbit = !Live.IsPlane && agency.TriesForOrbit(project);
            if (_ctx.TryOrbit && !resuming) Log("The rocket has the delta-v for orbit (" + project.Design.TotalDeltaV.ToString("0") + " m/s): the crew goes for orbit; the hop counts once they reach space.", null);
            _ctx.HandToDirector = () =>
            {
                if (Live == null) return;
                Live.ManualAllowed = true;
                TakeControl();
                if (_ctx != null) _ctx.Control.StopWarp();
                ScreenMessages.PostScreenMessage("[KSA] Final approach: you have the controls. Land the plane; recovery closes the mission.", 10f, ScreenMessageStyle.UPPER_CENTER);
            };
            if (!resuming) Log(_ctx.Crew.Describe(), null);
            uint dockId;
            if (uint.TryParse(Live.DockTargetVesselId, out dockId)) _ctx.DockTargetId = dockId;
            // KSA-65: a rescue flight knows who it is picking up.
            KSA.Core.Projects.Stranding stranding = project.RescueId.Length > 0 ? agency.GetStranding(project.RescueId) : null;
            if (!resuming) RescueTransferLeg.Aboard.Clear();
            if (stranding != null) foreach (string cid in stranding.Crew) { KSA.Core.Personnel.CrewProfile sc = agency.Crew.Get(cid); _ctx.RescueNames.Add(sc != null ? sc.Name : cid); }
            WireDepot(agency, project, type, resuming);
            _program = MissionPlanner.Build(_ctx, type);
            Live.ScienceMission = type != null && type.IsScience;
            // Experiments run on every live flight (agency.cfg RESEARCH experimentsOnEveryFlight), not only on science
            // missions: a crew report on a hop is Science too. Science missions also get their dedicated sweep legs.
            bool everyFlight = KsaCore.Agency != null && KsaCore.Agency.Settings.ExperimentsOnEveryFlight;
            _science = Live.ScienceMission || everyFlight ? new ScienceRunner(_ctx) : null;
            if (resuming) MissionPlanner.Resume(_program, _ctx, key => Live.Logged.Contains(key));
            var names = new List<string>();
            foreach (IFlightLeg l in _program.Legs) names.Add(l.Name);
            KsaCore.Log.Info("Flight plan for " + project.Name + ": " + string.Join(" > ", names.ToArray()) + (resuming ? " (resuming at leg " + _program.NextLegNumber + ")" : string.Empty));
        }

        /// <summary>
        /// The fuel depot (2026-10-29): a tanker's or a refuelling flight's depot, its keepers, which parts are ours once
        /// docked, what the agency hears after a pump, and whom to follow after undocking.
        /// </summary>
        private void WireDepot(Agency agency, Project project, MissionType type, bool resuming)
        {
            Program depot = project.DepotId.Length > 0 ? agency.GetProgram(project.DepotId) : null;
            Program own = project.ProgramId.Length > 0 ? agency.GetProgram(project.ProgramId) : null;
            if (type != null && type.IsTanker && own != null && agency.IsDepot(own))
            {
                depot = own;
                _ctx.CargoFuel = type.CargoFuel;
                foreach (string id in own.CrewAboard) { KSA.Core.Personnel.CrewProfile c = agency.Crew.Get(id); if (c != null) _ctx.KeeperNames.Add(c.Name); }
            }
            else if (depot != null) _ctx.Refuel = true;
            if (depot == null) return;
            uint did;
            if (uint.TryParse(depot.VesselId, out did)) _ctx.DepotTargetId = did;
            string depotId = depot.Id, pid = project.Id;
            _ctx.ReportDepot = t => agency.SetDepotFuel(depotId, t, 0);
            _ctx.Undocked = (ours, left) =>
            {
                _vessel = ours;
                if (Live != null) Live.VesselId = ours.persistentId;
                agency.BindVessel(pid, ours.persistentId.ToString());
                if (left != null) agency.BindProgramVessel(depotId, left.persistentId.ToString());
            };
            // Our parts carry our launch id. On the pad every part is ours; after a reload, the part with our crew is.
            if (!resuming && _vessel.rootPart != null) { _ctx.OwnLaunchId = _vessel.rootPart.launchID; return; }
            var names = new List<string>();
            bool swapped = Live != null && Live.Logged.Contains("Tanker:Kerbin");
            if (swapped) names.AddRange(_ctx.KeeperNames);
            else foreach (string id in project.Crew) { KSA.Core.Personnel.CrewProfile c = agency.Crew.Get(id); if (c != null) names.Add(c.Name); }
            foreach (Part p in _vessel.parts)
                foreach (ProtoCrewMember c in p.protoModuleCrew)
                    if (names.Contains(c.name)) { _ctx.OwnLaunchId = p.launchID; return; }
            if (_vessel.rootPart != null) _ctx.OwnLaunchId = _vessel.rootPart.launchID;
        }

        private static Vessel FindVessel(uint persistentId)
        {
            if (FlightGlobals.Vessels == null) return null;
            foreach (Vessel v in FlightGlobals.Vessels) if (v != null && v.persistentId == persistentId) return v;
            return null;
        }

        private void OnDestroy()
        {
            if (_program != null) _program.Disengage();
            GameEvents.onVesselSOIChanged.Remove(OnSoiChanged);
            GameEvents.onVesselSituationChange.Remove(OnSituationChange);
            GameEvents.onVesselWillDestroy.Remove(OnVesselWillDestroy);
            GameEvents.onCrewKilled.Remove(OnCrewKilled);
            GameEvents.onVesselChange.Remove(OnVesselChange);
            GameEvents.onCrewOnEva.Remove(OnCrewOnEva);
            GameEvents.onPartCouple.Remove(OnPartCouple);
            if (Instance == this) Instance = null;
        }

        public bool IsLive(Vessel v) { return Live != null && v != null && v.persistentId == Live.VesselId; }
        public static bool IsLiveVessel(uint persistentId) { return Live != null && Live.VesselId == persistentId; }

        private bool _netNoted;

        /// <summary>
        /// The crew never falls without chutes (2026-10-28: an ascent ran dry at 20 km, the plan moved on to "On orbit", and
        /// nothing armed the parachutes, so the pod hit the sea at 198 m/s). Every physics frame, whatever the flight plan
        /// is doing and whoever is flying: when the craft is falling back through an atmosphere on a path that meets the
        /// ground, empty stages are dropped and the chutes are armed (forced open below 2.5 km if still not safe).
        /// </summary>
        private void SurvivalNet()
        {
            Vessel v = _vessel;
            if (v == null || _ctx == null || v.mainBody == null || !v.mainBody.atmosphere) return;
            // Only while the crew flies: when the Director has the controls, the chutes are the Director's call (2026-10-28:
            // the net fired a chute at 166 m right after a Take Control).
            if (Live == null || !Live.AutopilotOn) return;
            if (v.situation == Vessel.Situations.LANDED || v.situation == Vessel.Situations.SPLASHED || v.situation == Vessel.Situations.PRELAUNCH) return;
            // Engines still pushing: a powered descent or a climb that is still burning, not a fall.
            foreach (ModuleEngines e in v.FindPartModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout && e.finalThrust > 0.1f) return;
            double atmo = v.mainBody.atmosphereDepth;
            bool falling = v.verticalSpeed < -10;
            bool comingDown = v.orbit == null || v.orbit.PeA < atmo * 0.5;
            if (!falling || !comingDown || v.altitude > atmo) return;
            // Nothing is burning and the craft falls back on a path that meets the ground: everything under the capsule
            // goes, spent or not, one stage at a time (2026-10-29: a hop came down with its fuelled upper stage still on,
            // fell nose first at 837 m/s and the chutes could not save it).
            string line = null;
            // Only over the home world, where every landing is under parachutes: a lander coasting down to Duna before its
            // landing burn keeps its descent stage.
            // A plane keeps its parts: nothing of it is a spent stage (2026-10-30).
            if (!Live.IsPlane && !_ctx.Control.DropSpentStage(ref line) && v.mainBody.isHomeWorld) _ctx.Control.ShedForReentry(ref line);
            if (line != null) Log(line, null);
            double aboveGround = Math.Max(0, v.altitude - Math.Max(0, v.terrainAltitude));
            string chute = null;
            _ctx.Control.DeployChutes(ref chute, aboveGround < 2500);
            if (chute != null) Log(chute + (_netNoted ? string.Empty : " (falling back: the crew arms the chutes whatever the flight plan says)"), null);
            if (chute != null) _netNoted = true;
        }

        /// <summary>
        /// The crew's pre-launch checklist (2026-10-28): SAS on in Stability Assist, and RCS on when the craft has thrusters,
        /// before the go, so nothing starts to wander on the pad or at liftoff.
        /// </summary>
        private void PreflightChecks(Vessel v)
        {
            if (v == null) return;
            try
            {
                v.ActionGroups.SetGroup(KSPActionGroup.SAS, true);
                if (v.Autopilot != null) v.Autopilot.SetMode(VesselAutopilot.AutopilotMode.StabilityAssist);
                bool rcs = false;
                foreach (ModuleRCS r in v.FindPartModulesImplementing<ModuleRCS>()) if (r.rcsEnabled) { rcs = true; break; }
                if (rcs) v.ActionGroups.SetGroup(KSPActionGroup.RCS, true);
                Log("Pre-launch checks: SAS on" + (rcs ? ", RCS on." : "; no RCS thrusters aboard."), null);
            }
            catch (Exception e) { KsaCore.Log.Warn("Pre-launch checks: " + e.Message); }
        }

        private void Log(string text, string achievement)
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
            if (Live == null || _vessel == null) return;
            if (_holdForLaunch)
            {
                // The Director pressed the staging key: that is the go.
                if (_vessel.currentStage < _padStage || _vessel.situation == Vessel.Situations.FLYING) GoForLaunch();
                else return;
            }
            Vessel active = FlightGlobals.ActiveVessel;
            bool ours = active == _vessel || (active != null && active.isEVA);
            if (!ours) return;
            double alt = _vessel.altitude;
            double speed = _vessel.srfSpeed;
            if (alt > Live.MaxAltitude) Live.MaxAltitude = alt;
            if (speed > Live.MaxSpeed) Live.MaxSpeed = speed;
            TrackExploration();
            TrackFullOrbit();
            // Aircraft (2026-10-30): a plane above the body's high-flight line (18 km on Kerbin) has flown high.
            if (Live.IsPlane && _vessel.situation == Vessel.Situations.FLYING && _vessel.mainBody != null && _vessel.mainBody.atmosphere
                && _vessel.mainBody.scienceValues != null && alt > _vessel.mainBody.scienceValues.flyingAltitudeThreshold && !Live.Logged.Contains("HighAir:" + _vessel.mainBody.bodyName))
                Log("Above " + Math.Round(_vessel.mainBody.scienceValues.flyingAltitudeThreshold / 1000) + " km: high-altitude flight.", "HighAir:" + _vessel.mainBody.bodyName);
            WatchForFailure();
            if (_vessel.state == Vessel.State.DEAD) { Log("Vessel destroyed.", null); Resolve(false); return; }
            if (_science != null)
            {
                string sline = _science.Step();
                if (sline != null) Log(sline, null);
                Live.SciencePoints = _science.PointsCollected;
            }
            SurvivalNet();
            if (Live.AutopilotOn && _program != null)
            {
                string line = _program.Step();
                if (line != null) Log(line, null);
                _status = _program.Status;
                if (_program.Finished && !_program.Failed && _ctx.Delivered) { CheckOrbit(); Resolve(false, true); return; }
                if (_program.Failed && !_haltNoted)
                {
                    _haltNoted = true;
                    string why = _program.Status;
                    Log("Flight program halted: " + why, null);
                    ScreenMessages.PostScreenMessage("[KSA] Flight program halted: " + why, 10f, ScreenMessageStyle.UPPER_CENTER);
                    // The crew's call: re-run the numbers once, or hold for Mission Control.
                    if (_ctx.AskCrew != null && !_retriedOnce)
                    {
                        _ctx.AskCrew(new KSA.Core.Flight.DecisionRequest { Kind = KSA.Core.Flight.DecisionKind.ProgramHalt, Situation = "Flight program halted: " + why, Risk = 0.5 }, d =>
                        {
                            if (Live == null || _program == null || !_program.Failed) return;
                            if (d.Choice == KSA.Core.Flight.DecisionChoice.Proceed) { _retriedOnce = true; RetryProgram(); }
                            else Log("The crew is holding. Take control, retry from the HUD, or resolve the flight from Mission Control.", null);
                        });
                    }
                    else Log("The crew is holding. Take control, retry from the HUD, or resolve the flight from Mission Control.", null);
                }
            }
            if (Planetarium.GetUniversalTime() > _nextSample)
            {
                _nextSample = Planetarium.GetUniversalTime() + 30;
                CheckOrbit();
            }
        }

        /// <summary>Farthest from home and time landed on other worlds, for the exploration medals (KSA-60).</summary>
        private void TrackExploration()
        {
            double now = Planetarium.GetUniversalTime();
            if (Live.LastSurfaceSample < 0) { Live.LastSurfaceSample = now; return; }
            double dt = now - Live.LastSurfaceSample;
            Live.LastSurfaceSample = now;
            if (dt <= 0 || dt > 3600 * 6) return; // a scene change or a long warp jump: skip the gap
            CelestialBody home = FlightGlobals.GetHomeBody();
            if (home != null)
            {
                double km = Math.Max(0, Vector3d.Distance(_vessel.GetWorldPos3D(), home.position) - home.Radius) / 1000.0; // from the surface (0.7.12)
                if (km > Live.MaxDistanceFromHomeKm) Live.MaxDistanceFromHomeKm = km;
                if (_vessel.mainBody != home && (_vessel.situation == Vessel.Situations.LANDED || _vessel.situation == Vessel.Situations.SPLASHED)) Live.SurfaceSeconds += dt;
            }
        }

        private double _orbitStart = -1;
        private CelestialBody _orbitBody;
        private bool _stableNoted;

        /// <summary>A stable orbit: periapsis above the atmosphere (or 10 km above an airless surface), not escaping.</summary>
        private static bool StableOrbit(Vessel v)
        {
            if (v == null || v.orbit == null || v.mainBody == null || v.situation != Vessel.Situations.ORBITING || v.orbit.eccentricity >= 1) return false;
            double floor = v.mainBody.atmosphere ? v.mainBody.atmosphereDepth : 10000;
            return v.orbit.PeA > floor;
        }

        /// <summary>Notes a stable orbit when reached; the orbit counts only once a full orbit has been flown (TrackFullOrbit).</summary>
        private void CheckOrbit()
        {
            if (!StableOrbit(_vessel) || _stableNoted) return;
            _stableNoted = true;
            Log("Stable orbit: " + LiveFlightResolver.FormatKm(_vessel.orbit.ApA) + " x " + LiveFlightResolver.FormatKm(_vessel.orbit.PeA) + " around " + _vessel.mainBody.bodyName + ". It counts once a full orbit is flown.", null);
        }

        /// <summary>
        /// Orbit counts only after one complete orbit (2026-10-28): the craft must stay in a stable orbit of a body for a
        /// whole period. Breaking the orbit (periapsis into the air, a burn out of orbit, a new body) starts the count over.
        /// </summary>
        private void TrackFullOrbit()
        {
            Vessel v = _vessel;
            if (v == null || Live == null) return;
            double now = Planetarium.GetUniversalTime();
            if (!StableOrbit(v)) { _orbitStart = -1; _orbitBody = null; _stableNoted = false; return; }
            if (_orbitStart < 0 || v.mainBody != _orbitBody) { _orbitStart = now; _orbitBody = v.mainBody; CheckOrbit(); return; }
            string key = "Orbit:" + v.mainBody.bodyName;
            if (Live.Logged.Contains(key)) return;
            if (now - _orbitStart >= v.orbit.period)
                Log("Full orbit of " + v.mainBody.bodyName + " completed (" + LiveFlightResolver.FormatKm(v.orbit.ApA) + " x " + LiveFlightResolver.FormatKm(v.orbit.PeA) + ", " + Math.Round(v.orbit.period / 60) + " min).", key);
        }

        // ------------------------------------------------------------------ events

        private void OnSituationChange(GameEvents.HostedFromToAction<Vessel, Vessel.Situations> data)
        {
            if (!IsLive(data.host)) return;
            string body = data.host.mainBody != null ? data.host.mainBody.bodyName : "Kerbin";
            switch (data.to)
            {
                case Vessel.Situations.FLYING:
                    if (data.from == Vessel.Situations.PRELAUNCH || data.from == Vessel.Situations.LANDED) Log("Liftoff from " + body + ".", "Flight:" + body);
                    break;
                case Vessel.Situations.SUB_ORBITAL:
                    if (data.from == Vessel.Situations.LANDED || data.from == Vessel.Situations.PRELAUNCH) Log("Liftoff from " + body + ".", "Flight:" + body);
                    // Only above the air: the game can flip a craft to SUB_ORBITAL for a moment lower down (2026-10-29).
                    else if (data.host.mainBody == null || !data.host.mainBody.atmosphere || data.host.altitude >= data.host.mainBody.atmosphereDepth * 0.99)
                        Log("Suborbital above " + body + ".", "Suborbit:" + body);
                    break;
                case Vessel.Situations.ORBITING:
                    CheckOrbit();
                    break;
                case Vessel.Situations.ESCAPING:
                    Log("Escape trajectory from " + body + ".", "Escape:" + body);
                    break;
                case Vessel.Situations.LANDED:
                    if (data.from != Vessel.Situations.PRELAUNCH) Log("Landed on " + body + " at " + Math.Round(data.host.srfSpeed, 1) + " m/s.", AtHome(data.host) ? null : "Land:" + body);
                    break;
                case Vessel.Situations.SPLASHED:
                    Log("Splashdown on " + body + " at " + Math.Round(data.host.srfSpeed, 1) + " m/s.", AtHome(data.host) ? null : "Land:" + body);
                    break;
            }
        }

        private static bool AtHome(Vessel v) { return v != null && v.mainBody == FlightGlobals.GetHomeBody(); }

        private void OnSoiChanged(GameEvents.HostedFromToAction<Vessel, CelestialBody> data)
        {
            if (!IsLive(data.host) || data.to == null) return;
            // Coming home or crossing into the star's SOI is not a flyby of anything.
            bool flyby = data.to != FlightGlobals.GetHomeBody() && data.to.referenceBody != null && data.to.referenceBody != data.to;
            Log("Entered the sphere of influence of " + data.to.bodyName + ".", flyby ? "Flyby:" + data.to.bodyName : null);
        }

        private void OnCrewOnEva(GameEvents.FromToAction<Part, Part> data)
        {
            if (Live == null || data.from == null || data.from.vessel == null || data.from.vessel.persistentId != Live.VesselId) return;
            string body = data.from.vessel.mainBody != null ? data.from.vessel.mainBody.bodyName : "Kerbin";
            string who = data.to != null && data.to.protoModuleCrew != null && data.to.protoModuleCrew.Count > 0 ? data.to.protoModuleCrew[0].name : "A crew member";
            Log(who + " on EVA at " + body + (data.from.vessel.Landed ? " (surface)" : "") + ".", "EVA:" + body);
        }

        private void OnPartCouple(GameEvents.FromToAction<Part, Part> data)
        {
            if (Live == null || data.from == null || data.to == null || data.from.vessel == null) return;
            Vessel v = data.from.vessel;
            if (v.persistentId != Live.VesselId && (data.to.vessel == null || data.to.vessel.persistentId != Live.VesselId)) return;
            // Docking merges vessels: whichever id survives, keep following it.
            Vessel merged = data.to.vessel ?? v;
            _vessel = merged;
            if (_ctx != null) { _ctx.Vessel = merged; _ctx.Control.Vessel = merged; }
            Live.VesselId = merged.persistentId;
            if (KsaCore.Agency != null) KsaCore.Agency.BindVessel(Live.ProjectId, merged.persistentId.ToString());
        }

        private void OnCrewKilled(EventReport report)
        {
            if (Live == null || report == null || string.IsNullOrEmpty(report.sender)) return;
            Project p = KsaCore.Agency != null ? KsaCore.Agency.GetProject(Live.ProjectId) : null;
            if (p == null || !p.Crew.Contains(report.sender)) return;
            if (!Live.Deaths.Contains(report.sender)) Live.Deaths.Add(report.sender);
            Live.Crashed = true;
            Log(report.sender + " was killed" + (string.IsNullOrEmpty(report.msg) ? "." : ": " + report.msg), null);
            // Everyone aboard is dead: the mission is over now, whatever is left of the vessel.
            bool allDead = true;
            foreach (string id in p.Crew) if (!Live.Deaths.Contains(id)) { allDead = false; break; }
            if (allDead) { Log("All crew lost.", null); Resolve(false); }
        }

        public static void NoteCrash(Vessel v, string detail)
        {
            if (Live == null || v == null || v.persistentId != Live.VesselId) return;
            if (!Live.Crashed && Instance != null) Instance.Log("Impact: " + detail, null);
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
                // Whoever is still aboard a craft that is being destroyed is lost with it. The game reports the deaths a
                // moment after this, too late for the mission report (2026-10-29: "vehicle lost, crew safe", then the pilot
                // was listed as killed on an unknown vessel).
                try
                {
                    foreach (ProtoCrewMember c in v.GetVesselCrew())
                        if (c != null && p.Crew.Contains(c.name) && !Live.Deaths.Contains(c.name)) { Live.Deaths.Add(c.name); Log(c.name + " was killed.", null); }
                }
                catch (Exception e) { KsaCore.Log.Warn("Crew check on a destroyed vessel failed: " + e.Message); }
                Log("Vessel destroyed.", null);
                Resolve(false);
            }
        }

        private void OnVesselChange(Vessel v)
        {
            // Switching to debris or another craft: keep flying only our vessel.
            if (Live != null && v != null && v.persistentId == Live.VesselId) _vessel = v;
        }

        /// <summary>Called by the recovery hook for the live vessel; also by destruction and delivery. Clears the live state.</summary>
        public static void Resolve(bool recovered) { Resolve(recovered, false); }

        /// <summary>The top of the home world's atmosphere, metres (70 km on stock Kerbin); 0 when unknown.</summary>
        private static double HomeSpaceAltitude()
        {
            try
            {
                CelestialBody home = FlightGlobals.GetHomeBody();
                return home != null && home.atmosphere ? home.atmosphereDepth : 0;
            }
            catch { return 0; }
        }

        public static void Resolve(bool recovered, bool delivered)
        {
            if (Live == null || KsaCore.Agency == null) return;
            if (!recovered) ScheduleReturn(); // recovery returns to the Space Center by itself
            CauseOfCrash();
            var result = new LiveFlightResult
            {
                Cause = Live.Cause,
                CauseDetail = Live.CauseDetail,
                Recovered = recovered,
                Delivered = delivered,
                Crashed = Live.Crashed,
                DirectorFlown = Live.DirectorFlown,
                MaxAltitude = Live.MaxAltitude,
                SpaceAltitude = HomeSpaceAltitude(),
                MaxSpeed = Live.MaxSpeed,
                SciencePoints = Live.SciencePoints,
                MaxDistanceFromHomeKm = Live.MaxDistanceFromHomeKm,
                SurfaceDays = Live.SurfaceSeconds / KsaCore.Clock.SecondsPerDay,
                DaysInSpace = Math.Max(0.02, (Planetarium.GetUniversalTime() - Live.LaunchUT) / KsaCore.Clock.SecondsPerDay)
            };
            result.Deaths.AddRange(Live.Deaths);
            foreach (string a in Live.Logged) result.Achievements.Add(a);
            if (recovered) result.Achievements.Add("Recover:-");
            AddPassengers(result);
            string pid = Live.ProjectId;
            string programId = Live.ProgramId;
            uint vesselId = Live.VesselId;
            Live = null;
            if (delivered && !string.IsNullOrEmpty(programId))
            {
                // Stations: after a docking the surviving vessel id may be the module's, so always rebind. Bases keep the first module.
                Program g = KsaCore.Agency.GetProgram(programId);
                Project proj = KsaCore.Agency.GetProject(pid);
                MissionType mt = proj != null ? KsaCore.Agency.Catalogs.Missions.Get(proj.MissionTypeId) : null;
                bool station = mt != null && mt.Profile == MissionProfile.Station;
                if (g != null && (station || g.VesselId.Length == 0)) KsaCore.Agency.BindProgramVessel(programId, vesselId.ToString());
            }
            KSA.Core.Projects.MissionReport r = KsaCore.Agency.ResolveLive(pid, result);
            if (r != null) KsaCore.Log.Info("Live flight resolved: " + r.ProjectName + " " + Agency.OutcomeText(r.Outcome));
            if (Instance != null && Instance._program != null) Instance._program.Disengage();
            if (delivered) ScreenMessages.PostScreenMessage("[KSA] " + (r != null ? r.ProjectName : "Module") + " delivered; the crew stays aboard.", 8f, ScreenMessageStyle.UPPER_CENTER);
        }

        // ------------------------------------------------------------------ KSA-68: AI Director automation

        private float _goAt = -1f;
        private float _returnAt = -1f;

        /// <summary>Hands-off flying under the AI Director: count down and give the go on the pad; after a flight that
        /// ended without a recovery (a delivered module, a loss, a stranding), save and go back to the Space Center.</summary>
        private void DirectorAutomation()
        {
            Agency agency = KsaCore.Agency;
            if (agency == null || !agency.DirectorOn) { _goAt = -1f; _returnAt = -1f; return; }
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_holdForLaunch && Live != null)
            {
                if (_goAt < 0f) _goAt = now + Math.Max(1, agency.Settings.Director.GoSeconds);
                if (now >= _goAt) { _goAt = -1f; Log("The AI Director gave the go.", null); GoForLaunch(); }
            }
            else _goAt = -1f;
            if (_returnAt > 0f && Live == null && now >= _returnAt && HighLogic.LoadedScene == GameScenes.FLIGHT)
            {
                _returnAt = -1f;
                KsaCore.Log.Info("AI Director: flight over, returning to the Space Center");
                try { GamePersistence.SaveGame("persistent", HighLogic.SaveFolder, SaveMode.OVERWRITE); } catch (Exception e) { KsaCore.Log.Warn("Save before returning failed: " + e.Message); }
                HighLogic.LoadScene(GameScenes.SPACECENTER);
            }
        }

        private static void ScheduleReturn()
        {
            if (Instance == null || KsaCore.Agency == null || !KsaCore.Agency.DirectorOn) return;
            Instance._returnAt = UnityEngine.Time.realtimeSinceStartup + Math.Max(3, KsaCore.Agency.Settings.Director.ReturnSeconds);
            ScreenMessages.PostScreenMessage("[KSA] AI Director: back to the Space Center in " + KsaCore.Agency.Settings.Director.ReturnSeconds + " s.", 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>KSA-65: anyone aboard at the end who is not the project crew (a rescued crew) rides home as a passenger.</summary>
        private static void AddPassengers(LiveFlightResult result)
        {
            var names = new HashSet<string>(RescueTransferLeg.Aboard, StringComparer.OrdinalIgnoreCase);
            Vessel v = Live != null ? FindVessel(Live.VesselId) : null;
            Project p = Live != null && KsaCore.Agency != null ? KsaCore.Agency.GetProject(Live.ProjectId) : null;
            if (v != null) foreach (ProtoCrewMember c in v.GetVesselCrew()) if (c != null) names.Add(c.name);
            foreach (string n in names) if (p == null || !p.Crew.Contains(n)) result.Passengers.Add(n);
            RescueTransferLeg.Aboard.Clear();
        }

        /// <summary>
        /// KSA-65: the Director calls it: the crew is alive but cannot get home (out of fuel in a strange orbit, a lander
        /// that will not lift off). The project ends, the crew stays aboard the vessel, and a rescue can be launched.
        /// </summary>
        public void DeclareStranded()
        {
            if (Live == null || _vessel == null || KsaCore.Agency == null) return;
            if (_program != null) _program.Disengage();
            bool down = LandingHelpers.IsDown(_vessel);
            var result = new LiveFlightResult
            {
                Stranded = true,
                StrandedBody = _vessel.mainBody != null ? _vessel.mainBody.bodyName : "Kerbin",
                StrandedOnSurface = down,
                StrandedVesselId = _vessel.persistentId.ToString(),
                Cause = Live.Cause,
                CauseDetail = Live.CauseDetail,
                Crashed = Live.Crashed,
                DirectorFlown = Live.DirectorFlown,
                MaxAltitude = Live.MaxAltitude,
                MaxSpeed = Live.MaxSpeed,
                MaxDistanceFromHomeKm = Live.MaxDistanceFromHomeKm,
                SurfaceDays = Live.SurfaceSeconds / KsaCore.Clock.SecondsPerDay,
                DaysInSpace = Math.Max(0.02, (Planetarium.GetUniversalTime() - Live.LaunchUT) / KsaCore.Clock.SecondsPerDay)
            };
            result.Deaths.AddRange(Live.Deaths);
            foreach (string a in Live.Logged) result.Achievements.Add(a);
            ScheduleReturn();
            string pid = Live.ProjectId;
            Live = null;
            RescueTransferLeg.Aboard.Clear();
            KSA.Core.Projects.MissionReport r = KsaCore.Agency.ResolveLive(pid, result);
            KsaCore.Log.Info("Live flight declared stranded: " + (r != null ? r.ProjectName : pid) + " at " + result.StrandedBody + (result.StrandedOnSurface ? " (surface)" : " (orbit)"));
            ScreenMessages.PostScreenMessage("[KSA] Crew stranded " + (result.StrandedOnSurface ? "on " : "in orbit of ") + result.StrandedBody + ". Launch a rescue from the Projects window.", 10f, ScreenMessageStyle.UPPER_CENTER);
        }

        /// <summary>The stranded button shows when the crew cannot be flying home on their own: away from Kerbin's surface, not on the pad.</summary>
        private bool CanDeclareStranded()
        {
            if (_vessel == null || _holdForLaunch) return false;
            bool homeSurface = _vessel.mainBody == FlightGlobals.GetHomeBody() && LandingHelpers.IsDown(_vessel);
            if (homeSurface) return false;
            bool settled = _vessel.situation == Vessel.Situations.ORBITING || LandingHelpers.IsDown(_vessel) || _vessel.situation == Vessel.Situations.ESCAPING;
            return settled && (_program == null || _program.Failed || !Live.AutopilotOn);
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
            if (_program != null)
            {
                // Re-plan from wherever the Director left us.
                Project p = KsaCore.Agency.GetProject(Live.ProjectId);
                if (p != null) BuildProgram(KsaCore.Agency, p, true);
                _haltNoted = false;
                _program.Engage();
            }
            KsaCore.Agency.SetDirectorFlown(Live.ProjectId, false);
        }

        private bool _retriedOnce;

        private void RetryProgram()
        {
            if (Live == null || KsaCore.Agency == null) return;
            Project pr = KsaCore.Agency.GetProject(Live.ProjectId);
            if (pr == null) return;
            BuildProgram(KsaCore.Agency, pr, true);
            _haltNoted = false; _rollNoted = false;
            _program.Engage();
            Log("Flight program restarted at " + _program.Phase + ".", null);
        }

        private bool _holdForLaunch;
        private int _padStage;

        /// <summary>The Director gives the go: the crew takes over from here.</summary>
        public void GoForLaunch()
        {
            if (!_holdForLaunch) return;
            _holdForLaunch = false;
            _inputGraceUntil = UnityEngine.Time.unscaledTime + 2f; // the staging key that gave the go is not a takeover
            Log("Go for launch from the Director.", null);
            if (Live != null && Live.AutopilotOn && _program != null) _program.Engage();
        }

        /// <summary>The flight toolbar's KSD button hides the Flight Director window (the crew keeps flying). Remembered for the session.</summary>
        public static bool HudHidden { get; set; }

        private void OnGUI()
        {
            if (_offering && _offerVessel != null && KsaCore.Agency != null)
            {
                // The pad offer needs an answer, so it shows even when the window is hidden.
                _hud = GUILayout.Window(GetInstanceID(), _hud, DrawOffer, "KSA Flight Director");
                KeepOnScreen();
                return;
            }
            if (HudHidden) return;
            if (Live == null || _vessel == null || KsaCore.Agency == null) return;
            _hud = GUILayout.Window(GetInstanceID(), _hud, DrawHud, "KSA Flight Director");
            KeepOnScreen();
        }

        /// <summary>Keeps the HUD inside the screen and left of the stock app toolbar, and remembers where it is.</summary>
        private void KeepOnScreen()
        {
            float maxX = Mathf.Max(0f, Screen.width - _hud.width - 46f);
            float maxY = Mathf.Max(0f, Screen.height - _hud.height);
            _hud.x = Mathf.Clamp(_hud.x, 0f, maxX);
            _hud.y = Mathf.Clamp(_hud.y, 0f, maxY);
            _hudPlace = _hud.position;
        }

        private void DrawHud(int id)
        {
            Project p = KsaCore.Agency.GetProject(Live.ProjectId);
            GUILayout.Label(p != null ? p.Name : Live.ProjectId);
            string phase = Live.AutopilotOn && _program != null ? _program.Phase + " (" + (_program.Index + 1) + "/" + _program.Legs.Count + ")" : "Manual";
            if (_holdForLaunch)
            {
                GUILayout.Label("<b>Holding on the pad.</b> The crew is ready.");
                if (_goAt > 0f) GUILayout.Label("AI Director: launching in " + Mathf.CeilToInt(_goAt - UnityEngine.Time.realtimeSinceStartup) + " s.");
                if (GUILayout.Button("LAUNCH", GUILayout.Height(36f))) GoForLaunch();
            }
            else GUILayout.Label("Leg: " + phase);
            GUILayout.Label(_status);
            if (_science != null) GUILayout.Label("Science aboard: " + Live.SciencePoints.ToString("0.#") + " (" + (_science != null ? _science.ExperimentsRun : 0) + " experiments run)");
            GUILayout.Label("Alt " + LiveFlightResolver.FormatKm(_vessel.altitude) + "   Ap " + LiveFlightResolver.FormatKm(_vessel.orbit != null ? _vessel.orbit.ApA : 0) + "   Pe " + LiveFlightResolver.FormatKm(_vessel.orbit != null ? _vessel.orbit.PeA : 0));
            GUILayout.Label("Speed " + Math.Round(_vessel.srfSpeed) + " m/s   Stage " + _vessel.currentStage + "   " + (_vessel.mainBody != null ? _vessel.mainBody.bodyName : "") + "   " + Stamp());
            GUILayout.BeginHorizontal();
            if (Live.ManualAllowed)
            {
                if (Live.AutopilotOn) { if (GUILayout.Button("Take control")) TakeControl(); }
                else if (GUILayout.Button("Hand back to crew")) HandBack();
            }
            else GUILayout.Label("Crew flying (manual override off in agency.cfg)");
            if (_program != null && _program.Failed && Live.AutopilotOn && GUILayout.Button("Retry program")) RetryProgram();
            GUILayout.EndHorizontal();
            if (Live != null && CanDeclareStranded() && GUILayout.Button("Crew stranded: call for a rescue")) DeclareStranded();
            GUI.DragWindow();
        }
    }
}
