using System;
using System.Collections.Generic;
using KSA.Core.Data;
using KSA.Core.Finance;
using KSA.Core.Goals;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;
using KSA.Core.Review;

namespace KSA.Core
{
    /// <summary>
    /// Agency state to and from a <see cref="DataNode"/> tree (Jira KSA-22, KSA-26). The host stores the tree in its save.
    /// Format version 2 adds STATE, LEDGER, GOAL, REVIEW, APPLICANT and extra crew fields. Version 1 saves load with
    /// defaults (the agency then starts its books on the next tick). Unknown keys are ignored.
    /// </summary>
    public static partial class AgencyStateSerializer
    {
        public const string RootName = "KSA_AGENCY";
        public const int CurrentVersion = 4;

        public static DataNode Write(Agency agency)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            var root = new DataNode(RootName);
            root.AddValue("version", CurrentVersion);
            root.AddValue("nextMissionNumber", agency.NextMissionNumber);

            // --- agency state ---
            AgencyState s = agency.State;
            DataNode st = root.AddChild("STATE");
            st.AddValue("started", s.Started);
            st.AddValue("currentYear", s.CurrentYear);
            st.AddValue("prestige", s.Prestige);
            st.AddValue("agencyName", s.AgencyName);
            st.AddValue("hiringFreeze", s.HiringFreeze);
            st.AddValue("probation", s.Probation);
            st.AddValue("directorNumber", s.DirectorNumber);
            st.AddValue("pendingLayoffs", s.PendingLayoffs);
            st.AddValue("yearStartBalance", s.YearStartBalance);
            st.AddValue("deathsThisYear", s.DeathsThisYear);
            st.AddValue("missionsThisYear", s.MissionsThisYear);
            st.AddValue("lastWalkInQuarter", s.LastWalkInQuarter);
            st.AddValue("lastWalkInYear", s.LastWalkInYear);
            foreach (var kv in agency.ManualAwardsThisYear)
                st.AddChild("MANUAL_AWARD").AddValue("medal", kv.Key).AddValue("count", kv.Value);

            // --- ledger ---
            DataNode ledger = root.AddChild("LEDGER");
            ledger.AddValue("balance", agency.Ledger.Balance);
            foreach (LedgerEntry e in agency.Ledger.Entries)
            {
                DataNode n = ledger.AddChild("ENTRY");
                n.AddValue("day", e.Day);
                n.AddValue("year", e.Year);
                n.AddValue("amount", e.Amount);
                n.AddValue("category", e.Category.ToString());
                n.AddValue("reason", e.Reason);
            }

            // --- goals ---
            foreach (GoalState g in agency.ActiveGoals) WriteGoal(root.AddChild("GOAL"), g);
            foreach (GoalState g in agency.GoalHistory) WriteGoal(root.AddChild("GOAL"), g);

            // --- reviews ---
            foreach (ReviewResult r in agency.Reviews)
            {
                DataNode n = root.AddChild("REVIEW");
                n.AddValue("year", r.Year);
                n.AddValue("issued", r.GoalsIssued);
                n.AddValue("met", r.GoalsMet);
                n.AddValue("deaths", r.Deaths);
                n.AddValue("band", r.Band.ToString());
                n.AddValue("budgetChangePercent", r.BudgetChangePercent);
                n.AddValue("nextGrant", r.NextGrant);
                n.AddValue("prestigeBefore", r.PrestigeBefore);
                n.AddValue("prestigeAfter", r.PrestigeAfter);
                n.AddValue("probationBefore", r.ProbationBefore);
                n.AddValue("probationAfter", r.ProbationAfter);
                n.AddValue("directorReplaced", r.DirectorReplaced);
                n.AddValue("hiringFreeze", r.HiringFreeze);
                n.AddValue("layoffs", r.Layoffs);
                foreach (string g in r.MetGoals) n.AddValue("metGoal", g);
                foreach (string g in r.MissedGoals) n.AddValue("missedGoal", g);
                foreach (string e in r.Events) n.AddValue("event", e);
            }

            // --- applicants ---
            DataNode pool = root.AddChild("APPLICANTS");
            pool.AddValue("nextId", agency.Applicants.NextId);
            foreach (Applicant a in agency.Applicants.Applicants) WriteApplicant(pool.AddChild("APPLICANT"), a);
            foreach (Applicant a in agency.Applicants.Returning) WriteApplicant(pool.AddChild("RETURNING"), a);

            WriteProjects(root, agency);

