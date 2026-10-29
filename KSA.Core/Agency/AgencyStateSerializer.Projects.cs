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
                n.AddValue("rescue", p.RescueId);
                if (p.DepotId.Length > 0) n.AddValue("depot", p.DepotId);
                n.AddValue("budget", p.Budget);
                n.AddValue("mode", p.Mode.ToString());
                n.AddValue("status", p.Status.ToString());
                n.AddValue("createdDay", p.CreatedDay);
                n.AddValue("designDays", p.DesignDays);
                n.AddValue("designAttempts", p.DesignAttempts);
                n.AddValue("byAiDirector", p.ByAiDirector);
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
            DataNode dir = root.AddChild("DIRECTOR"); // KSA-68
            dir.AddValue("enabled", agency.State.AiDirector);
            dir.AddValue("flights", agency.State.DirectorFlights.ToString());
            dir.AddValue("plan", Agency.Clean(agency.DirectorPlan));
            dir.AddValue("lastTurnDay", agency.DirectorLastTurnDay);
            foreach (KSA.Core.Ai.DirectorLogEntry e in agency.DirectorLog)
            {
                DataNode en = dir.AddChild("ENTRY");
                en.AddValue("day", e.Day); en.AddValue("year", e.Year); en.AddValue("kind", e.Kind); en.AddValue("text", Agency.Clean(e.Text));
            }
            root.AddChild(agency.News.ToDataNode());
            foreach (KSA.Core.Rivals.RivalAgency rv in agency.Rivals) root.AddChild(rv.ToDataNode());
            foreach (KSA.Core.Analytics.YearSummary ys in agency.YearHistory) root.AddChild(ys.ToDataNode());
            foreach (FirstClaim fc in agency.Firsts)
            {
                DataNode fn = root.AddChild("FIRST").AddValue("achievement", fc.Achievement).AddValue("by", fc.ClaimedBy).AddValue("year", fc.Year).AddValue("mission", fc.Mission).AddValue("day", fc.Day);
                foreach (string c in fc.Crew) fn.AddValue("crew", c);
            }
            foreach (RecordEntry re in agency.Records.Values)
                root.AddChild("RECORD").AddValue("key", re.Key).AddValue("value", re.Value).AddValue("holder", re.Holder).AddValue("mission", re.Mission).AddValue("day", re.Day).AddValue("year", re.Year);
            foreach (DepartmentPost dp in agency.Departments) if (dp.Filled) root.AddChild("DEPARTMENT").AddValue("department", dp.Department.ToString()).AddValue("crew", dp.CrewId).AddValue("sinceDay", dp.SinceDay);
            foreach (MissionType mt in agency.Catalogs.Missions.Types)
            {
                if (!mt.IsCustom) continue;
                DataNode cm = root.AddChild("CUSTOM_MISSION");
                cm.AddValue("id", mt.Id); cm.AddValue("name", mt.Name); cm.AddValue("description", mt.Description); cm.AddValue("tier", mt.Tier.ToString()); cm.AddValue("body", mt.Body);
                cm.AddValue("profile", mt.Profile.ToString()); cm.AddValue("crew", mt.CrewSize); cm.AddValue("passengers", mt.PassengerSeats); cm.AddValue("minStars", mt.MinStars); cm.AddValue("durationDays", mt.DurationDays);
                cm.AddValue("baseCost", mt.BaseCost); cm.AddValue("difficulty", mt.Difficulty); cm.AddValue("orbits", mt.Orbits); cm.AddValue("surfaceDays", mt.SurfaceDays);
                cm.AddValue("purpose", mt.Purpose); cm.AddValue("scienceYield", mt.ScienceYield);
                foreach (string a in mt.Achievements) cm.AddValue("achievement", a);
            }

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
                n.AddValue("lastSuppliedDay", g.LastSuppliedDay);
                foreach (string cid in g.CrewAboard) n.AddValue("aboard", cid);
                n.AddValue("stage", g.Stage.ToString());
                n.AddValue("resupplies", g.ResuppliesDelivered);
                n.AddValue("colonistsMale", g.ColonistsMale);
                n.AddValue("colonistsFemale", g.ColonistsFemale);
                n.AddValue("seats", g.Seats);
                n.AddValue("drills", g.Drills);
                n.AddValue("isru", g.Isru);
                n.AddValue("power", g.Power);
                n.AddValue("greenhouses", g.Greenhouses);
                n.AddValue("scrubbers", g.Scrubbers);
                n.AddValue("waterRecyclers", g.WaterRecyclers);
                n.AddValue("rocket", g.HasRocket);
                n.AddValue("extraRoom", g.ExtraRoom);
                if (g.DepotCapacity > 0) { n.AddValue("depotFuel", g.DepotFuel); n.AddValue("depotCapacity", g.DepotCapacity); }
                n.AddValue("colonyDay", g.ColonyDay);
                foreach (string hp in g.HardwareCounted) n.AddValue("hardware", hp);
                foreach (string pid in g.ModuleProjectIds) n.AddValue("module", pid);
                foreach (string note in g.Notes) n.AddValue("note", note);
            }

            foreach (Stranding s in agency.Strandings)
            {
                DataNode n = root.AddChild("STRANDING");
                n.AddValue("id", s.Id);
                n.AddValue("body", s.Body);
                n.AddValue("onSurface", s.OnSurface);
                n.AddValue("vesselId", s.VesselId);
                n.AddValue("day", s.Day);
                n.AddValue("cause", s.Cause);
                n.AddValue("rescue", s.RescueProjectId);
                n.AddValue("resolved", s.Resolved);
                foreach (string c in s.Crew) n.AddValue("crew", c);
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
                n.AddValue("maxAltitude", r.MaxAltitude);
                n.AddValue("maxSpeed", r.MaxSpeed);
                n.AddValue("farthestKm", r.FarthestKm);
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
                    RescueId = n.GetValue("rescue", string.Empty),
                    DepotId = n.GetValue("depot", string.Empty),
                    Budget = n.GetDouble("budget", 0),
                    Mode = ParseEnum(n.GetValue("mode"), MissionMode.Simulate),
                    Status = ParseEnum(n.GetValue("status"), ProjectStatus.Design),
                    CreatedDay = n.GetInt("createdDay", 0),
                    DesignDays = n.GetInt("designDays", 1),
                    DesignAttempts = n.GetInt("designAttempts", 0),
                    ByAiDirector = n.GetBool("byAiDirector", false),
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
                    // The game was saved mid-session; the team picks the work up again on the next tick (ResumeInterruptedDesigns).
                    p.Design.Error = "Design session interrupted by save/load; resuming.";
                }
                agency.Projects.Add(p);
            }
            agency.Costs.Load(root.GetChild("COST_GUARD"));
            DataNode dirNode = root.GetChild("DIRECTOR");
            if (dirNode != null)
            {
                agency.State.AiDirector = dirNode.GetBool("enabled", false);
                agency.State.DirectorFlights = ParseEnum(dirNode.GetValue("flights"), KSA.Core.Ai.DirectorFlights.Firsts);
                agency.DirectorPlan = dirNode.GetValue("plan", string.Empty);
                agency.DirectorLastTurnDay = dirNode.GetInt("lastTurnDay", -1);
                foreach (DataNode en in dirNode.GetChildren("ENTRY"))
                    agency.DirectorLog.Add(new KSA.Core.Ai.DirectorLogEntry { Day = en.GetInt("day", 0), Year = en.GetInt("year", 0), Kind = en.GetValue("kind", "Note"), Text = en.GetValue("text", string.Empty) });
            }
            agency.News.Load(root.GetChild("NEWSFEED"));
            agency.Rivals.Clear();
            foreach (DataNode rv in root.GetChildren("RIVAL")) agency.Rivals.Add(KSA.Core.Rivals.RivalAgency.FromDataNode(rv));
            agency.YearHistory.Clear();
            foreach (DataNode ys in root.GetChildren("YEAR_SUMMARY")) agency.YearHistory.Add(KSA.Core.Analytics.YearSummary.FromDataNode(ys));
            agency.Firsts.Clear();
            foreach (DataNode fc in root.GetChildren("FIRST"))
            {
                var claim = new FirstClaim { Achievement = fc.GetValue("achievement", string.Empty), ClaimedBy = fc.GetValue("by", string.Empty), Year = fc.GetInt("year", 0), Mission = fc.GetValue("mission", string.Empty), Day = fc.GetInt("day", 0) };
                claim.Crew.AddRange(fc.GetValues("crew"));
                agency.Firsts.Add(claim);
            }
            agency.Records.Clear();
            foreach (DataNode rn in root.GetChildren("RECORD"))
            {
                var re = new RecordEntry { Key = rn.GetValue("key", string.Empty), Value = rn.GetDouble("value", 0), Holder = rn.GetValue("holder", string.Empty), Mission = rn.GetValue("mission", string.Empty), Day = rn.GetInt("day", 0), Year = rn.GetInt("year", 0) };
                if (re.Key.Length > 0) agency.Records[re.Key] = re;
            }
            foreach (DataNode cm in root.GetChildren("CUSTOM_MISSION"))
            {
                string cid = cm.GetValue("id", string.Empty);
                if (cid.Length == 0 || agency.Catalogs.Missions.Get(cid) != null) continue;
                MissionTier ctier; MissionProfile cprofile;
                if (!System.Enum.TryParse(cm.GetValue("tier", "Early"), out ctier)) ctier = MissionTier.Early;
                if (!System.Enum.TryParse(cm.GetValue("profile", "Orbit"), out cprofile)) cprofile = MissionProfile.Orbit;
                var mt = new MissionType
                {
                    Id = cid, Name = cm.GetValue("name", cid), Description = cm.GetValue("description", string.Empty), Tier = ctier, Body = cm.GetValue("body", "Kerbin"), Profile = cprofile,
                    CrewSize = cm.GetInt("crew", 1), PassengerSeats = cm.GetInt("passengers", 0), MinStars = cm.GetInt("minStars", 0), DurationDays = cm.GetInt("durationDays", 1), BaseCost = cm.GetDouble("baseCost", 10000), Difficulty = cm.GetDouble("difficulty", 0.3),
                    Orbits = cm.GetInt("orbits", 0), SurfaceDays = cm.GetDouble("surfaceDays", 0), Purpose = cm.GetValue("purpose", string.Empty), ScienceYield = cm.GetDouble("scienceYield", 0), IsCustom = true
                };
                mt.Achievements.AddRange(cm.GetValues("achievement"));
                agency.Catalogs.Missions.Add(mt);
            }
            foreach (DataNode dn in root.GetChildren("DEPARTMENT"))
            {
                Department dept;
                if (!System.Enum.TryParse(dn.GetValue("department", string.Empty), out dept)) continue;
                DepartmentPost post = agency.Post(dept);
                post.CrewId = dn.GetValue("crew", string.Empty);
                post.SinceDay = dn.GetInt("sinceDay", 0);
            }

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
                    , LastSuppliedDay = n.GetInt("lastSuppliedDay", -1)
                };
                if (g.Id.Length == 0) continue;
                g.ModuleProjectIds.AddRange(n.GetValues("module"));
                g.CrewAboard.AddRange(n.GetValues("aboard"));
                g.Stage = ParseEnum(n.GetValue("stage"), ColonyStage.None);
                g.ResuppliesDelivered = n.GetInt("resupplies", 0);
                g.ColonistsMale = n.GetInt("colonistsMale", 0);
                g.ColonistsFemale = n.GetInt("colonistsFemale", 0);
                g.Seats = n.GetInt("seats", 0);
                g.Drills = n.GetInt("drills", 0);
                g.Isru = n.GetInt("isru", 0);
                g.Power = n.GetInt("power", 0);
                g.Greenhouses = n.GetInt("greenhouses", 0);
                g.Scrubbers = n.GetInt("scrubbers", 0);
                g.WaterRecyclers = n.GetInt("waterRecyclers", 0);
                g.HasRocket = n.GetBool("rocket", false);
                g.ExtraRoom = n.GetInt("extraRoom", 0);
                g.DepotFuel = n.GetDouble("depotFuel", 0);
                g.DepotCapacity = n.GetDouble("depotCapacity", 0);
                g.ColonyDay = n.GetInt("colonyDay", -1);
                g.HardwareCounted.AddRange(n.GetValues("hardware"));
                if (g.Stage == ColonyStage.None && g.Status == ProgramStatus.Complete) g.Stage = ColonyStage.Base; // saves from before KSA-64
                g.Notes.AddRange(n.GetValues("note"));
                agency.Programs.Add(g);
            }

            foreach (DataNode n in root.GetChildren("STRANDING"))
            {
                var s = new Stranding
                {
                    Id = n.GetValue("id", string.Empty),
                    Body = n.GetValue("body", "Kerbin"),
                    OnSurface = n.GetBool("onSurface", false),
                    VesselId = n.GetValue("vesselId", string.Empty),
                    Day = n.GetInt("day", 0),
                    Cause = n.GetValue("cause", string.Empty),
                    RescueProjectId = n.GetValue("rescue", string.Empty),
                    Resolved = n.GetBool("resolved", false)
                };
                if (s.Id.Length == 0) continue;
                s.Crew.AddRange(n.GetValues("crew"));
                agency.Strandings.Add(s);
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
                    MaxAltitude = n.GetDouble("maxAltitude", 0),
                    MaxSpeed = n.GetDouble("maxSpeed", 0),
                    FarthestKm = n.GetDouble("farthestKm", 0),
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
