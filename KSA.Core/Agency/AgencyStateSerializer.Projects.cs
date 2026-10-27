using System;
using KSA.Core.Data;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;

namespace KSA.Core
{
    /// <summary>Save format v3: PROJECT, PROGRAM and REPORT nodes.</summary>
    public static partial class AgencyStateSerializer
    {
        private static void WriteProjects(DataNode root, Agency agency)
        {
            root.AddValue("nextProjectNumber", agency.NextProjectNumber);
            root.AddValue("nextProgramNumber", agency.NextProgramNumber);

            foreach (Project p in agency.Projects)
            {
                DataNode n = root.AddChild("PROJECT");
                n.AddValue("id", p.Id);
                n.AddValue("name", p.Name);
                n.AddValue("missionType", p.MissionTypeId);
                n.AddValue("template", p.TemplateId);
                n.AddValue("goal", p.GoalId);
                n.AddValue("program", p.ProgramId);
                n.AddValue("moduleRole", p.ModuleRole);
                n.AddValue("budget", p.Budget);
                n.AddValue("mode", p.Mode.ToString());
                n.AddValue("status", p.Status.ToString());
                n.AddValue("createdDay", p.CreatedDay);
                n.AddValue("designDays", p.DesignDays);
                n.AddValue("buildDays", p.BuildDays);
                n.AddValue("phaseStartDay", p.PhaseStartDay);
                n.AddValue("launchDay", p.LaunchDay);
                n.AddValue("endDay", p.EndDay);
                n.AddValue("lead", p.LeadEngineer);
                n.AddValue("programManager", p.ProgramManager);
                n.AddValue("missionId", p.MissionId);
                foreach (string c in p.Crew) n.AddValue("crew", c);
                foreach (string e in p.Engineers) n.AddValue("engineer", e);
                foreach (string note in p.Notes) n.AddValue("note", note);
                n.AddValue("vesselId", p.VesselId);
                n.AddValue("directorFlown", p.DirectorFlown);
                n.AddValue("launchUT", p.LaunchUT);
                foreach (string e in p.FlightLog) n.AddValue("flight", e);
                foreach (string a in p.FlightAchievements) n.AddValue("flightAchievement", a);
                if (p.Design != null) n.AddChild(p.Design.ToDataNode());
            }
            root.AddChild(agency.Costs.ToDataNode());
            root.AddChild(agency.News.ToDataNode());
            foreach (KSA.Core.Rivals.RivalAgency rv in agency.Rivals) root.AddChild(rv.ToDataNode());
            foreach (KSA.Core.Analytics.YearSummary ys in agency.YearHistory) root.AddChild(ys.ToDataNode());
            foreach (FirstClaim fc in agency.Firsts) root.AddChild("FIRST").AddValue("achievement", fc.Achievement).AddValue("by", fc.ClaimedBy).AddValue("year", fc.Year);

            foreach (Program g in agency.Programs)
            {
                DataNode n = root.AddChild("PROGRAM");
                n.AddValue("id", g.Id);
                n.AddValue("name", g.Name);
                n.AddValue("programType", g.ProgramTypeId);
                n.AddValue("body", g.Body);
                n.AddValue("programManager", g.ProgramManager);
                n.AddValue("status", g.Status.ToString());
                n.AddValue("createdDay", g.CreatedDay);
                n.AddValue("nextModuleIndex", g.NextModuleIndex);
                n.AddValue("site", g.Site);
                n.AddValue("siteSlope", g.SiteSlope);
                n.AddValue("vesselId", g.VesselId);
                foreach (string pid in g.ModuleProjectIds) n.AddValue("module", pid);
                foreach (string note in g.Notes) n.AddValue("note", note);
            }

            foreach (MissionReport r in agency.Reports)
            {
                DataNode n = root.AddChild("REPORT");
                n.AddValue("project", r.ProjectId);
                n.AddValue("projectName", r.ProjectName);
                n.AddValue("missionId", r.MissionId);
                n.AddValue("missionType", r.MissionTypeId);
                n.AddValue("day", r.Day);
                n.AddValue("outcome", r.Outcome.ToString());
                n.AddValue("mode", r.Mode.ToString());
                n.AddValue("daysInSpace", r.DaysInSpace);
                n.AddValue("designScore", r.DesignScore);
                n.AddValue("crewScore", r.CrewScore);
                n.AddValue("successChance", r.SuccessChance);
                n.AddValue("lesson", r.Lesson);
                n.AddValue("directorFlown", r.DirectorFlown);
                n.AddValue("postMortem", r.PostMortem);
                foreach (string c in r.Crew) n.AddValue("crew", c);
                foreach (string l in r.Lines) n.AddValue("line", l);
                foreach (string a in r.Achievements) n.AddValue("achievement", a);
                foreach (IncidentKind k in r.Incidents) n.AddValue("incident", k.ToString());
                foreach (string d in r.Deaths) n.AddValue("death", d);
            }
        }