            // --- crew ---
            foreach (CrewProfile p in agency.Crew.All)
            {
                DataNode c = root.AddChild("CREW");
                c.AddValue("id", p.Id);
                c.AddValue("name", p.Name);
                c.AddValue("role", p.Role.ToString());
                c.AddValue("stars", p.Stars);
                c.AddValue("courage", p.Courage);
                c.AddValue("stupidity", p.Stupidity);
                c.AddValue("traitsOwned", p.TraitsOwned);
                c.AddValue("status", p.Status.ToString());
                c.AddValue("track", p.Track.ToString());
                c.AddValue("rank", p.RankId);
                c.AddValue("morale", p.Morale);
                c.AddValue("salary", p.Salary);
                c.AddValue("bornDay", p.BornDay);
                c.AddValue("hiredDay", p.HiredDay);
                c.AddValue("inFlightSinceUT", p.InFlightSinceUT);
                c.AddValue("departed", p.Departed);
                c.AddValue("departureReason", p.DepartureReason.ToString());
                c.AddValue("departedDay", p.DepartedDay);
                c.AddValue("lowMoraleYears", p.LowMoraleYears);
                c.AddValue("lastMissionYear", p.LastMissionYear);
                c.AddValue("portrait", p.PortraitKey);
                c.AddValue("project", p.ProjectId);
                c.AddValue("daysInSpace", p.Record.DaysInSpace);
                foreach (string tag in p.PersonalityTags) c.AddValue("tag", tag);
                foreach (string a in p.Record.Achievements) c.AddValue("achievement", a);
                foreach (string q in p.Record.Quotes) c.AddValue("quote", q);

                foreach (MissionEntry m in p.Record.Missions)
                {
                    DataNode n = c.AddChild("MISSION");
                    n.AddValue("id", m.MissionId);
                    n.AddValue("name", m.Name);
                    n.AddValue("startDay", m.StartDay);
                    n.AddValue("endDay", m.EndDay);
                    n.AddValue("outcome", m.Outcome.ToString());
                    n.AddValue("directorFlown", m.DirectorFlown);
                }
                foreach (MedalAward a in p.Record.Medals)
                {
                    DataNode n = c.AddChild("MEDAL");
                    n.AddValue("id", a.MedalId);
                    n.AddValue("day", a.Day);
                    n.AddValue("mission", a.MissionId);
                    n.AddValue("note", a.Note);
                }
                foreach (IncidentEntry i in p.Record.Incidents)
                {
                    DataNode n = c.AddChild("INCIDENT");
                    n.AddValue("kind", i.Kind.ToString());
                    n.AddValue("day", i.Day);
                    n.AddValue("mission", i.MissionId);
                    n.AddValue("description", i.Description);
                }
            }
            return root;
        }

        private static void WriteGoal(DataNode n, GoalState g)
        {
            n.AddValue("id", g.GoalId);
            n.AddValue("yearIssued", g.YearIssued);
            n.AddValue("yearResolved", g.YearResolved);
            n.AddValue("status", g.Status.ToString());
        }

        private static void WriteApplicant(DataNode n, Applicant a)
        {
            n.AddValue("id", a.Id);
            n.AddValue("name", a.Name);
            n.AddValue("role", a.Role.ToString());
            n.AddValue("stars", a.Stars);
            n.AddValue("courage", a.Courage);
            n.AddValue("stupidity", a.Stupidity);
            n.AddValue("source", a.SourceId);
            n.AddValue("arrivedDay", a.ArrivedDay);
            n.AddValue("interviews", a.Interviews);
            n.AddValue("returnDay", a.ReturnDay);
            n.AddValue("hireFee", a.HireFee);
            foreach (string t in a.Tags) n.AddValue("tag", t);
            foreach (string ach in a.Achievements) n.AddValue("achievement", ach);
            foreach (string r in a.Revealed) n.AddValue("revealed", r);
        }

        /// <summary>Replaces the agency's state with the saved one.</summary>
        public static void Read(DataNode root, Agency agency)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            agency.Crew.Clear();
            agency.ActiveGoals.Clear();
            agency.GoalHistory.Clear();
            agency.Reviews.Clear();
            agency.Applicants.Applicants.Clear();
            agency.Applicants.Returning.Clear();
            agency.ManualAwardsThisYear.Clear();
            agency.Ledger.Entries.Clear();
            if (root == null) return;

            agency.NextMissionNumber = Math.Max(1, root.GetInt("nextMissionNumber", 1));

