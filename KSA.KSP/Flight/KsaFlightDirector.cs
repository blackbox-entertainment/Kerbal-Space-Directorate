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
            public string DockTargetVesselId = string.Empty;
            public string ProgramId = string.Empty;
            public HashSet<string> Logged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public double SciencePoints;
            public bool ScienceMission;
        }

        private MissionProgram _program;
        private ScienceRunner _science;
        private FlightContext _ctx;
        private Rect _hud = new Rect(Screen.width - 350, 120, 340, 210);
        private string _status = "Standing by.";
        private Vessel _vessel;
        private double _nextSample;
        private bool _haltNoted;

        private bool _adopted;
        private int _adoptTries;
        private float _nextAdoptTry;

        private void Start()
        {
            Instance = this;
            TryAdopt();
        }

        private float _waited;

        private void Update()
        {
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
                    ManualAllowed = pending.ManualOverrideAllowed,
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
                    ManualAllowed = agency.Settings.ManualOverride,
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
            if (!resuming) Log(_ctx.Crew.Describe(), null);
            uint dockId;
            if (uint.TryParse(Live.DockTargetVesselId, out dockId)) _ctx.DockTargetId = dockId;
            _program = MissionPlanner.Build(_ctx, type);
            Live.ScienceMission = type != null && type.IsScience;
            _science = Live.ScienceMission ? new ScienceRunner(_ctx) : null;
            if (resuming) MissionPlanner.Resume(_program, _ctx, key => Live.Logged.Contains(key));
            var names = new List<string>();
            foreach (IFlightLeg l in _program.Legs) names.Add(l.Name);
            KsaCore.Log.Info("Flight plan for " + project.Name + ": " + string.Join(" > ", names.ToArray()) + (resuming ? " (resuming at leg " + _program.NextLegNumber + ")" : string.Empty));
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
            if (_vessel.state == Vessel.State.DEAD) { Log("Vessel destroyed.", null); Resolve(false); return; }
            if (Live.ScienceMission && _science != null)
            {
                string sline = _science.Step();
                if (sline != null) Log(sline, null);
                Live.SciencePoints = _science.PointsCollected;
            }
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

        private void CheckOrbit()
        {
            if (_vessel == null || _vessel.orbit == null || _vessel.mainBody == null) return;
            if (_vessel.situation == Vessel.Situations.ORBITING && _vessel.orbit.PeA > _vessel.mainBody.atmosphereDepth)
                Log("Orbit achieved: " + LiveFlightResolver.FormatKm(_vessel.orbit.ApA) + " x " + LiveFlightResolver.FormatKm(_vessel.orbit.PeA) + " around " + _vessel.mainBody.bodyName + ".", "Orbit:" + _vessel.mainBody.bodyName);
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
                    else Log("Suborbital above " + body + ".", "Suborbit:" + body);
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

        public static void Resolve(bool recovered, bool delivered)
        {
            if (Live == null || KsaCore.Agency == null) return;
            var result = new LiveFlightResult
            {
                Recovered = recovered,
                Delivered = delivered,
                Crashed = Live.Crashed,
                DirectorFlown = Live.DirectorFlown,
                MaxAltitude = Live.MaxAltitude,
                MaxSpeed = Live.MaxSpeed,
                SciencePoints = Live.SciencePoints,
                DaysInSpace = Math.Max(0.02, (Planetarium.GetUniversalTime() - Live.LaunchUT) / KsaCore.Clock.SecondsPerDay)
            };
            result.Deaths.AddRange(Live.Deaths);
            foreach (string a in Live.Logged) result.Achievements.Add(a);
            if (recovered) result.Achievements.Add("Recover:-");
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
            _haltNoted = false;
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
            Log("Go for launch from the Director.", null);
            if (Live != null && Live.AutopilotOn && _program != null) _program.Engage();
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
            string phase = Live.AutopilotOn && _program != null ? _program.Phase + " (" + (_program.Index + 1) + "/" + _program.Legs.Count + ")" : "Manual";
            if (_holdForLaunch)
            {
                GUILayout.Label("<b>Holding on the pad.</b> The crew is ready.");
                if (GUILayout.Button("LAUNCH", GUILayout.Height(36f))) GoForLaunch();
            }
            else GUILayout.Label("Leg: " + phase);
            GUILayout.Label(_status);
            if (Live.ScienceMission) GUILayout.Label("Science aboard: " + Live.SciencePoints.ToString("0.#") + " (" + (_science != null ? _science.ExperimentsRun : 0) + " experiments run)");
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
            GUI.DragWindow();
        }
    }
}
