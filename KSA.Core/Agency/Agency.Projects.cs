using System;
using System.Collections.Generic;
using KSA.Core.Careers;
using KSA.Core.Design;
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

        /// <summary>Farthest distance, surface days and longest mission on the crew member's record (KSA-60).</summary>
        private void UpdateExplorationRecord(CrewProfile c, MissionReport r)
        {
            MissionType type = Catalogs.Missions.Get(r.MissionTypeId);
            double far = r.FarthestKm > 0 ? r.FarthestKm : (r.IsSuccess || r.Outcome == MissionOutcome.PartialSuccess) && type != null ? BodyDistances.FromHomeKm(type.Body) : 0;
            if (far > c.Record.FarthestKm) c.Record.FarthestKm = far;
            double surface = r.SurfaceDays > 0 ? r.SurfaceDays : r.IsSuccess ? BodyDistances.SurfaceDaysFor(type) : 0;
            if (type != null && !string.Equals(type.Body, "Kerbin", StringComparison.OrdinalIgnoreCase)) c.Record.DaysOnOtherWorlds += surface;
            if (r.DaysInSpace > c.Record.LongestMissionDays) c.Record.LongestMissionDays = r.DaysInSpace;
        }

        /// <summary>How many projects of this mission type finished successfully (for the "done before" marks).</summary>
        public int MissionsCompleted(string missionTypeId)
        {
            int n = 0;
            foreach (Project p in Projects) if (p.Status == ProjectStatus.Complete && string.Equals(p.MissionTypeId, missionTypeId, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>How many programs of this type completed.</summary>
        public int ProgramsCompleted(string programTypeId)
        {
            int n = 0;
            foreach (Program g in Programs) if (g.Status == ProgramStatus.Complete && string.Equals(g.ProgramTypeId, programTypeId, StringComparison.OrdinalIgnoreCase)) n++;
            return n;
        }

        /// <summary>Crew who can be put on a new project: active, alive, not flying, not already on a project.</summary>
        public List<CrewProfile> AvailableFor(CrewRole role)
        {
            var list = new List<CrewProfile>();
            foreach (CrewProfile p in Crew.Active)
            {
                if (p.Status == CrewStatus.Dead || p.IsFlying || p.ProjectId.Length > 0 || p.InLab || IsDepartmentHead(p.Id)) continue;
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
            // Rotate the pilots (asked for 2026-10-28): whoever has flown least goes first, then whoever flew longest ago,
            // then the more experienced. Everyone gets flights and experience instead of one kerbal flying everything.
            var pilots = new List<CrewProfile>();
            foreach (CrewProfile p in AvailableFor(CrewRole.Pilot)) if (p.Stars >= type.MinStars) pilots.Add(p);
            pilots.Sort((x, y) =>
            {
                int c = x.Record.Missions.Count.CompareTo(y.Record.Missions.Count);
                if (c != 0) return c;
                c = LastFlightDay(x).CompareTo(LastFlightDay(y));
                if (c != 0) return c;
                return y.Stars.CompareTo(x.Stars);
            });
            foreach (CrewProfile p in pilots) { if (ids.Count >= type.CrewSize) break; ids.Add(p.Id); }
            if (ids.Count < type.CrewSize)
                foreach (CrewProfile p in AvailableFor(CrewRole.Unknown)) { if (ids.Count >= type.CrewSize) break; if (!ids.Contains(p.Id) && p.Role != CrewRole.Tourist) ids.Add(p.Id); }
            return ids;
        }

        private static int LastFlightDay(CrewProfile p)
        {
            int last = -1;
            foreach (MissionEntry m in p.Record.Missions) if (m.StartDay > last) last = m.StartDay;
            return last;
        }

        public List<string> AutoEngineers(int count, ICollection<string> exclude = null)
        {
            var ids = new List<string>();
            foreach (CrewProfile p in AvailableFor(CrewRole.Engineer)) { if (ids.Count >= count) break; if (exclude != null && exclude.Contains(p.Id)) continue; ids.Add(p.Id); }
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
                                     string programManagerId, MissionMode? mode, out string message, string moduleRole = null, string playerDesign = null)
        {
            MissionType type = Catalogs.Missions.Get(missionTypeId);
            if (type == null) { message = "Unknown mission type."; return null; }
            // The Director's own rocket (2026-10-28): it only has to fit the mission, whatever designs exist.
            PlayerDesign mine = null;
            if (!string.IsNullOrEmpty(playerDesign))
            {
                mine = FindPlayerDesign(playerDesign);
                if (mine == null) { message = "No saved design called '" + playerDesign + "'."; return null; }
                string unfit = mine.Blocker(type, Requirements.For(type));
                if (unfit != null) { message = mine.Name + " cannot fly " + type.Name + ": " + unfit + "."; return null; }
            }
            string why = mine != null ? null : MissionBlocker(type);
            if (why != null) { message = type.Name + " " + why + ". Earn Science (lab, missions) and unlock parts in R&D first."; return null; }
            List<Template> able = TemplatesFor(type, moduleRole);
            Template template;
            if (mine != null) template = null;
            else if (string.IsNullOrEmpty(templateId))
            {
                // No design picked: the first fallback design that can be built today, else (AI on) none at all and the
                // engineering team designs the rocket from scratch (2026-10-28: templates are the AI-off fallback, not a gate).
                template = able.Find(x => x.IsAvailable(InstalledMods) && TemplateBlocker(x) == null);
                if (template == null && !AiDesignAvailable && DesignableBlocker(type) != null) { message = "No rocket can be built for " + type.Name + " with the parts researched."; return null; }
            }
            else
            {
                template = Catalogs.Templates.Get(templateId);
                if (template == null) { message = "No design template can fly " + type.Name + " with the mods installed."; return null; }
                if (!able.Exists(x => x.Id == template.Id) || !template.IsAvailable(InstalledMods)) { message = template.Name + " cannot fly " + type.Name + " here."; return null; }
                string blocked = TemplateBlocker(template);
                if (blocked != null) { message = template.Name + " " + blocked + ". Put scientists in the lab or fly missions to earn Science, then unlock the parts in R&D."; return null; }
            }

            List<string> crew = crewIds != null ? new List<string>(crewIds) : AutoCrew(type);
            if (crew.Count != type.CrewSize) { message = type.Name + " needs " + type.CrewSize + " available crew (" + crew.Count + " chosen)."; return null; }
            foreach (string id in crew)
            {
                CrewProfile p = Crew.Get(id);
                if (p == null || p.Departed || p.Status == CrewStatus.Dead) { message = "Crew member " + id + " is not available."; return null; }
                if (p.IsFlying || p.ProjectId.Length > 0) { message = p.Name + " is already assigned."; return null; }
                if (p.Stars < type.MinStars) { message = p.Name + " lacks the experience for " + type.Name + " (needs " + type.MinStars + " stars)."; return null; }
            }
            List<string> engineers = engineerIds != null ? new List<string>(engineerIds) : AutoEngineers(2, crew); // an engineer flying it cannot also design it
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
                TemplateId = template != null ? template.Id : string.Empty,
                Budget = cost,
                Mode = mode.HasValue ? mode.Value : DefaultModeFor(type),
                Status = ProjectStatus.Design,
                CreatedDay = day,
                PhaseStartDay = day,
                ProgramManager = programManagerId ?? string.Empty,
                ModuleRole = moduleRole ?? string.Empty // set before the design starts, so the brief knows the role
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
            PublishProjectStart(project, type);
            if (mine != null)
            {
                project.Design = mine.ToRecord(Requirements.For(type));
                project.DesignDays = 1; // already designed: the engineers only check it over
                project.Notes.Add("Flying the Director's own design '" + mine.Name + "' (" + project.Design.TotalDeltaV + " m/s, TWR " + project.Design.LiftoffTwr + "). Take Control is always allowed on it.");
            }
            else
            {
                if (template == null) project.Notes.Add("No ready-made design: the engineering team designs this rocket from scratch.");
                if (!TryReuseDesign(project) && !StartDesign(project, null) && template == null)
                {
                    // No AI: the engineers draw it up by hand (2026-10-28).
                    string handWhy;
                    DesignRecord byHand = EngineersDesign(project, out handWhy);
                    if (byHand != null) { project.Design = byHand; project.Notes.Add("Designed by hand: '" + byHand.DesignName + "' (" + byHand.TotalDeltaV + " m/s, TWR " + byHand.LiftoffTwr + ")."); }
                }
            }
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
            days *= BuildTimeFactor() * LeadTimeFactorFor(project);
            return Math.Max(1, (int)Math.Round(days));
        }

        private int BuildDaysFor(MissionType type, double cost, Project project)
        {
            double days = Settings.BuildBaseDays + cost / 10000.0;
            if (project.ProgramManager.Length > 0) days *= 0.9;
            days *= BuildTimeFactor() * LeadTimeFactorFor(project);
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
                            if (day >= p.PhaseStartDay + p.DesignDays && !DesignInProgress(p.Id) && NeedsAiDesign(p) && !HasApprovedDesign(p))
                            {
                                // Nothing to fall back on: the team keeps at it, or the project is called off with its money back.
                                RetryAiDesign(p, notices);
                                break;
                            }
                            if (day >= p.PhaseStartDay + p.DesignDays && !DesignInProgress(p.Id))
                            {
                                p.Status = ProjectStatus.Build; p.PhaseStartDay = p.PhaseStartDay + p.DesignDays; changed = true;
                                string designName = p.Design != null && p.Design.IsAi && p.Design.Verdict == Design.DesignVerdict.Approved
                                    ? p.Design.DesignName + " (AI)"
                                    : DesignNameOf(p);
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
                            if (p.Mode == MissionMode.WatchLive)
                            {
                                string blocker = LiveBlocker(p);
                                if (blocker != null)
                                {
                                    p.Mode = MissionMode.Simulate;
                                    p.Notes.Add("Live flight not possible: " + blocker + " Flown as a simulation.");
                                    Push(notices, p.Name + ": " + blocker);
                                }
                                else if (!p.Notes.Exists(n => n.StartsWith("Ready on the pad")))
                                {
                                    p.Notes.Add("Ready on the pad; waiting for the Director to launch from Mission Control.");
                                    Push(notices, p.Name + " is ready: launch it live from Mission Control");
                                }
                            }
                            if (p.Mode == MissionMode.Simulate)
                            {
                                // One pad: launches go at least launchSpacingDays apart (2026-10-28). The day it became ready,
                                // or the next launch window, so catch-up ticks resolve in sequence.
                                int launchDay = Math.Max(p.PhaseStartDay, NextLaunchDay);
                                if (launchDay > day)
                                {
                                    if (!p.Notes.Exists(n => n.StartsWith("Waiting for the pad")))
                                        p.Notes.Add("Waiting for the pad: launches go " + Settings.LaunchSpacingDays + " days apart; next window day " + NextLaunchDay + ".");
                                    break;
                                }
                                Launch(p, type, launchDay, notices);
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
            if (day > State.LastLaunchDay) State.LastLaunchDay = day;
            Push(notices, p.Name + " launched (" + (p.Mode == MissionMode.Simulate ? "simulated" : "live") + ", " + type.DurationDays + " days)");
            RaiseProjectChanged(p);
        }

        /// <summary>The first day the next automatic launch may go: launchSpacingDays after the last launch (one pad, 2026-10-28).</summary>
        public int NextLaunchDay { get { return State.LastLaunchDay < 0 ? int.MinValue : State.LastLaunchDay + Math.Max(0, Settings.LaunchSpacingDays); } }

        /// <summary>Null when an automatic launch (simulated, or a live roll-out) may go today, else why it waits. The Director's own Launch now skips the queue.</summary>
        public string LaunchWindowBlocker()
        {
            int next = NextLaunchDay;
            if (CurrentDay >= next) return null;
            int wait = next - CurrentDay;
            return "waiting for the pad: next launch window day " + next + " (in " + wait + " day" + (wait == 1 ? "" : "s") + "; launches go " + Settings.LaunchSpacingDays + " days apart)";
        }

        /// <summary>Launches a Ready project immediately instead of waiting for the next tick.</summary>
        public bool LaunchNow(string projectId, out string message)
        {
            Project p = GetProject(projectId);
            if (p == null) { message = "No such project."; return false; }
            if (p.Status != ProjectStatus.Ready) { message = p.Name + " is not ready to launch."; return false; }
            MissionType type = Catalogs.Missions.Get(p.MissionTypeId);
            if (type == null) { message = "Unknown mission type."; return false; }
            if (p.Mode == MissionMode.WatchLive)
            {
                if (LiveBlocker(p) == null) return LaunchLive(projectId, out message);
                p.Mode = MissionMode.Simulate;
                p.Notes.Add("Live flight not possible; flown as a simulation.");
            }
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
            input.LeadDesignBonus = LeadDesignBonusFor(p);
            input.DesignOverride = DesignOverride(p);
            Program program = p.ProgramId.Length > 0 ? GetProgram(p.ProgramId) : null;
            if (program != null && type.IsLanding) input.SiteSlope = program.SiteSlope;
            input.SuccessBonus = SuccessBonus();

            MissionReport r = MissionSimulator.Resolve(p, input, day, Rng);
            r.MissionId = p.MissionId;
            MaybeStrand(type, r); // KSA-65: survivors of a failure far from home may be stuck there
            ApplyReport(p, r, notices);
        }

        /// <summary>Applies a mission report to the crew records, ledger-free. Used for simulated and (later) live missions.</summary>
        public void ApplyReport(Project p, MissionReport r, List<string> notices)
        {
            int day = r.Day;
            if (r.Stranded.Count > 0) r.Achievements.RemoveAll(a => a.StartsWith("Recover", StringComparison.OrdinalIgnoreCase)); // nobody came home
            // Make sure the clock-based flight time matches the report: book a fixed duration instead.
            foreach (string id in r.Crew)
            {
                CrewProfile c = Crew.Get(id);
                if (c == null) continue;
                c.InFlightSinceUT = -1;                 // don't double-book against the clock
                c.Record.DaysInSpace += r.DaysInSpace;
                UpdateExplorationRecord(c, r);
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
            if (r.Stranded.Count > 0) Strand(p, r, notices);
            if (string.Equals(p.ModuleRole, "rescue", StringComparison.OrdinalIgnoreCase)) ApplyRescue(p, r, notices);
            DriftAfterMission(r);
            ClaimPlayerFirsts(notices); // a first is claimed the moment it is flown, not at year end
            AwardExperience(p, r, notices);
            ApplyPersonalityAfterMission(p, r, notices);
            AwardMissionScience(r, Catalogs.Missions.Get(p.MissionTypeId), notices);
            p.Report = r;
            p.EndDay = day;
            p.Status = r.IsSuccess ? ProjectStatus.Complete : ProjectStatus.Failed;
            ReleaseStaff(p);
            Reports.Add(r);
            if (Reports.Count > MaxReports) Reports.RemoveRange(0, Reports.Count - MaxReports);
            Push(notices, p.Name + ": " + OutcomeText(r.Outcome));
            RaiseProjectChanged(p);
            RequestDirectorTurn(p.Name + " ended: " + OutcomeText(r.Outcome));
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
            string blockedBy = ProgramBlocker(type, body);
            if (blockedBy != null)
            {
                message = type.Name + ": " + blockedBy + ".";
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
            Push(null, "Program " + program.Name + " started (" + type.RequiredModuleCount + " modules).");

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
            string templateId = null;
            if (recipe.Templates.Count > 0)
            {
                // KSA-67: a module with its own templates (the colony industry module) uses the first one that can be built.
                MissionType mt = Catalogs.Missions.Get(recipe.MissionTypeId);
                List<Template> able = TemplatesFor(mt, recipe.Role);
                var why = new List<string>();
                foreach (string id in recipe.Templates)
                {
                    Template t = able.Find(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (t == null) continue;
                    string blocked = TemplateBlocker(t);
                    if (blocked == null) { templateId = t.Id; break; }
                    why.Add(t.Name + " " + blocked);
                }
                if (templateId == null)
                {
                    reason = "The " + recipe.Role + " module has no design it can build yet" + (why.Count > 0 ? ": " + string.Join("; ", why.ToArray()) : " with the mods installed") + ".";
                    return null;
                }
            }
            Project p = CreateProject(moduleName, recipe.MissionTypeId, templateId, null, null, program.ProgramManager, null, out reason, recipe.Role);
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
            if (string.Equals(p.ModuleRole, "resupply", StringComparison.OrdinalIgnoreCase)) { if (r != null) ApplyResupply(p, r, program); return; }
            if (p.Status == ProjectStatus.Complete && r != null && r.Mode == MissionMode.WatchLive) NoteCrewAboard(p, program);
            if (p.Status == ProjectStatus.Complete) CountHardware(program, p, true); // KSA-64: what the module carried

            if (p.Status == ProjectStatus.Complete)
            {
                if (program.NextModuleIndex >= type.RequiredModuleCount)
                {
                    bool firstTime = program.Stage < ColonyStage.Base;
                    program.Status = ProgramStatus.Complete;
                    if (program.LastSuppliedDay < 0) program.LastSuppliedDay = CurrentDay;
                    // The whole program's crews share the Base:<body> achievement (station, base or cloud city).
                    foreach (string pid in program.ModuleProjectIds) { Project mp = GetProject(pid); if (mp == null) continue; foreach (string cid in mp.Crew) AddAchievements(cid, new[] { "Base:" + program.Body }); }
                    Push(null, firstTime ? "Program " + program.Name + " complete." : program.Name + ": " + p.ModuleRole + " module delivered.");
                    UpdateStage(program, null);
                    return;
                }
                UpdateStage(program, null);
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