            // --- state (absent in v1) ---
            DataNode st = root.GetChild("STATE");
            AgencyState s = agency.State;
            if (st != null)
            {
                s.Started = st.GetBool("started", false);
                s.CurrentYear = st.GetInt("currentYear", 0);
                s.Prestige = st.GetDouble("prestige", agency.Settings.StartingPrestige);
                s.AgencyName = st.GetValue("agencyName", string.Empty);
                s.HiringFreeze = st.GetBool("hiringFreeze", false);
                s.Probation = st.GetBool("probation", false);
                s.DirectorNumber = st.GetInt("directorNumber", 1);
                s.PendingLayoffs = st.GetInt("pendingLayoffs", 0);
                s.YearStartBalance = st.GetDouble("yearStartBalance", 0);
                s.DeathsThisYear = st.GetInt("deathsThisYear", 0);
                s.MissionsThisYear = st.GetInt("missionsThisYear", 0);
                s.LastWalkInQuarter = st.GetInt("lastWalkInQuarter", -1);
                s.LastWalkInYear = st.GetInt("lastWalkInYear", 0);
                foreach (DataNode n in st.GetChildren("MANUAL_AWARD"))
                {
                    string medal = n.GetValue("medal", string.Empty);
                    if (medal.Length > 0) agency.ManualAwardsThisYear[medal] = n.GetInt("count", 0);
                }
            }
            else
            {
                s.Started = false;
                s.CurrentYear = 0;
            }

            // --- ledger ---
            DataNode ledger = root.GetChild("LEDGER");
            if (ledger != null)
            {
                agency.Ledger.SetBalance(ledger.GetDouble("balance", 0));
                foreach (DataNode n in ledger.GetChildren("ENTRY"))
                {
                    agency.Ledger.Entries.Add(new LedgerEntry
                    {
                        Day = n.GetInt("day", 0),
                        Year = n.GetInt("year", 0),
                        Amount = n.GetDouble("amount", 0),
                        Category = ParseEnum(n.GetValue("category"), LedgerCategory.Other),
                        Reason = n.GetValue("reason", string.Empty)
                    });
                }
            }

            // --- goals ---
            foreach (DataNode n in root.GetChildren("GOAL"))
            {
                var g = new GoalState
                {
                    GoalId = n.GetValue("id", string.Empty),
                    YearIssued = n.GetInt("yearIssued", 0),
                    YearResolved = n.GetInt("yearResolved", -1),
                    Status = ParseEnum(n.GetValue("status"), GoalStatus.Active)
                };
                if (g.GoalId.Length == 0) continue;
                if (g.Status == GoalStatus.Active) agency.ActiveGoals.Add(g); else agency.GoalHistory.Add(g);
            }

            // --- reviews ---
            foreach (DataNode n in root.GetChildren("REVIEW"))
            {
                var r = new ReviewResult
                {
                    Year = n.GetInt("year", 0),
                    GoalsIssued = n.GetInt("issued", 0),
                    GoalsMet = n.GetInt("met", 0),
                    Deaths = n.GetInt("deaths", 0),
                    Band = ParseEnum(n.GetValue("band"), ReviewBand.Flat),
                    BudgetChangePercent = n.GetDouble("budgetChangePercent", 0),
                    NextGrant = n.GetDouble("nextGrant", 0),
                    PrestigeBefore = n.GetDouble("prestigeBefore", 50),
                    PrestigeAfter = n.GetDouble("prestigeAfter", 50),
                    ProbationBefore = n.GetBool("probationBefore", false),
                    ProbationAfter = n.GetBool("probationAfter", false),
                    DirectorReplaced = n.GetBool("directorReplaced", false),
                    HiringFreeze = n.GetBool("hiringFreeze", false),
                    Layoffs = n.GetInt("layoffs", 0)
                };
                r.MetGoals.AddRange(n.GetValues("metGoal"));
                r.MissedGoals.AddRange(n.GetValues("missedGoal"));
                r.Events.AddRange(n.GetValues("event"));
                agency.Reviews.Add(r);
            }

            // --- applicants ---
            DataNode pool = root.GetChild("APPLICANTS");
            if (pool != null)
            {
                agency.Applicants.NextId = Math.Max(1, pool.GetInt("nextId", 1));
                foreach (DataNode n in pool.GetChildren("APPLICANT")) agency.Applicants.Applicants.Add(ReadApplicant(n));
                foreach (DataNode n in pool.GetChildren("RETURNING")) agency.Applicants.Returning.Add(ReadApplicant(n));
            }

            ReadProjects(root, agency);

