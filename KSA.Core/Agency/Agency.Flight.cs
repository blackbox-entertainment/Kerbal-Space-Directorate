using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Flight;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>What the host needs to put a live flight on the pad.</summary>
    public sealed class LaunchSpec
    {
        public Project Project { get; set; }
        public MissionType Mission { get; set; }
        /// <summary>Absolute craft path from an AI design, or empty.</summary>
        public string CraftPath { get; set; }
        /// <summary>Template craft reference ("stock:Kerbal 1" or a GameData-relative path) when there is no AI craft.</summary>
        public string TemplateCraft { get; set; }
        public List<string> CrewNames { get; } = new List<string>();
        public double TargetOrbitAltitude { get; set; }
        /// <summary>Orbit altitude above the mission body for capture and surface ascent (agency.cfg liveTargetOrbitAltitude).</summary>
        public double BodyOrbitAltitude { get; set; }
        public double ReentryPeriapsis { get; set; }
        /// <summary>Station modules: host vessel id of the station to dock with, else empty.</summary>
        public string DockTargetVesselId { get; set; }
        /// <summary>Base and landing missions: accepted site slope (tan).</summary>
        public double MaxSiteSlope { get; set; }
        public string ProgramId { get; set; }
        public bool ManualOverrideAllowed { get; set; }
        /// <summary>The crew reduced to flight numbers: precision, margins, mistake chance (decision 21).</summary>
        public CrewFlightProfile FlightCrew { get; set; }

        public LaunchSpec() { CraftPath = string.Empty; TemplateCraft = string.Empty; DockTargetVesselId = string.Empty; ProgramId = string.Empty; MaxSiteSlope = 0.3; }
    }

    /// <summary>
    /// M5: live flights. The host launches a Ready project on request, logs what happens through
    /// <see cref="LogFlightEvent"/>, and closes it with <see cref="ResolveLive"/>. The agency never waits on the game.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>Host check: null when the mission can be flown live right now, else the reason it cannot.</summary>
        public Func<MissionType, string> LiveFlightCheck { get; set; }

        /// <summary>The host should load the craft and start the flight. Raised by <see cref="LaunchLive"/>.</summary>
        public event Action<LaunchSpec> LiveLaunchRequested;
        /// <summary>A report's text changed after the fact (post-mortem arrived).</summary>
        public event Action<MissionReport> ReportUpdated;

        /// <summary>Why the project cannot fly live, or null if it can.</summary>
        public string LiveBlocker(Project p)
        {
            if (p == null) return "No such project.";
            if (!LiveFlightAvailable) return "Live flight is not available.";
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            if (type == null) return "Unknown mission type.";
            if (!ProfileInScope(type)) return "Live flight for " + type.Name + " is outside the scope set in agency.cfg (liveFlightScope = " + Settings.LiveFlightScope + ").";
            if (type.Floating) return type.Name + " floats in the clouds; the flight director has no airship program, so it is simulated.";
            if (type.IsRescue)
            {
                Stranding s = GetStranding(p.RescueId);
                if (s == null) return "Nobody to rescue.";
                if (s.OnSurface) return "Surface rescues are simulated: the flight director cannot aim a landing at a particular spot yet.";
                if (s.VesselId.Length == 0) return "The stranded crew were not flown live, so there is no vessel to meet; this rescue is simulated.";
            }
            if (type.Profile == MissionProfile.Station && !string.Equals(type.ModuleRole, "core", StringComparison.OrdinalIgnoreCase))
            {
                Program g = p.ProgramId.Length > 0 ? GetProgram(p.ProgramId) : null;
                if (g == null || g.VesselId.Length == 0) return "The station core was not flown live, so there is nothing in orbit to dock with; this module is simulated.";
            }
            string supplies = LifeSupportBlocker(p, type);
            if (supplies != null) return supplies;
            Func<MissionType, string> check = LiveFlightCheck;
            string why = check != null ? check(type) : null;
            return why;
        }

        /// <summary>
        /// KSA-67: with life support a live crew eats real food. The craft must carry food and water for the whole
        /// mission plus a margin, or the flight is simulated. Null when it does (or nothing is known about the craft).
        /// </summary>
        public string LifeSupportBlocker(Project p, MissionType type)
        {
            if (!LifeSupportOn || p == null || type == null) return null;
            Func<Project, ModuleHardware> check = ModuleHardwareCheck;
            ModuleHardware h = null;
            try { h = check != null ? check(p) : null; }
            catch (Exception e) { Log.Warn("Supply check failed for " + p.Name + ": " + e.Message); }
            if (h == null) return null;
            int crew = Math.Max(1, p.Crew.Count > 0 ? p.Crew.Count : type.CrewSize);
            double days = LifeSupport.SupplyDays(h.Food, h.Water, crew);
            int need = type.DurationDays + Math.Max(0, LifeSupport.MarginDays);
            if (days >= need) return null;
            return "Life support (" + LifeSupportMod + "): the craft carries food and water for " + Math.Floor(days).ToString("0") + " days for " + crew + " crew, and "
                + type.Name + " takes " + type.DurationDays + " days (+" + LifeSupport.MarginDays + " spare), so it is simulated. A design with supply containers can fly it live.";
        }

        private bool ProfileInScope(MissionType type)
        {
            string scope = Settings.LiveFlightScope ?? "All";
            bool home = string.Equals(type.Body, "Kerbin", StringComparison.OrdinalIgnoreCase);
            bool moon = string.Equals(type.Body, "Mun", StringComparison.OrdinalIgnoreCase) || string.Equals(type.Body, "Minmus", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(scope, "Kerbin", StringComparison.OrdinalIgnoreCase)) return home && type.Profile != MissionProfile.Station;
            if (string.Equals(scope, "Moons", StringComparison.OrdinalIgnoreCase)) return home || moon;
            return true;
        }

        /// <summary>The host tells us which vessel is the program's station or base, so later modules can dock or land beside it.</summary>
        public void BindProgramVessel(string programId, string vesselId)
        {
            Program g = GetProgram(programId);
            if (g == null) return;
            g.VesselId = vesselId ?? string.Empty;
            g.Notes.Add("Vessel " + g.VesselId + " is on station.");
        }

        /// <summary>Starts a live flight for a Ready project: marks it in flight and asks the host to launch.</summary>
        public bool LaunchLive(string projectId, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status != ProjectStatus.Ready) { message = p.Name + " is not ready to launch."; return false; }
            string blocker = LiveBlocker(p);
            if (blocker != null) { message = blocker; return false; }
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            Template template = Catalogs.Templates.Get(p.TemplateId);

            var spec = new LaunchSpec
            {
                Project = p,
                Mission = type,
                CraftPath = p.Design != null && p.Design.Verdict == Design.DesignVerdict.Approved ? p.Design.CraftPath : string.Empty,
                TemplateCraft = template != null ? template.Craft : string.Empty,
                TargetOrbitAltitude = Settings.LiveOrbitAltitude,
                BodyOrbitAltitude = Settings.LiveTargetOrbitAltitude,
                ReentryPeriapsis = Settings.LiveReentryPeriapsis,
                ProgramId = p.ProgramId,
                FlightCrew = FlightProfileFor(p.Id),
                ManualOverrideAllowed = Settings.ManualOverride || (p.Design != null && p.Design.Source == Design.DesignSource.Player) // the Director may always take over a rocket they built (2026-10-28)
            };
            foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null) spec.CrewNames.Add(c.Name); }
            Program program = p.ProgramId.Length > 0 ? GetProgram(p.ProgramId) : null;
            if (program != null) { spec.DockTargetVesselId = program.VesselId; spec.MaxSiteSlope = Math.Max(0.1, Math.Min(0.6, program.SiteSlope + 0.15)); }
            Stranding rescue = p.RescueId.Length > 0 ? GetStranding(p.RescueId) : null;
            if (rescue != null) spec.DockTargetVesselId = rescue.VesselId; // KSA-65: meet the stranded vessel

            p.Mode = MissionMode.WatchLive;
            var notices = new List<string>();
            Launch(p, type, CurrentDay, notices);
            p.LaunchUT = Clock.UniversalTime;
            p.FlightLog.Clear();
            p.FlightAchievements.Clear();
            p.DirectorFlown = false;
            Action<LaunchSpec> handler = LiveLaunchRequested;
            if (handler != null) handler(spec);
            message = p.Name + " is on the pad. Watch from Mission Control.";
            return true;
        }

        /// <summary>The flying crew of a project, reduced to the numbers the flight legs use (decision 21).</summary>
        public CrewFlightProfile FlightProfileFor(string projectId)
        {
            Project p = GetProject(projectId);
            if (p == null) return CrewFlightProfile.Neutral();
            var crew = new List<CrewProfile>();
            foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null) crew.Add(c); }
            return CrewFlightProfile.Build(crew, Settings.FlightCrew, Settings.Personality);
        }

        /// <summary>
        /// A judgement call during a live flight. The trait rule always answers. With crewDecisions = Local (or Any,
        /// which also allows the API key) and a model configured, the pilot is asked in character first; the rule
        /// answers after 20 s of silence. The pilot's line goes into the flight log either way.
        /// </summary>
        public void AskCrew(string projectId, DecisionRequest request, Action<CrewDecision> done)
        {
            Project p = GetProject(projectId);
            CrewFlightProfile profile = FlightProfileFor(projectId);
            CrewProfile pilot = p != null && profile.PilotId.Length > 0 ? Crew.Get(profile.PilotId) : null;
            Action<CrewDecision> record = d =>
            {
                if (p != null && d.Line.Length > 0) LogFlightEvent(p.Id, request.Situation + " " + d.Line);
                done(d);
            };
            string mode = Settings.CrewDecisions ?? "Local";
            if (string.Equals(mode, "Off", StringComparison.OrdinalIgnoreCase)) { record(new CrewDecision { Choice = DecisionChoice.Proceed, Source = "Off" }); return; }
            LlmEndpoint endpoint = null;
            if (Ai != null && Transport != null && !string.Equals(mode, "Rule", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = Ai.EndpointFor(AiTask.Narrative);
                if (endpoint != null && !endpoint.IsLocal && !string.Equals(mode, "Any", StringComparison.OrdinalIgnoreCase)) endpoint = null;
                string why;
                if (endpoint != null && !endpoint.IsLocal && !Costs.Allows(Ai, false, CurrentDay, out why)) endpoint = null;
            }
            if (endpoint == null) { record(CrewDecider.Rule(profile, pilot, request, Rng)); return; }
            if (!endpoint.IsLocal) Costs.Record(CurrentDay, 0.001);
            CrewDecider.Ask(new LlmClient(Transport, Log), endpoint, Clock, 20, pilot, profile, AgencyName, request, Rng, record);
        }

        /// <summary>
        /// A kerbal hired through the stock Astronaut Complex list: the host refunds the stock price and we charge the
        /// cheapest walk-in rate from recruiting.cfg instead, so every hire costs KSA money. Returns the fee charged.
        /// </summary>
        public double ChargeStockHire(string crewName)
        {
            double fee = double.MaxValue;
            foreach (KSA.Core.Recruiting.RecruitSource s in Catalogs.Recruiting.Sources) if (s.WalkInWeight > 0 && s.HireFee < fee) fee = s.HireFee;
            if (fee == double.MaxValue) fee = 5000;
            RefreshFunds();
            Ledger.Withdraw(fee, Finance.LedgerCategory.Hiring, "Hired " + crewName + " (walk-in via the Astronaut Complex)", CurrentDay, CurrentYear);
            Push(null, crewName + " hired as a walk-in for " + Money(fee) + " (no interview).");
            return fee;
        }

        /// <summary>
        /// The Director rolled a Ready project's craft out from the VAB by hand: start the mission on the vessel that is
        /// actually on the pad, with the agency crew who are actually aboard (the planned crew is replaced).
        /// </summary>
        public bool AdoptManualLaunch(string projectId, string vesselId, IList<string> crewNamesAboard, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status != ProjectStatus.Ready) { message = p.Name + " is not ready to launch."; return false; }
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            var aboard = new List<string>();
            if (crewNamesAboard != null) foreach (string n in crewNamesAboard) { CrewProfile c = Crew.Get(n); if (c != null && !c.Departed) aboard.Add(c.Id); }
            if (aboard.Count > 0) { p.Crew.Clear(); p.Crew.AddRange(aboard); }
            p.Mode = MissionMode.WatchLive;
            var notices = new List<string>();
            Launch(p, type, CurrentDay, notices);
            p.LaunchUT = Clock.UniversalTime;
            p.FlightLog.Clear();
            p.FlightAchievements.Clear();
            p.DirectorFlown = false;
            p.VesselId = vesselId ?? string.Empty;
            p.Notes.Add("Rolled out from the VAB by the Director on day " + CurrentDay + ".");
            RaiseProjectChanged(p);
            message = p.Name + " is on the pad; the crew has the vehicle.";
            return true;
        }

        /// <summary>
        /// KSA-62: the Director built a rocket in the hangar, seated agency kerbals and launched it. A hangar project
        /// is created on the spot (no design, build or budget) for the chosen mission type and the crew flies it, so
        /// the flight earns records, medals and a post-mortem like any other. Returns the project, or null with a reason.
        /// </summary>
        public Project AdoptHangarLaunch(string missionTypeId, string vesselId, string vesselName, IList<string> crewNamesAboard, out string message)
        {
            MissionType type = Catalogs.Missions.Get(missionTypeId);
            if (type == null) { message = "Unknown mission type."; return null; }
            var aboard = new List<string>();
            if (crewNamesAboard != null)
                foreach (string n in crewNamesAboard)
                {
                    CrewProfile c = Crew.Get(n);
                    if (c == null || c.Departed || c.Status == CrewStatus.Dead) continue;
                    if (c.IsFlying || c.ProjectId.Length > 0) { message = c.Name + " is already assigned to another project."; return null; }
                    aboard.Add(c.Id);
                }
            if (aboard.Count == 0) { message = "No agency kerbal is aboard."; return null; }
            List<Template> able = TemplatesFor(type);
            int day = CurrentDay;
            string title = string.IsNullOrEmpty(vesselName) ? type.Name : vesselName.Trim();
            var p = new Project
            {
                Id = "P" + NextProjectNumber++,
                Name = "Hangar flight: " + title,
                MissionTypeId = type.Id,
                TemplateId = able.Count > 0 ? able[0].Id : string.Empty, // only used if the flight ever has to be simulated
                Budget = 0,
                Mode = MissionMode.WatchLive,
                Status = ProjectStatus.Ready,
                CreatedDay = day,
                PhaseStartDay = day
            };
            p.Crew.AddRange(aboard);
            foreach (string id in aboard) Crew.Get(id).ProjectId = p.Id;
            p.Notes.Add("Built by the Director in the hangar (" + title + ") and flown as " + type.Name + ".");
            Projects.Add(p);
            var notices = new List<string>();
            Launch(p, type, day, notices);
            p.LaunchUT = Clock.UniversalTime;
            p.VesselId = vesselId ?? string.Empty;
            RaiseProjectChanged(p);
            CrewProfile pilot = Crew.Get(aboard[0]);
            message = (pilot != null ? pilot.Name : "The crew") + " has the vehicle: " + type.Name + ".";
            return p;
        }

        /// <summary>The host tells us which vessel flies the project (so recovery and crashes can be matched).</summary>
        public void BindVessel(string projectId, string vesselId)
        {
            Project p = GetProject(projectId);
            if (p == null) return;
            p.VesselId = vesselId ?? string.Empty;
            RaiseProjectChanged(p);
        }

        public Project ProjectForVessel(string vesselId)
        {
            if (string.IsNullOrEmpty(vesselId)) return null;
            foreach (Project p in Projects)
                if (p.Status == ProjectStatus.InFlight && p.Mode == MissionMode.WatchLive && string.Equals(p.VesselId, vesselId, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        /// <summary>Appends a timeline line (and optionally an achievement key such as "Orbit:Kerbin") to a live project.</summary>
        public void LogFlightEvent(string projectId, string text, string achievementKey = null)
        {
            Project p = GetProject(projectId);
            if (p == null) return;
            if (!string.IsNullOrEmpty(text)) { p.FlightLog.Add(text); Log.Info("Flight " + p.Name + ": " + text); }
            if (!string.IsNullOrEmpty(achievementKey) && !p.FlightAchievements.Contains(achievementKey)) p.FlightAchievements.Add(achievementKey);
        }

        /// <summary>The Director took (or returned) the controls. Taking control is remembered for the record.</summary>
        public void SetDirectorFlown(string projectId, bool flown)
        {
            Project p = GetProject(projectId);
            if (p == null) return;
            if (flown && !p.DirectorFlown) { p.DirectorFlown = true; LogFlightEvent(projectId, "The Director took manual control."); }
            else if (!flown && p.DirectorFlown) LogFlightEvent(projectId, "Controls handed back to the crew.");
        }

        /// <summary>Closes a live flight with the host's facts: applies the report and starts an AI post-mortem when available.</summary>
        public MissionReport ResolveLive(string projectId, LiveFlightResult result)
        {
            Project p = GetProject(projectId);
            if (p == null || p.Status != ProjectStatus.InFlight) return null;
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            if (result == null) result = new LiveFlightResult();
            foreach (string a in p.FlightAchievements) if (!result.Achievements.Contains(a)) result.Achievements.Add(a);
            if (result.Events.Count == 0) result.Events.AddRange(p.FlightLog);
            result.DirectorFlown = result.DirectorFlown || p.DirectorFlown;
            if (result.DaysInSpace <= 0 && p.LaunchUT >= 0 && Clock.SecondsPerDay > 0) result.DaysInSpace = Math.Max(0.05, (Clock.UniversalTime - p.LaunchUT) / Clock.SecondsPerDay);

            var input = new SimulationInput { Type = type, Template = Catalogs.Templates.Get(p.TemplateId), HasProgramManager = p.ProgramManager.Length > 0 };
            foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null) input.Crew.Add(c); }
            input.LeadEngineerRung = RankRung(Crew.Get(p.LeadEngineer));
            input.LeadDesignBonus = LeadDesignBonusFor(p);
            input.DesignOverride = DesignOverride(p);

            MissionReport r = LiveFlightResolver.Resolve(p, type, p.Crew, result, CurrentDay, MissionSimulator.DesignScore(input), MissionSimulator.CrewScore(input));
            var notices = new List<string>();
            ApplyReport(p, r, notices);
            if (result.Delivered)
            {
                // The crew stays aboard the station or base: still flying, not assignable, days in space keep accruing.
                foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null && !c.Departed) { c.InFlightSinceUT = Clock.UniversalTime; c.Status = CrewStatus.Assigned; } }
            }
            StartPostMortem(p, r, type);
            Action<MissionReport> reported = LiveMissionReported;
            if (reported != null) reported(r);
            return r;
        }

        /// <summary>A live flight was resolved; the host shows the KSA mission report (experience, Science) next to the stock recovery screen (2026-10-28).</summary>
        public event Action<MissionReport> LiveMissionReported;

        /// <summary>A crew member who stayed aboard a delivered station or base has come home: book the time, free them, no second mission credit.</summary>
        public void ReturnFromStation(string crewId)
        {
            CrewProfile c = Crew.Get(crewId);
            if (c == null) return;
            BookFlightTime(c);
            if (c.Status == CrewStatus.Assigned) c.Status = CrewStatus.Available;
            Push(null, c.Name + " has returned from station duty");
        }

        /// <summary>
        /// The launch never produced a vessel (bad craft file, scene failure): put the project back on the pad as Ready
        /// and release the crew, so it can be launched again instead of being written off.
        /// </summary>
        public bool ReturnToPad(string projectId, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status != ProjectStatus.InFlight) { message = p.Name + " is not in flight."; return false; }
            foreach (string id in p.Crew)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null) continue;
                MissionEntry m = c.Record.CurrentMission;
                if (m != null && m.MissionId == p.MissionId) c.Record.Missions.Remove(m);
                c.InFlightSinceUT = -1;
                if (c.Status == CrewStatus.Assigned) c.Status = CrewStatus.Available;
            }
            p.Status = ProjectStatus.Ready;
            p.MissionId = string.Empty;
            p.VesselId = string.Empty;
            p.LaunchUT = -1;
            p.FlightLog.Clear();
            p.FlightAchievements.Clear();
            p.Notes.Add("Launch scrubbed on day " + CurrentDay + "; back on the pad.");
            message = p.Name + " is back on the pad.";
            Push(null, message);
            RaiseProjectChanged(p);
            return true;
        }

        /// <summary>The flight never happened (vessel lost to a save reload, player gave up): resolve it as a simulation.</summary>
        public bool SimulateInstead(string projectId, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status != ProjectStatus.InFlight && p.Status != ProjectStatus.Ready) { message = p.Name + " is not in flight."; return false; }
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            if (type == null) { message = "Unknown mission type."; return false; }
            var notices = new List<string>();
            if (p.Status == ProjectStatus.Ready) Launch(p, type, CurrentDay, notices);
            p.Mode = MissionMode.Simulate;
            p.Notes.Add("Live flight abandoned on day " + CurrentDay + "; outcome simulated.");
            ResolveSimulated(p, type, CurrentDay, notices);
            message = notices.Count > 0 ? notices[notices.Count - 1] : p.Name + " resolved.";
            return true;
        }

        private void StartPostMortem(Project p, MissionReport r, MissionType type)
        {
            if (Ai == null || Transport == null) return;
            LlmEndpoint endpoint = Ai.EndpointFor(AiTask.PostMortem);
            if (endpoint == null) return;
            string why;
            if (!Costs.Allows(Ai, endpoint.IsLocal, CurrentDay, out why)) { Log.Info("Post-mortem skipped: " + why); return; }
            var crew = new List<CrewProfile>();
            foreach (string id in r.Crew) { CrewProfile c = Crew.Get(id); if (c != null) crew.Add(c); }
            var log = new List<string>(p.FlightLog);
            if (!endpoint.IsLocal) Costs.Record(CurrentDay, 0.002);
            PostMortemWriter.Run(new LlmClient(Transport, Log), endpoint, r, type, crew, log, ok =>
            {
                if (!ok) return;
                Log.Info("Post-mortem written for " + r.ProjectName);
                Action<MissionReport> handler = ReportUpdated;
                if (handler != null) handler(r);
            });
        }
    }
}
