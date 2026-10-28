using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Design;
using KSA.Core.Missions;
using KSA.Core.Parts;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>
    /// M4: AI engineering. The host supplies settings, a part catalog and a transport; the agency runs a
    /// DesignSession for each project in its Design phase and records the result on the project.
    /// Everything stays asynchronous: the session's callbacks arrive later on the host's main thread.
    /// </summary>
    public sealed partial class Agency
    {
        /// <summary>User-local AI settings (API key stays in memory only). Null or Off means template designs.</summary>
        public AiSettings Ai { get; set; }
        /// <summary>Call and spend caps, persisted with the save.</summary>
        public CostGuard Costs { get; } = new CostGuard();
        /// <summary>Parts the host offers the engineering team. Null means AI design is unavailable.</summary>
        public IPartCatalog Parts { get; set; }
        /// <summary>Host HTTP. Null means AI design is unavailable.</summary>
        public ILlmTransport Transport { get; set; }
        /// <summary>Delta-v and TWR targets per mission profile (dv_requirements.cfg).</summary>
        public MissionRequirements Requirements { get; set; } = new MissionRequirements();
        /// <summary>Whether DLC parts may be offered (the host sets it from what is installed).</summary>
        public bool AllowDlcParts { get; set; }
        /// <summary>Surface gravity of the launch site's body, m/s² (Kerbin by default).</summary>
        public double LaunchGravity { get; set; } = 9.81;

        private readonly HashSet<string> _designsInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>An AI design session started for the project.</summary>
        public event Action<Project> DesignStarted;
        /// <summary>A design session finished (approved, short, or failed). The record is on the project.</summary>
        public event Action<Project, DesignRecord> DesignFinished;
        /// <summary>The project entered Build with an approved AI plan; the host may assemble a craft file.</summary>
        public event Action<Project, DesignRecord> BuildRequested;

        public bool AiDesignAvailable
        {
            get { return Ai != null && Ai.AiEnabled && Transport != null && Parts != null && Parts.All.Count > 0; }
        }

        public bool DesignInProgress(string projectId) { return projectId != null && _designsInFlight.Contains(projectId); }

        private bool _designsResumed;

        /// <summary>
        /// After a load, any project still in Design with a Pending AI record lost its session with the old game instance:
        /// start a fresh session so the team finishes the job. With AI off, the template flies and the note says why.
        /// </summary>
        public void ResumeInterruptedDesigns(List<string> notices)
        {
            if (_designsResumed) return;
            _designsResumed = true;
            foreach (Project p in Projects)
            {
                if (p.Status != ProjectStatus.Design || p.Design == null || p.Design.Verdict != DesignVerdict.Pending || DesignInProgress(p.Id)) continue;
                if (StartDesign(p, notices)) { p.Notes.Add("Design session resumed after a reload."); Push(notices, p.Name + ": the engineering team resumed the design"); }
                else
                {
                    p.Design.Verdict = DesignVerdict.Failed;
                    p.Design.Error = "Design session interrupted by save/load and AI design is not available now; the template flies.";
                    p.Notes.Add(p.Design.Error);
                }
            }
        }

        public static PartJob JobFor(MissionType type)
        {
            if (type == null) return PartJob.LaunchVehicle;
            switch (type.Profile)
            {
                case MissionProfile.Station: return PartJob.StationModule;
                case MissionProfile.Base:
                    return string.Equals(type.Body, "Eve", StringComparison.OrdinalIgnoreCase) ? PartJob.CloudCityModule : PartJob.BaseModule;
                default: return PartJob.LaunchVehicle;
            }
        }

        /// <summary>Builds the request the prompt is written from. Public so the Design Review window can show it.</summary>
        public DesignRequest BuildDesignRequest(Project p)
        {
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            var req = new DesignRequest
            {
                Mission = type,
                Requirement = Requirements.For(type),
                LeadEngineerId = p.LeadEngineer,
                Budget = p.Budget,
                CrewSize = type != null ? type.CrewSize : p.Crew.Count,
                ProjectName = p.Name
            };
            foreach (string id in p.Engineers)
            {
                CrewProfile e = Crew.Get(id);
                if (e == null) continue;
                req.Engineers.Add(e);
                req.RankNames[e.Id] = Careers.RankName(e.Track, e.RankId);
            }
            if (Parts != null) req.AllowedParts.AddRange(PartFilter.ForJob(Parts, JobFor(type), AllowDlcParts));
            req.Lessons.AddRange(Lessons(type, 8));
            req.PriorDesigns = MissionsCompleted(type.Id);
            return req;
        }

        /// <summary>Post-mortem lines from earlier flights of the same body/profile, oldest first.</summary>
        public List<string> Lessons(MissionType type, int max)
        {
            var lessons = new List<string>();
            for (int i = 0; i < Reports.Count && lessons.Count < max; i++)
            {
                MissionReport r = Reports[i];
                MissionType rt = Catalogs.Missions.Get(r.MissionTypeId);
                if (type != null && rt != null && !string.Equals(rt.Body, type.Body, StringComparison.OrdinalIgnoreCase)) continue;
                if (r.Outcome == MissionOutcome.Success) continue;
                string summary = r.ProjectName + " (" + OutcomeText(r.Outcome) + ")";
                if (!string.IsNullOrEmpty(r.Lesson)) summary += ": " + r.Lesson;
                else if (r.Lines.Count > 0) summary += ": " + r.Lines[r.Lines.Count - 1];
                lessons.Add(summary);
            }
            return lessons;
        }

        /// <summary>Starts a session for a project in Design if AI is on and the cost guard allows. Returns true if started.</summary>
        public bool StartDesign(Project p, List<string> notices)
        {
            if (p == null || p.Status != ProjectStatus.Design || _designsInFlight.Contains(p.Id)) return false;
            if (!AiDesignAvailable) return false;
            LlmEndpoint endpoint = Ai.EndpointFor(AiTask.Design);
            if (endpoint == null) return false;
            string why;
            if (!Costs.Allows(Ai, endpoint.IsLocal, CurrentDay, out why))
            {
                p.Notes.Add("AI design skipped: " + why + " Using the template.");
                Push(notices, p.Name + ": " + why + " Template design used.");
                return false;
            }

            DesignRequest request = BuildDesignRequest(p);
            if (request.AllowedParts.Count == 0) { p.Notes.Add("AI design skipped: no parts available for this job."); return false; }
            CrewProfile lead = Crew.Get(p.LeadEngineer);
            int passes = Math.Min(5, PromptBuilder.RevisionPasses(lead, RankRung(lead), false) + Personality.DesignPasses(lead));

            var session = new DesignSession(new LlmClient(Transport, Log), endpoint, Parts, Log, LaunchGravity);
            _designsInFlight.Add(p.Id);
            p.Design = new DesignRecord { Source = DesignSource.Ai, Verdict = DesignVerdict.Pending, PassesAllowed = passes, Model = endpoint.Model };
            p.Notes.Add("Engineering team (" + (lead != null ? lead.Name : "no lead") + ") requested an AI design via " + endpoint.Provider + " / " + endpoint.Model + ", up to " + passes + " pass(es).");
            Log.Info("Design session started for " + p.Name + " (" + passes + " passes, " + request.AllowedParts.Count + " parts offered)");
            Action<Project> started = DesignStarted;
            if (started != null) started(p);

            string projectId = p.Id;
            session.PassCompleted += record =>
            {
                Project live = GetProject(projectId);
                if (live != null) live.Design = record;
                if (!endpoint.IsLocal) Costs.Record(CurrentDay, EstimateCost(record));
            };
            session.Run(request, passes, record => FinishDesign(projectId, record));
            return true;
        }

        private double EstimateCost(DesignRecord record)
        {
            // Rough: ~6k prompt tokens + ~1k reply per pass at the model's listed rate, else a conservative cent.
            double rate = 0.001;
            if (Ai != null && ModelRates != null)
            {
                double r;
                if (ModelRates.TryGetValue(record.Model ?? string.Empty, out r)) rate = r;
            }
            return 7.0 * rate;
        }

        /// <summary>USD per 1000 tokens by model id, from ai_models.cfg (host fills it).</summary>
        public Dictionary<string, double> ModelRates { get; set; }

        private void FinishDesign(string projectId, DesignRecord record)
        {
            _designsInFlight.Remove(projectId);
            Project p = GetProject(projectId);
            if (p == null) return;
            if (!p.IsActive) { p.Design = record; return; }
            Template template = Catalogs.Templates.Get(p.TemplateId);
            switch (record.Verdict)
            {
                case DesignVerdict.Approved:
                    p.Notes.Add("AI design '" + record.DesignName + "' approved after " + record.PassesUsed + " pass(es): " + record.TotalDeltaV + " m/s, TWR " + record.LiftoffTwr + ", " + record.PartCount + " parts.");
                    break;
                case DesignVerdict.Short:
                    p.Notes.Add("AI design '" + record.DesignName + "' fell short after " + record.PassesUsed + " pass(es) (" + record.TotalDeltaV + " of " + record.RequiredDeltaV + " m/s). Flying the template " + (template != null ? template.Name : p.TemplateId) + " instead.");
                    break;
                default:
                    p.Notes.Add("AI design failed: " + record.Error + " Flying the template " + (template != null ? template.Name : p.TemplateId) + ".");
                    break;
            }
            p.Design = record;
            Log.Info("Design session for " + p.Name + " finished: " + record.Verdict);
            Action<Project, DesignRecord> finished = DesignFinished;
            if (finished != null) finished(p, record);
            RaiseProjectChanged(p);
            // Let the pipeline move on if the design clock has already run out.
            var notices = new List<string>();
            TickProjects(CurrentDay, notices);
            foreach (string n in notices) Push(null, n);
        }

        /// <summary>Lets the host or tests attach a finished record directly (also used when loading saves).</summary>
        public void SetDesign(string projectId, DesignRecord record)
        {
            Project p = GetProject(projectId);
            if (p == null) return;
            _designsInFlight.Remove(projectId);
            p.Design = record;
            RaiseProjectChanged(p);
        }

        /// <summary>The Director asks for another attempt (Design or Build phase only). Returns a message.</summary>
        public bool RequestRedesign(string projectId, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status != ProjectStatus.Design && p.Status != ProjectStatus.Build) { message = "Too late to redesign " + p.Name + "."; return false; }
            if (_designsInFlight.Contains(p.Id)) { message = "The team is still working on " + p.Name + "."; return false; }
            if (!AiDesignAvailable) { message = "AI design is off. Set it up in AI Setup."; return false; }
            if (p.Status == ProjectStatus.Build)
            {
                p.Status = ProjectStatus.Design;
                p.PhaseStartDay = CurrentDay;
                p.Notes.Add("Sent back to the drawing board on day " + CurrentDay + ".");
            }
            var notices = new List<string>();
            bool ok = StartDesign(p, notices);
            message = ok ? "The engineering team is revising " + p.Name + "." : (notices.Count > 0 ? notices[0] : "Could not start a design session.");
            return ok;
        }

        /// <summary>Design quality for the simulator: the AI analysis when approved, else the template.</summary>
        internal double? DesignOverride(Project p)
        {
            if (p.Design == null || !p.Design.IsAi) return null;
            if (p.Design.Verdict == DesignVerdict.Approved) return p.Design.DesignScore;
            return null; // fell back to the template
        }

        private void OnEnteredBuild(Project p)
        {
            if (p.Design != null && p.Design.IsAi && p.Design.Verdict == DesignVerdict.Approved)
            {
                Action<Project, DesignRecord> build = BuildRequested;
                if (build != null) build(p, p.Design);
            }
        }
    }
}
