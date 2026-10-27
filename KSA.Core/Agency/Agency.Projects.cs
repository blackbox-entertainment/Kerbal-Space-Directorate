using System;
using System.Collections.Generic;
using KSA.Core.Careers;
using KSA.Core.Finance;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>Projects, programs, and simulated missions (Jira KSA-35, KSA-36, KSA-37).</summary>
    public sealed partial class Agency
    {
        public const int MaxReports = 100;

        public List<Project> Projects { get; } = new List<Project>();
        public List<Program> Programs { get; } = new List<Program>();
        public List<MissionReport> Reports { get; } = new List<MissionReport>();
        public int NextProjectNumber { get; set; } = 1;
        public int NextProgramNumber { get; set; } = 1;
        /// <summary>Mod identifiers the host reports as installed; gates templates that need mod parts.</summary>
        public HashSet<string> InstalledMods { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>True once Live flight exists (M5). Until then Watch Live projects fall back to Simulate.</summary>
        public bool LiveFlightAvailable { get; set; }

        /// <summary>A project reached a new status. The host may show it.</summary>
        public event Action<Project> ProjectChanged;
        /// <summary>A mission resolved.</summary>
        public event Action<MissionReport> MissionResolved;

        public Project GetProject(string id)
        {
            for (int i = 0; i < Projects.Count; i++)
                if (string.Equals(Projects[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Projects[i];
            return null;
        }

        public Program GetProgram(string id)
        {
            for (int i = 0; i < Programs.Count; i++)
                if (string.Equals(Programs[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Programs[i];
            return null;
        }

        // ------------------------------------------------------------------ staffing helpers

        /// <summary>Crew who can be put on a new project: active, alive, not flying, not already on a project.</summary>
        public List<CrewProfile> AvailableFor(CrewRole role)
        {
            var list = new List<CrewProfile>();
            foreach (CrewProfile p in Crew.Active)
            {
                if (p.Status == CrewStatus.Dead || p.IsFlying || p.ProjectId.Length > 0) continue;
                if (role != CrewRole.Unknown && p.Role != role) continue;
                list.Add(p);
            }
            list.Sort((a, b) => b.Stars != a.Stars ? b.Stars.CompareTo(a.Stars) : b.Morale.CompareTo(a.Morale));
            return list;
        }

        /// <summary>Best available crew for a mission: pilots first, then any role, by stars.</summary>
        public List<string> AutoCrew(MissionType type)
        {
            var ids = new List<string>();
            if (type == null) return ids;
            foreach (CrewProfile p in AvailableFor(CrewRole.Pilot)) { if (ids.Count >= type.CrewSize) break; if (p.Stars >= type.MinStars) ids.Add(p.Id); }
            if (ids.Count < type.CrewSize)
                foreach (CrewProfile p in AvailableFor(CrewRole.Unknown)) { if (ids.Count >= type.CrewSize) break; if (!ids.Contains(p.Id) && p.Role != CrewRole.Tourist) ids.Add(p.Id); }
            return ids;
        }

        public List<string> AutoEngineers(int count)
        {
            var ids = new List<string>();
            foreach (CrewProfile p in AvailableFor(CrewRole.Engineer)) { if (ids.Count >= count) break; ids.Add(p.Id); }
            return ids;
        }

        /// <summary>What a project would cost before it is created.</summary>
        public double ProjectCost(MissionType type, Template template)
        {
            if (type == null) return 0;
            double cost = type.BaseCost * (template != null ? template.CostMultiplier : 1.0);
            return Math.Round(cost);
        }

        /// <summary>Mode a new project starts with under the agency default (Hybrid: live only for an unachieved First).</summary>
        public MissionMode DefaultModeFor(MissionType type)
        {
            switch (Settings.DefaultMissionMode)
            {
                case DefaultMissionMode.WatchLive: return MissionMode.WatchLive;
                case DefaultMissionMode.Hybrid:
                    return type != null && type.IsFirst && !HasAgencyAchievement(type.PrimaryAchievement) ? MissionMode.WatchLive : MissionMode.Simulate;
                default: return MissionMode.Simulate;
            }
        }

        // ------------------------------------------------------------------ projects

        /// <summary>
        /// Creates a project. Pass null for crew or engineers to auto-staff. Charges the budget immediately.
        /// </summary>
        public Project CreateProject(string name, string missionTypeId, string templateId, IList<string> crewIds, IList<string> engineerIds,
                                     string programManagerId, MissionMode? mode, out string message)
        {
            MissionType type = Catalogs.Missions.Get(missionTypeId);
            if (type == null) { message = "Unknown mission type."; return null; }
            Template template = string.IsNullOrEmpty(templateId) ? Catalogs.Templates.Best(type.Id, InstalledMods) : Catalogs.Templates.Get(templateId);
            if (template == null) { message = "No design template can fly " + type.Name + " with the mods installed."; return null; }
            if (!template.Flies(type.Id) || !template.IsAvailable(InstalledMods)) { message = template.Name + " cannot fly " + type.Name + " here."; return null; }

            List<string> crew = crewIds != null ? new List<string>(crewIds) : AutoCrew(type);
            if (crew.Count != type.CrewSize) { message = type.Name + " needs " + type.CrewSize + " available crew (" + crew.Count + " chosen)."; return null; }
            foreach (string id in crew)
            {
                CrewProfile p = Crew.Get(id);
                if (p == null || p.Departed || p.Status == CrewStatus.Dead) { message = "Crew member " + id + " is not available."; return null; }
                if (p.IsFlying || p.ProjectId.Length > 0) { message = p.Name + " is already assigned."; return null; }
                if (p.Stars < type.MinStars) { message = p.Name + " lacks the experience for " + type.Name + " (needs " + type.MinStars + " stars)."; return null; }
            }
            List<string> engineers = engineerIds != null ? new List<string>(engineerIds) : AutoEngineers(2);
            foreach (string id in engineers)
            {
                CrewProfile p = Crew.Get(id);
                if (p == null || p.Departed || p.Role != CrewRole.Engineer) { message = "Engineer " + id + " is not available."; return null; }
                if (p.ProjectId.Length > 0 || crew.Contains(id)) { message = p.Name + " is already assigned."; return null; }
            }
            if (!string.IsNullOrEmpty(programManagerId) && Crew.Get(programManagerId) == null) { message = "Unknown program manager."; return null; }

            double cost = ProjectCost(type, template);
            if (!Ledger.CanAfford(cost)) { message = "Not enough funds for " + type.Name + " (" + Money(cost) + ")."; return null; }

            int day = CurrentDay;
            var project = new Project
            {
                Id = "P" + NextProjectNumber++,
                Name = string.IsNullOrEmpty(name) ? type.Name : name,
                MissionTypeId = type.Id,
                TemplateId = template.Id,
                Budget = cost,
                Mode = mode.HasValue ? mode.Value : DefaultModeFor(type),
                Status = ProjectStatus.Design,
                CreatedDay = day,
                PhaseStartDay = day,
                ProgramManager = programManagerId ?? string.Empty
            };
            project.Crew.AddRange(crew);
            project.Engineers.AddRange(engineers);
            project.LeadEngineer = PickLead(engineers);
            project.DesignDays = DesignDaysFor(type, project);
            project.BuildDays = BuildDaysFor(type, cost, project);
            foreach (string id in crew) Crew.Get(id).ProjectId = project.Id;
            foreach (string id in engineers) Crew.Get(id).ProjectId = project.Id;

            Ledger.Withdraw(cost, LedgerCategory.Other, "Project " + project.Name, day, CurrentYear);
            Projects.Add(project);
            message = "Project " + project.Name + " started: " + Money(cost) + ", design " + project.DesignDays + " days, build " + project.BuildDays + " days, mode " + project.Mode + ".";
            Push(null, message);
            RaiseProjectChanged(project);
            StartDesign(project, null);
            return project;
        }

        private string PickLead(List<string> engineers)
        {
            string lead = string.Empty;
            int bestRung = -1;
            foreach (string id in engineers)
            {
                CrewProfile p = Crew.Get(id);
                int rung = RankRung(p);
                if (rung > bestRung) { bestRung = rung; lead = id; }
            }
            return lead;
        }

        /// <summary>Rung on the crew member's track ladder (0 = entry), -1 if unranked.</summary>
        public int RankRung(CrewProfile p)
        {
            if (p == null) return -1;
            CareerTrackDefinition t = Careers.GetTrack(p.Track);
            return t != null ? t.IndexOf(p.RankId) : -1;
        }

        private int DesignDaysFor(MissionType type, Project project)
        {
            double days = Settings.DesignBaseDays + type.Difficulty * Settings.DesignBaseDays * 2;
            int rung = RankRung(Crew.Get(project.LeadEngineer));
            if (rung > 0) days *= 1 - Math.Min(0.4, 0.1 * rung);
            if (project.ProgramManager.Length > 0) days *= 0.9;
            return Math.Max(1, (int)Math.Round(days));
        }

        private int BuildDaysFor(MissionType type, double cost, Project project)
        {
            double days = Settings.BuildBaseDays + cost / 10000.0;
            if (project.ProgramManager.Length > 0) days *= 0.9;
            return Math.Max(1, (int)Math.Round(days));
        }

        /// <summary>Cancels a project that has not launched. Refunds part of the budget; crew morale dips.</summary>
        public bool CancelProject(string projectId, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (!p.IsActive) { message = p.Name + " is already finished."; return false; }
            if (p.Status == ProjectStatus.InFlight) { message = p.Name + " is in flight and cannot be cancelled."; return false; }
            double refund = Math.Round(p.Budget * Settings.CancelRefundFraction);
            if (refund > 0) Ledger.Deposit(refund, LedgerCategory.Cancellation, "Cancelled " + p.Name, CurrentDay, CurrentYear);
            foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null) { Catalogs.Morale.Apply(c, MoraleEvent.MissionFailure); } }
            ReleaseStaff(p);
            p.Status = ProjectStatus.Cancelled;
            p.EndDay = CurrentDay;
            message = p.Name + " cancelled; " + Money(refund) + " refunded.";
            Push(null, message);
            RaiseProjectChanged(p);
            UpdateProgramFor(p, null);
            return true;
        }

        /// <summary>Switches a project's mode before launch.</summary>
        public bool SetProjectMode(string projectId, MissionMode mode, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status == ProjectStatus.InFlight || !p.IsActive) { message = "Too late to change how " + p.Name + " flies."; return false; }
            p.Mode = mode;
            message = p.Name + " will " + (mode == MissionMode.WatchLive ? "be flown live." : "be simulated.");
            return true;
        }

        private void ReleaseStaff(Project p)
        {
            foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null && c.ProjectId == p.Id) c.ProjectId = string.Empty; }
            foreach (string id in p.Engineers) { CrewProfile c = Crew.Get(id); if (c != null && c.ProjectId == p.Id) c.ProjectId = string.Empty; }
        }

        // ------------------------------------------------------------------ pipeline

        private void TickProjects(int day, List<string> notices)
        {
            for (int i = 0; i < Projects.Count; i++)
            {
                Project p = Projects[i];
                if (!p.IsActive) continue;
                MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
                if (type == null) continue;
                int guard = 0;
                bool changed = true;
                while (changed && guard++ < 6)
                {
                    changed = false;
                    switch (p.Status)
                    {
                        case ProjectStatus.Design:
                            if (day >= p.PhaseStartDay + p.DesignDays && !DesignInProgress(p.Id))
                            {
                                p.Status = ProjectStatus.Build; p.PhaseStartDay = p.PhaseStartDay + p.DesignDays; changed = true;
                                string designName = p.Design != null && p.Design.IsAi && p.Design.Verdict == Design.DesignVerdict.Approved
                                    ? p.Design.DesignName + " (AI)"
                                    : Catalogs.Templates.NameOf(p.TemplateId);
                                p.Notes.Add("Design complete on day " + p.PhaseStartDay + " (" + designName + ").");
                                Push(notices, p.Name + ": design complete, build started");
                                OnEnteredBuild(p);
                                RaiseProjectChanged(p);
                            }
                            break;
                        case ProjectStatus.Build:
                            if (day >= p.PhaseStartDay + p.BuildDays)
                            {
                                p.Status = ProjectStatus.Ready; p.PhaseStartDay = p.PhaseStartDay + p.BuildDays; changed = true;
                                Push(notices, p.Name + ": vehicle ready");
                                RaiseProjectChanged(p);
                            }
                            break;
                        case ProjectStatus.Ready:
                            if (p.Mode == MissionMode.WatchLive && !LiveFlightAvailable)
                            {
                                p.Mode = MissionMode.Simulate;
                                p.Notes.Add("Watch Live is not available yet; flown as a simulation.");
                                Push(notices, p.Name + ": live flight unavailable, simulating instead");
                            }
                            if (p.Mode == MissionMode.Simulate)
                            {
                                Launch(p, type, p.PhaseStartDay, notices); // the day it became ready, so catch-up ticks resolve in sequence
                                changed = true;
                            }
                            break;
                        case ProjectStatus.InFlight:
                            if (p.Mode == MissionMode.Simulate && day >= p.LaunchDay + type.DurationDays)
                            {
                                ResolveSimulated(p, type, Math.Min(day, p.LaunchDay + type.DurationDays), notices);
                                changed = false;
                            }
                            break;
                    }
                }
            }
        }

        private void Launch(Project p, MissionType type, int day, List<string> notices)
        {
            p.MissionId = BeginMission(p.Crew, p.Name);
            p.Status = ProjectStatus.InFlight;
            p.LaunchDay = day;
            Push(notices, p.Name + " launched (" + (p.Mode == MissionMode.Simulate ? "simulated" : "live") + ", " + type.DurationDays + " days)");
            RaiseProjectChanged(p);
        }

        /// <summary>Launches a Ready project immediately instead of waiting for the next tick.</summary>
        public bool LaunchNow(string projectId, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status != ProjectStatus.Ready) { message = p.Name + " is not ready to launch."; return false; }
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            if (type == null) { message = "Unknown mission type."; return false; }
            if (p.Mode == MissionMode.WatchLive && !LiveFlightAvailable) { p.Mode = MissionMode.Simulate; p.Notes.Add("Watch Live is not available yet; flown as a simulation."); }
            var notices = new List<string>();
            Launch(p, type, CurrentDay, notices);
            message = notices.Count > 0 ? notices[0] : p.Name + " launched.";
            return true;
        }

        private void ResolveSimulated(Project p, MissionType type, int day, List<string> notices)
        {
            var input = new SimulationInput { Type = type, Template = Catalogs.Templates.Get(p.TemplateId), HasProgramManager = p.ProgramManager.Length > 0 };
            foreach (string id in p.Crew) { CrewProfile c = Crew.Get(id); if (c != null) input.Crew.Add(c); }
            input.LeadEngineerRung = RankRung(Crew.Get(p.LeadEngineer));
            input.DesignOverride = DesignOverride(p);
            Program program = p.ProgramId.Length > 0 ? GetProgram(p.ProgramId) : null;
            if (program != null && type.IsLanding) input.SiteSlope = program.SiteSlope;

            MissionReport r = MissionSimulator.Resolve(p, input, day, Rng);
            r.MissionId = p.MissionId;
            ApplyReport(p, r, notices);
        }

        /// <summary>Applies a mission report to the crew records, ledger-free. Used for simulated and (later) live missions.</summary>
        public void ApplyReport(Project p, MissionReport r, List<string> notices)
        {
            int day = r.Day;
            // Make sure the clock-based flight time matches the report: book a fixed duration instead.
            foreach (string id in r.Crew)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null) continue;
                c.InFlightSinceUT = -1;                 // don't double-book against the clock
                c.Record.DaysInSpace += r.DaysInSpace;
                foreach (IncidentKind k in r.Incidents) RecordIncident(id, k, k == IncidentKind.Crash ? "Vehicle lost on " + r.ProjectName : "Problems on " + r.ProjectName);
                if (r.Deaths.Contains(id))
                {
                    notices.AddRange(RecordDeath(id, "Killed on " + r.ProjectName));
                }
                else
                {
                    notices.AddRange(AddAchievements(id, r.Achievements));
                    notices.AddRange(EndMission(id, r.Outcome));
                }
            }
            p.Report = r;
            p.EndDay = day;
            p.Status = r.IsSuccess ? ProjectStatus.Complete : ProjectStatus.Failed;
            ReleaseStaff(p);
            Reports.Add(r);
            if (Reports.Count > MaxReports) Reports.RemoveRange(0, Reports.Count - MaxReports);
            Push(notices, p.Name + ": " + OutcomeText(r.Outcome));
            RaiseProjectChanged(p);
            Action<MissionReport> handler = MissionResolved;
            if (handler != null) handler(r);
            UpdateProgramFor(p, r);
        }

        public static string OutcomeText(MissionOutcome o)
        {
            switch (o)
            {
                case MissionOutcome.Success: return "success";
                case MissionOutcome.PartialSuccess: return "partial success";
                case MissionOutcome.Abort: return "aborted, crew safe";
                case MissionOutcome.LossOfVehicle: return "vehicle lost, crew safe";
                case MissionOutcome.LossOfCrew: return "crew lost";
                case MissionOutcome.InProgress: return "in progress";
                default: return o.ToString();
            }
        }

        private void RaiseProjectChanged(Project p)
        {
            Action<Project> handler = ProjectChanged;
            if (handler != null) handler(p);
        }

        // ------------------------------------------------------------------ programs

        /// <summary>Starts a program and its first module project (auto-staffed). Later modules start as earlier ones succeed.</summary>
        public Program CreateProgram(string name, string programTypeId, string body, string programManagerId, out string message)
        {
            ProgramType type = Catalogs.ProgramTypes.Get(programTypeId);
            if (type == null) { message = "Unknown program type."; return null; }
            if (type.Bodies.Count > 0 && !type.Bodies.Exists(b => string.Equals(b, body, StringComparison.OrdinalIgnoreCase)))
            {
                message = type.Name + " cannot be built at " + body + ". Choose from: " + string.Join(", ", type.Bodies.ToArray()) + ".";
                return null;
            }
            var program = new Program
            {
                Id = "G" + NextProgramNumber++,
                Name = string.IsNullOrEmpty(name) ? type.Name + " " + body : name,
                ProgramTypeId = type.Id,
                Body = body,
                ProgramManager = programManagerId ?? string.Empty,
                Status = ProgramStatus.Active,
                CreatedDay = CurrentDay
            };
            if (type.Kind != ProgramKind.OrbitalStation)
            {
                program.SiteSlope = Math.Round(Rng.NextDouble() * 0.5, 2);
                program.Site = SiteDescription(program.SiteSlope, body);
                program.Notes.Add("Site survey: " + program.Site);
            }
            Programs.Add(program);
            Push(null, "Program " + program.Name + " started (" + type.Modules.Count + " modules).");

            string why;
            Project first = StartNextModule(program, out why);
            message = first != null
                ? "Program " + program.Name + " started; first module " + first.Name + " is in design."
                : "Program " + program.Name + " created but the first module could not start: " + why;
            if (first == null) { program.Status = ProgramStatus.Stalled; program.Notes.Add("Stalled: " + why); }
            return program;
        }

        /// <summary>Starts the next module as an auto-staffed project. Returns null (with a reason) if it cannot.</summary>
        public Project StartNextModule(Program program, out string reason)
        {
            ProgramType type = Catalogs.ProgramTypes.Get(program.ProgramTypeId);
            if (type == null) { reason = "Unknown program type."; return null; }
            if (program.NextModuleIndex >= type.Modules.Count) { reason = "All modules started."; return null; }
            ModuleRecipe recipe = type.Modules[program.NextModuleIndex];
            string moduleName = program.Name + ": " + recipe.Role + " module";
            Project p = CreateProject(moduleName, recipe.MissionTypeId, null, null, null, program.ProgramManager, null, out reason);
            if (p == null) return null;
            p.ProgramId = program.Id;
            p.ModuleRole = recipe.Role;
            program.ModuleProjectIds.Add(p.Id);
            program.NextModuleIndex++;
            program.Status = ProgramStatus.Active;
            return p;
        }

        private void UpdateProgramFor(Project p, MissionReport r)
        {
            if (p.ProgramId.Length == 0) return;
            Program program = GetProgram(p.ProgramId);
            if (program == null || program.Status == ProgramStatus.Cancelled) return;
            ProgramType type = Catalogs.ProgramTypes.Get(program.ProgramTypeId);
            if (type == null) return;

            if (p.Status == ProjectStatus.Complete)
            {
                if (program.NextModuleIndex >= type.Modules.Count)
                {
                    program.Status = ProgramStatus.Complete;
                    Push(null, "Program " + program.Name + " complete.");
                    return;
                }
                string why;
                Project next = StartNextModule(program, out why);
                if (next == null)
                {
                    program.Status = ProgramStatus.Stalled;
                    program.Notes.Add("Stalled after " + p.ModuleRole + ": " + why);
                    Push(null, "Program " + program.Name + " stalled: " + why);
                }
            }
            else
            {
                // A failed or cancelled module leaves the program half-built; the Director retries from the Programs window.
                program.NextModuleIndex = Math.Max(0, program.NextModuleIndex - 1);
                program.ModuleProjectIds.Remove(p.Id);
                program.Status = ProgramStatus.Stalled;
                program.Notes.Add(p.ModuleRole + " module " + (r != null ? OutcomeText(r.Outcome) : "cancelled") + " on day " + CurrentDay + ".");
                Push(null, "Program " + program.Name + " stalled: " + p.ModuleRole + " module " + (r != null ? OutcomeText(r.Outcome) : "cancelled"));
            }
        }

        /// <summary>Retries a stalled program by starting its next module.</summary>
        public bool ResumeProgram(string programId, out string message)
        {
            Program program = GetProgram(programId);
            if (program == null) { message = "No such program."; return false; }
            if (program.Status == ProgramStatus.Complete || program.Status == ProgramStatus.Cancelled) { message = program.Name + " is finished."; return false; }
            foreach (string pid in program.ModuleProjectIds)
            {
                Project existing = GetProject(pid);
                if (existing != null && existing.IsActive) { message = program.Name + " already has a module in progress."; return false; }
            }
            Project next = StartNextModule(program, out message);
            if (next == null) return false;
            message = next.Name + " started.";
            return true;
        }

        public bool CancelProgram(string programId, out string message)
        {
            Program program = GetProgram(programId);
            if (program == null) { message = "No such program."; return false; }
            foreach (string pid in program.ModuleProjectIds)
            {
                Project mp = GetProject(pid);
                string ignored;
                if (mp != null && mp.IsActive && mp.Status != ProjectStatus.InFlight) CancelProject(pid, out ignored);
            }
            program.Status = ProgramStatus.Cancelled;
            message = "Program " + program.Name + " cancelled.";
            Push(null, message);
            return true;
        }

        private static string SiteDescription(double slope, string body)
        {
            string ground = slope < 0.15 ? "a flat plain" : slope < 0.3 ? "gently rolling ground" : slope < 0.45 ? "a sloping valley floor" : "rough, cratered highlands";
            return ground + " on " + body + " (slope " + (int)Math.Round(slope * 100) + "%)";
        }
    }
}