        private static void ReadProjects(DataNode root, Agency agency)
        {
            agency.Projects.Clear();
            agency.Programs.Clear();
            agency.Reports.Clear();
            agency.NextProjectNumber = Math.Max(1, root.GetInt("nextProjectNumber", 1));
            agency.NextProgramNumber = Math.Max(1, root.GetInt("nextProgramNumber", 1));

            foreach (DataNode n in root.GetChildren("PROJECT"))
            {
                var p = new Project
                {
                    Id = n.GetValue("id", string.Empty),
                    Name = n.GetValue("name", string.Empty),
                    MissionTypeId = n.GetValue("missionType", string.Empty),
                    TemplateId = n.GetValue("template", string.Empty),
                    GoalId = n.GetValue("goal", string.Empty),
                    ProgramId = n.GetValue("program", string.Empty),
                    ModuleRole = n.GetValue("moduleRole", string.Empty),
                    Budget = n.GetDouble("budget", 0),
                    Mode = ParseEnum(n.GetValue("mode"), MissionMode.Simulate),
                    Status = ParseEnum(n.GetValue("status"), ProjectStatus.Design),
                    CreatedDay = n.GetInt("createdDay", 0),
                    DesignDays = n.GetInt("designDays", 1),
                    BuildDays = n.GetInt("buildDays", 1),
                    PhaseStartDay = n.GetInt("phaseStartDay", 0),
                    LaunchDay = n.GetInt("launchDay", -1),
                    EndDay = n.GetInt("endDay", -1),
                    LeadEngineer = n.GetValue("lead", string.Empty),
                    ProgramManager = n.GetValue("programManager", string.Empty),
                    MissionId = n.GetValue("missionId", string.Empty)
                };
                if (p.Id.Length == 0) continue;
                p.Crew.AddRange(n.GetValues("crew"));
                p.Engineers.AddRange(n.GetValues("engineer"));
                p.Notes.AddRange(n.GetValues("note"));
                p.VesselId = n.GetValue("vesselId", string.Empty);
                p.DirectorFlown = n.GetBool("directorFlown", false);
                p.LaunchUT = n.GetDouble("launchUT", -1);
                p.FlightLog.AddRange(n.GetValues("flight"));
                p.FlightAchievements.AddRange(n.GetValues("flightAchievement"));
                p.Design =KSA.Core.Design.DesignRecord.FromDataNode(n.GetChild("DESIGN"));
                if (p.Design != null && p.Design.Verdict == KSA.Core.Design.DesignVerdict.Pending)
                {
                    // The game was saved mid-session; the session is gone. Fall back to the template.
                    p.Design.Verdict = KSA.Core.Design.DesignVerdict.Failed;
                    p.Design.Error = "Design session interrupted by save/load.";
                }
                agency.Projects.Add(p);
            }
            agency.Costs.Load(root.GetChild("COST_GUARD"));
            agency.News.Load(root.GetChild("NEWSFEED"));
            agency.Rivals.Clear();
            foreach (DataNode rv in root.GetChildren("RIVAL")) agency.Rivals.Add(KSA.Core.Rivals.RivalAgency.FromDataNode(rv));
            agency.YearHistory.Clear();
            foreach (DataNode ys in root.GetChildren("YEAR_SUMMARY")) agency.YearHistory.Add(KSA.Core.Analytics.YearSummary.FromDataNode(ys));
            agency.Firsts.Clear();
            foreach (DataNode fc in root.GetChildren("FIRST")) agency.Firsts.Add(new FirstClaim { Achievement = fc.GetValue("achievement", string.Empty), ClaimedBy = fc.GetValue("by", string.Empty), Year = fc.GetInt("year", 0) });

            foreach (DataNode n in root.GetChildren("PROGRAM"))
            {
                var g = new Program
                {
                    Id = n.GetValue("id", string.Empty),
                    Name = n.GetValue("name", string.Empty),
                    ProgramTypeId = n.GetValue("programType", string.Empty),
                    Body = n.GetValue("body", string.Empty),
                    ProgramManager = n.GetValue("programManager", string.Empty),
                    Status = ParseEnum(n.GetValue("status"), ProgramStatus.Active),
                    CreatedDay = n.GetInt("createdDay", 0),
                    NextModuleIndex = n.GetInt("nextModuleIndex", 0),
                    Site = n.GetValue("site", string.Empty),
                    SiteSlope = n.GetDouble("siteSlope", 0)
                    , VesselId = n.GetValue("vesselId", string.Empty)
                };
                if (g.Id.Length == 0) continue;
                g.ModuleProjectIds.AddRange(n.GetValues("module"));
                g.Notes.AddRange(n.GetValues("note"));
                agency.Programs.Add(g);
            }

            foreach (DataNode n in root.GetChildren("REPORT"))
            {
                var r = new MissionReport
                {
                    ProjectId = n.GetValue("project", string.Empty),
                    ProjectName = n.GetValue("projectName", string.Empty),
                    MissionId = n.GetValue("missionId", string.Empty),
                    MissionTypeId = n.GetValue("missionType", string.Empty),
                    Day = n.GetInt("day", 0),
                    Outcome = ParseEnum(n.GetValue("outcome"), MissionOutcome.Success),
                    Mode = ParseEnum(n.GetValue("mode"), MissionMode.Simulate),
                    DaysInSpace = n.GetDouble("daysInSpace", 0),
                    DesignScore = n.GetDouble("designScore", 0),
                    CrewScore = n.GetDouble("crewScore", 0),
                    SuccessChance = n.GetDouble("successChance", 0),
                    Lesson = n.GetValue("lesson", string.Empty),
                    DirectorFlown = n.GetBool("directorFlown", false),
                    PostMortem = n.GetValue("postMortem", string.Empty)
                };
                r.Crew.AddRange(n.GetValues("crew"));
                r.Lines.AddRange(n.GetValues("line"));
                r.Achievements.AddRange(n.GetValues("achievement"));
                foreach (string k in n.GetValues("incident")) r.Incidents.Add(ParseEnum(k, IncidentKind.Other));
                r.Deaths.AddRange(n.GetValues("death"));
                agency.Reports.Add(r);
                Project owner = agency.GetProject(r.ProjectId);
                if (owner != null && owner.Report == null) owner.Report = r;
            }
        }
    }
}
