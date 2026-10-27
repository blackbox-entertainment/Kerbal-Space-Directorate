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
        public bool ManualOverrideAllowed { get; set; }

        public LaunchSpec() { CraftPath = string.Empty; TemplateCraft = string.Empty; }
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
            Func<MissionType, string> check = LiveFlightCheck;
            string why = check != null ? check(type) : null;
            return why;
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
                ManualOverrideAllowed = Settings.ManualOverride
            };
            foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null) spec.CrewNames.Add(c.Name); }

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
            input.DesignOverride = DesignOverride(p);

            MissionReport r = LiveFlightResolver.Resolve(p, type, p.Crew, result, CurrentDay, MissionSimulator.DesignScore(input), MissionSimulator.CrewScore(input));
            var notices = new List<string>();
            ApplyReport(p, r, notices);
            StartPostMortem(p, r, type);
            return r;
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