            // --- crew ---
            foreach (DataNode c in root.GetChildren("CREW"))
            {
                string id = c.GetValue("id", null);
                if (string.IsNullOrEmpty(id)) continue;

                var p = new CrewProfile(id, c.GetValue("name", id))
                {
                    Role = ParseEnum(c.GetValue("role"), CrewRole.Unknown),
                    Stars = c.GetInt("stars", 0),
                    Courage = (float)c.GetDouble("courage", 0.5),
                    Stupidity = (float)c.GetDouble("stupidity", 0.5),
                    TraitsOwned = c.GetBool("traitsOwned", false),
                    Status = ParseEnum(c.GetValue("status"), CrewStatus.Available),
                    Track = ParseEnum(c.GetValue("track"), CareerTrack.None),
                    RankId = c.GetValue("rank", string.Empty),
                    Morale = c.GetInt("morale", CrewProfile.DefaultMorale),
                    Salary = c.GetInt("salary", 0),
                    BornDay = c.GetInt("bornDay", 0),
                    HiredDay = c.GetInt("hiredDay", 0),
                    InFlightSinceUT = c.GetDouble("inFlightSinceUT", -1),
                    Departed = c.GetBool("departed", false),
                    DepartureReason = ParseEnum(c.GetValue("departureReason"), DepartureReason.None),
                    DepartedDay = c.GetInt("departedDay", -1),
                    LowMoraleYears = c.GetInt("lowMoraleYears", 0),
                    LastMissionYear = c.GetInt("lastMissionYear", 0)
                    , PortraitKey = c.GetValue("portrait", string.Empty)
                    , ProjectId = c.GetValue("project", string.Empty)
                };
                if (p.Status == CrewStatus.Dead && p.DepartureReason == DepartureReason.None)
                {
                    p.Departed = true;
                    p.DepartureReason = DepartureReason.Died;
                }
                p.Record.DaysInSpace = c.GetDouble("daysInSpace", 0);
                p.PersonalityTags.AddRange(c.GetValues("tag"));
                foreach (string a in c.GetValues("achievement")) p.Record.Achievements.Add(a);
                p.Record.Quotes.AddRange(c.GetValues("quote"));

                foreach (DataNode n in c.GetChildren("MISSION"))
                {
                    p.Record.Missions.Add(new MissionEntry
                    {
                        MissionId = n.GetValue("id", string.Empty),
                        Name = n.GetValue("name", string.Empty),
                        StartDay = n.GetInt("startDay", 0),
                        EndDay = n.GetInt("endDay", -1),
                        Outcome = ParseEnum(n.GetValue("outcome"), MissionOutcome.InProgress),
                        DirectorFlown = n.GetBool("directorFlown", false)
                    });
                }
                foreach (DataNode n in c.GetChildren("MEDAL"))
                {
                    p.Record.Medals.Add(new MedalAward
                    {
                        MedalId = n.GetValue("id", string.Empty),
                        Day = n.GetInt("day", 0),
                        MissionId = n.GetValue("mission", string.Empty),
                        Note = n.GetValue("note", string.Empty)
                    });
                }
                foreach (DataNode n in c.GetChildren("INCIDENT"))
                {
                    p.Record.Incidents.Add(new IncidentEntry
                    {
                        Kind = ParseEnum(n.GetValue("kind"), IncidentKind.Other),
                        Day = n.GetInt("day", 0),
                        MissionId = n.GetValue("mission", string.Empty),
                        Description = n.GetValue("description", string.Empty)
                    });
                }
                agency.Crew.Add(p);
            }
        }

        private static Applicant ReadApplicant(DataNode n)
        {
            var a = new Applicant
            {
                Id = n.GetValue("id", string.Empty),
                Name = n.GetValue("name", string.Empty),
                Role = ParseEnum(n.GetValue("role"), CrewRole.Unknown),
                Stars = n.GetInt("stars", 0),
                Courage = (float)n.GetDouble("courage", 0.5),
                Stupidity = (float)n.GetDouble("stupidity", 0.5),
                SourceId = n.GetValue("source", string.Empty),
                ArrivedDay = n.GetInt("arrivedDay", 0),
                Interviews = n.GetInt("interviews", 0),
                ReturnDay = n.GetInt("returnDay", -1),
                HireFee = n.GetDouble("hireFee", 0)
            };
            a.Tags.AddRange(n.GetValues("tag"));
            a.Achievements.AddRange(n.GetValues("achievement"));
            foreach (string r in n.GetValues("revealed")) a.Revealed.Add(r);
            return a;
        }

        private static T ParseEnum<T>(string text, T fallback) where T : struct
        {
            T value;
            return text != null && Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
        }
    }
}
