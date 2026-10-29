using System;
using System.Collections.Generic;
using KSA.Core.Ai;
using KSA.Core.Design;
using KSA.Core.Finance;
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

        /// <summary>The design days are over but the AI engineering team has not answered yet: the project waits on it.</summary>
        public bool WaitingForAiDesign(Project p)
        {
            return p != null && p.Status == ProjectStatus.Design && CurrentDay >= p.PhaseStartDay + p.DesignDays && DesignInProgress(p.Id);
        }

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
            AddMustCarry(p, type, req);
            req.Lessons.AddRange(Lessons(type, 8));
            // A retry learns from the attempt before it.
            if (p.Design != null && p.Design.IsAi && p.Design.Verdict == DesignVerdict.Short)
                req.Lessons.Add("Your last design for this project, '" + p.Design.DesignName + "', had only " + p.Design.TotalDeltaV + " of the " + p.Design.RequiredDeltaV + " m/s needed: add stages, use more efficient engines or carry more fuel.");
            else if (p.Design != null && p.Design.IsAi && p.Design.Verdict == DesignVerdict.Failed && !string.IsNullOrEmpty(p.Design.Error))
                req.Lessons.Add("Your last design for this project was rejected: " + p.Design.Error);
            req.PriorDesigns = MissionsCompleted(type.Id);
            return req;
        }

        /// <summary>KSA-67: what the module or flight must carry, and the parts to carry it with.</summary>
        private void AddMustCarry(Project p, MissionType type, DesignRequest req)
        {
            if (type == null) return;
            if (string.Equals(p.ModuleRole, "industry", StringComparison.OrdinalIgnoreCase))
            {
                req.MustCarry.Add("at least one drill and one ore converter (Industry parts) and a power source (solar panels or an RTG): this module makes the base self-sufficient, and it stays on the surface.");
                if (LifeSupportOn) req.MustCarry.Add("at least one greenhouse (" + LifeSupportMod + " LifeSupport part) to grow food for the residents.");
            }
            AddScienceInstruments(req);
            AddHeatShield(type, req);
            AddAntenna(type, req);
            AddBattery(type, req);
            if (!LifeSupportOn) return;
            int crew = Math.Max(1, type.CrewSize);
            int days = type.DurationDays + Math.Max(0, LifeSupport.MarginDays);
            req.MustCarry.Add("food and water for " + crew + " crew for " + days + " days (" + LifeSupportMod + " is installed: a crew pod holds about 5 days; add supply containers, LifeSupport parts, for longer flights).");
            if (Parts == null) return;
            // Offer life support parts even to launch vehicles, whose part list leaves them out.
            bool have = req.AllowedParts.Exists(x => x.Kind == PartKind.LifeSupport);
            if (!have) foreach (PartInfo part in PartFilter.ForJob(Parts, PartJob.StationModule, AllowDlcParts)) if (part.Kind == PartKind.LifeSupport) req.AllowedParts.Add(part);
        }

        /// <summary>
        /// Every flight carries the small science instruments researched (thermometer, barometer, goo...), since live flights
        /// run them all and recovery pays for them (2026-10-28). Labs and other heavy science parts are left to stations.
        /// </summary>
        private void AddScienceInstruments(DesignRequest req)
        {
            if (Parts == null || !Settings.ExperimentsOnEveryFlight) return;
            var names = new List<string>();
            foreach (PartInfo part in Parts.All)
            {
                if (part.Kind != PartKind.Science || part.Mass > 0.1 || part.CrewCapacity > 0) continue;
                if (part.RequiresDlc && !AllowDlcParts) continue;
                if (names.Count >= 6) break; // keep the brief short with science mods installed
                if (!req.AllowedParts.Exists(x => x.Name == part.Name)) req.AllowedParts.Add(part);
                names.Add(part.Name);
            }
            if (names.Count > 0)
            {
                req.MustCarry.Add("one of each science instrument, as payload extras on the crew pod: " + string.Join(", ", names.ToArray()) + ". The crew runs them during the flight and the data is paid on recovery.");
                req.MustInclude.Add(new KeyValuePair<string, List<string>>("a science instrument", names));
            }
        }

        /// <summary>
        /// A crew coming home from orbit or beyond needs a heat shield under the pod, when one is researched (2026-10-28).
        /// Suborbital hops come in slowly enough without one. Checked by the design review, not only asked for.
        /// </summary>
        private void AddHeatShield(MissionType type, DesignRequest req)
        {
            if (Parts == null || type == null || type.CrewSize <= 0 || type.Profile == MissionProfile.Suborbital) return;
            if (type.ModuleRole.Length > 0 && type.Profile == MissionProfile.Base) return; // base modules stay on the surface
            var shields = new List<string>();
            foreach (PartInfo part in Parts.All)
            {
                if (part.Kind != PartKind.HeatShield || (part.RequiresDlc && !AllowDlcParts)) continue;
                if (part.Diameter > 0 && part.Diameter < 1.0) continue; // too small for a crew pod
                if (!req.AllowedParts.Exists(x => x.Name == part.Name)) req.AllowedParts.Add(part);
                shields.Add(part.Name);
            }
            if (shields.Count == 0) return;
            // Pods with a built-in ablator count as shielded.
            var shieldedPods = new List<string>();
            foreach (PartInfo part in req.AllowedParts) if (part.Kind == PartKind.Command && part.Tags.Contains("heatshielded")) shieldedPods.Add(part.Name);
            req.MustCarry.Add("a heat shield matching the crew pod's diameter, as a payload extra (it is mounted under the pod): " + string.Join(", ", shields.ToArray()) + ". The crew comes home from orbit or beyond and an unshielded pod can burn up."
                + (shieldedPods.Count > 0 ? " Not needed with a pod that has its own shield: " + string.Join(", ", shieldedPods.ToArray()) + "." : string.Empty));
            var accepted = new List<string>(shields);
            accepted.AddRange(shieldedPods);
            req.MustInclude.Add(new KeyValuePair<string, List<string>>("a heat shield for re-entry", accepted));
        }

        /// <summary>
        /// With Kerbalism, experiments record onto small drives (the Mk1 pod holds 0.5 Mb) and stop with "no storage space"
        /// when they fill. An external antenna sends the data home during the flight and frees the drive (2026-10-28).
        /// Pods' built-in antennas do not carry science there, so a crewed design must carry one. Checked by the review.
        /// </summary>
        private void AddAntenna(MissionType type, DesignRequest req)
        {
            if (Parts == null || type == null || type.CrewSize <= 0) return;
            if (!string.Equals(LifeSupportMod, "Kerbalism", StringComparison.OrdinalIgnoreCase)) return;
            var antennas = new List<string>();
            foreach (PartInfo part in Parts.All)
            {
                if (part.Kind != PartKind.Antenna || (part.RequiresDlc && !AllowDlcParts)) continue;
                if (!req.AllowedParts.Exists(x => x.Name == part.Name)) req.AllowedParts.Add(part);
                antennas.Add(part.Name);
            }
            if (antennas.Count == 0) return;
            req.MustCarry.Add("an antenna, as a payload extra on the crew pod: " + string.Join(", ", antennas.ToArray()) + ". Kerbalism stores experiment data on small drives; the antenna sends it home during the flight so the experiments do not stop for lack of storage space.");
            req.MustInclude.Add(new KeyValuePair<string, List<string>>("an antenna to send science home", antennas));
        }

        /// <summary>
        /// With Kerbalism the pod's own charge goes fast (life support draws on it) and experiments stop with "no Electricity"
        /// (2026-10-28). A crewed design carries a battery or other power part when one is researched. Checked by the review.
        /// </summary>
        private void AddBattery(MissionType type, DesignRequest req)
        {
            if (Parts == null || type == null || type.CrewSize <= 0) return;
            if (!string.Equals(LifeSupportMod, "Kerbalism", StringComparison.OrdinalIgnoreCase)) return;
            var power = new List<string>();
            foreach (PartInfo part in Parts.All)
            {
                if (part.Kind != PartKind.Power || (part.RequiresDlc && !AllowDlcParts) || part.Mass > 0.3) continue;
                if (!req.AllowedParts.Exists(x => x.Name == part.Name)) req.AllowedParts.Add(part);
                power.Add(part.Name);
                if (power.Count >= 6) break;
            }
            if (power.Count == 0) return;
            req.MustCarry.Add("electric power, as payload extras on the crew pod: at least one of " + string.Join(", ", power.ToArray()) + ". With " + LifeSupportMod + " the pod's own charge runs down in minutes and the experiments stop for lack of electricity; add more for longer flights.");
            req.MustInclude.Add(new KeyValuePair<string, List<string>>("a battery or power source", power));
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
            p.DesignAttempts++;
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
                    p.Notes.Add("AI design '" + record.DesignName + "' fell short after " + record.PassesUsed + " pass(es) (" + record.TotalDeltaV + " of " + record.RequiredDeltaV + " m/s). "
                        + (template != null ? "Flying the template " + template.Name + " instead." : "No fallback design: the team goes back to the drawing board."));
                    break;
                default:
                    p.Notes.Add("AI design failed: " + record.Error + " " + (template != null ? "Flying the template " + template.Name + "." : "No fallback design: the team goes back to the drawing board."));
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

        /// <summary>Design attempts the engineering team gets on a rocket with no fallback template before the project is called off.</summary>
        public const int MaxAiDesignAttempts = 3;

        /// <summary>True when the project has no fallback template: only an approved AI design can fly it.</summary>
        public bool NeedsAiDesign(Project p) { return p != null && Catalogs.Templates.Get(p.TemplateId) == null; }

        public static bool HasApprovedDesign(Project p) { return p != null && p.Design != null && p.Design.IsAi && p.Design.Verdict == DesignVerdict.Approved; }

        /// <summary>The design a project flies, for the UI: the approved AI design, else the template, else the team's work in progress.</summary>
        public string DesignNameOf(Project p)
        {
            if (p == null) return string.Empty;
            if (HasApprovedDesign(p)) return p.Design.DesignName + " (AI)";
            Template t = Catalogs.Templates.Get(p.TemplateId);
            return t != null ? t.Name : "engineering team design (AI)";
        }

        /// <summary>
        /// A rocket with no fallback template whose design came up short: the team tries again with the shortfall as a lesson,
        /// up to MaxAiDesignAttempts sessions. After that, or with AI unavailable, the project is called off and the whole
        /// budget comes back (nothing was built). Returns true when another attempt started.
        /// </summary>
        private bool RetryAiDesign(Project p, List<string> notices)
        {
            if (p.DesignAttempts < MaxAiDesignAttempts && StartDesign(p, notices))
            {
                p.Notes.Add("Design attempt " + p.DesignAttempts + " of " + MaxAiDesignAttempts + " started.");
                Push(notices, p.Name + ": the engineers try another design (attempt " + p.DesignAttempts + " of " + MaxAiDesignAttempts + ")");
                return true;
            }
            string why = p.DesignAttempts >= MaxAiDesignAttempts
                ? "the engineering team could not design a rocket that does the job with the parts researched (" + p.DesignAttempts + " attempts)"
                : "AI design is not available (AI Setup) and there is no fallback design";
            double refund = Math.Round(p.Budget);
            if (refund > 0) Ledger.Deposit(refund, LedgerCategory.Cancellation, "Called off " + p.Name, CurrentDay, CurrentYear);
            ReleaseStaff(p);
            p.Status = ProjectStatus.Cancelled;
            p.EndDay = CurrentDay;
            p.Notes.Add("Called off: " + why + ". " + Money(refund) + " returned.");
            Push(notices, p.Name + " called off: " + why + "; " + Money(refund) + " returned. Research more parts and try again.");
            RaiseProjectChanged(p);
            UpdateProgramFor(p, null);
            return false;
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
