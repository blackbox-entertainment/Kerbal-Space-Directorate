using System;
using System.Collections.Generic;
using KSA.Core.Careers;
using KSA.Core.Data;
using KSA.Core.Finance;
using KSA.Core.Goals;
using KSA.Core.Medals;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;
using KSA.Core.Review;

namespace KSA.Core
{
    /// <summary>What a roster sync changed, for logging.</summary>
    public sealed class SyncResult
    {
        public List<CrewProfile> Hired { get; } = new List<CrewProfile>();
        public List<CrewProfile> Departed { get; } = new List<CrewProfile>();
        public List<string> Notices { get; } = new List<string>();
    }

    /// <summary>
    /// The agency itself: owns the crew registry, the books, the calendar, goals, and the applicant pool, and applies
    /// every rule to the events the host reports. Platform-free. The host (KSA.KSP) feeds it roster snapshots,
    /// mission events, a clock, names, and persistence, and acts on the events it raises.
    /// </summary>
    public sealed partial class Agency
    {
        public const int MinHireAge = 24;
        public const int MaxHireAge = 38;

        public AgencyCatalogs Catalogs { get; }
        public CrewRegistry Crew { get; }
        public CareerCatalog Careers { get { return Catalogs.Careers; } }
        public MedalCatalog Medals { get { return Catalogs.Medals; } }
        public AgencySettings Settings { get { return Catalogs.Settings; } }
        public PromotionEngine Promotions { get; }
        public MedalEvaluator MedalRules { get; }
        public IClock Clock { get; }
        public ILogger Log { get; }

        public Ledger Ledger { get; }
        public FiscalCalendar Calendar { get; }
        public AgencyState State { get; }
        public List<GoalState> ActiveGoals { get; } = new List<GoalState>();
        public List<GoalState> GoalHistory { get; } = new List<GoalState>();
        public List<ReviewResult> Reviews { get; } = new List<ReviewResult>();
        public ApplicantPool Applicants { get; }
        public Dictionary<string, int> ManualAwardsThisYear { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Supplies applicant names. The host sets this; tests use a counter. Null falls back to generic names.</summary>
        public INameSource Names { get; set; }
        public Random Rng { get; set; }

        /// <summary>Counter for host-independent mission ids.</summary>
        public int NextMissionNumber { get; set; }

        /// <summary>Raised for every human-readable change (promotion, medal, hire, review). The host shows or logs it.</summary>
        public event Action<string> Notice;
        /// <summary>A crew member left (quit, retired, fired). The host removes them from its own roster.</summary>
        public event Action<CrewProfile, DepartureReason> CrewDeparted;
        /// <summary>An applicant was hired. The host creates the real crew member with this name and traits.</summary>
        public event Action<Applicant, CrewProfile> ApplicantHired;
        /// <summary>A fiscal year closed.</summary>
        public event Action<ReviewResult> YearEnded;

        public Agency(AgencyCatalogs catalogs, IClock clock, ILogger log)
        {
            if (catalogs == null) throw new ArgumentNullException(nameof(catalogs));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (log == null) throw new ArgumentNullException(nameof(log));
            catalogs.Validate();
            Catalogs = catalogs;
            Clock = clock;
            Log = log;
            Crew = new CrewRegistry();
            Promotions = new PromotionEngine(catalogs.Careers, Crew);
            MedalRules = new MedalEvaluator(catalogs.Medals, Crew);
            catalogs.Morale.Personality = catalogs.Settings != null ? catalogs.Settings.Personality : null;
            Ledger = new Ledger();
            Calendar = new FiscalCalendar(clock.DaysPerYear);
            State = new AgencyState { Prestige = catalogs.Settings.StartingPrestige };
            Applicants = new ApplicantPool();
            Rng = new Random();
            NextMissionNumber = 1;
            InitNarrative();
        }

        public int CurrentDay { get { return Clock.CurrentDay(); } }
        public int CurrentYear { get { return State.Started ? State.CurrentYear : Calendar.YearOf(CurrentDay); } }
        public int DayOfYear { get { return Calendar.DayOfYear(CurrentDay); } }

        // ------------------------------------------------------------------ lifecycle

        /// <summary>Opens the books on first use: founding balance, first grant, first goals.</summary>
        public List<string> StartIfNeeded()
        {
            var notices = new List<string>();
            if (State.Started) return notices;
            int day = CurrentDay;
            State.Started = true;
            State.CurrentYear = Calendar.YearOf(day);
            if (Ledger.Mirror != null && Ledger.Mirror.IsActive) Ledger.SyncFromMirror();
            else Ledger.SetBalance(Settings.StartingBalance);
            double grant = Math.Round(Settings.BaseGrant * PrestigeFactor() * GrantFactor());
            Ledger.Deposit(grant, LedgerCategory.Grant, "Government grant, year " + State.CurrentYear, day, State.CurrentYear);
            State.YearStartBalance = Ledger.Balance;
            Push(notices, "Fiscal year " + State.CurrentYear + " opens. Grant: " + Money(grant));
            IssueGoals(notices);
            return notices;
        }

        /// <summary>
        /// Advances agency time to the clock. Call often (scene start, every few seconds). Rolls any fiscal years that
        /// have ended since the last call, refreshes applicants quarterly. Returns notices.
        /// </summary>
        public List<string> Tick()
        {
            var notices = new List<string>();
            KSA.Core.Flight.CrewDecider.Tick(Clock.UniversalTime); // overdue crew judgement calls fall back to the trait rule
            notices.AddRange(StartIfNeeded());
            ResumeInterruptedDesigns(notices);
            int day = CurrentDay;
            int guard = 0;
            while (Calendar.YearOf(day) > State.CurrentYear && guard++ < 100)
                RunYearEnd(notices);
            TickRecruiting(day, notices);
            TickProjects(day, notices);
            TickLab(day, notices); // lab staff earn experience day by day (2026-10-28)
            if (LifeSupportOn) StrandedSuppliesTick(notices); // KSA-67: with life support the stranded run out in days, not years
            DirectorTick(); // KSA-68: the AI Director takes a turn when one is due
            return notices;
        }

        /// <summary>Closes the current fiscal year right now, regardless of the clock. Used by tests and the debug menu.</summary>
        public List<string> ForceYearEnd()
        {
            var notices = new List<string>();
            notices.AddRange(StartIfNeeded());
            RunYearEnd(notices);
            return notices;
        }

        private void RunYearEnd(List<string> notices)
        {
            int year = State.CurrentYear;
            int day = Math.Max(CurrentDay, Calendar.FirstDayOfYear(year + 1) - 1);

            // 1. Resolve goals.
            int issued = ActiveGoals.Count, met = 0;
            var ctx = BuildGoalContext();
            var metNames = new List<string>();
            var missedNames = new List<string>();
            var carried = new List<GoalState>();
            foreach (GoalState gs in ActiveGoals)
            {
                GoalDefinition def = Catalogs.Goals.Get(gs.GoalId);
                bool ok = def != null && GoalEvaluator.IsMet(def, ctx);
                if (ok)
                {
                    gs.Status = GoalStatus.Met; gs.YearResolved = year; met++;
                    metNames.Add(def.Name);
                    GoalHistory.Add(gs);
                }
                else if (def != null && def.MultiYear)
                {
                    carried.Add(gs); // not counted against the agency, stays active
                    issued--;
                }
                else
                {
                    gs.Status = GoalStatus.Missed; gs.YearResolved = year;
                    missedNames.Add(def != null ? def.Name : gs.GoalId);
                    GoalHistory.Add(gs);
                }
            }
            ActiveGoals.Clear();
            ActiveGoals.AddRange(carried);

            // 2. The review.
            int activeCount = 0;
            foreach (CrewProfile p in Crew.Active) activeCount++;
            ReviewResult r = GovernmentReview.Evaluate(year, issued, met, State.DeathsThisYear, State.Probation, State.Prestige,
                                                       Settings.BaseGrant, activeCount, Settings.CrisisLayoffFraction);
            r.MetGoals.AddRange(metNames);
            r.MissedGoals.AddRange(missedNames);
            Reviews.Add(r);

            // 3. Salaries.
            double salaries = 0;
            foreach (CrewProfile p in Crew.Active) salaries += p.Salary;
            double salaryFactor = SalaryFactor();
            if (salaryFactor < 0.999) { double saved = salaries * (1 - salaryFactor); salaries -= saved; Push(notices, "Head of Finances trimmed the payroll by " + Money(saved)); }
            if (salaries > 0)
            {
                Ledger.Withdraw(salaries, LedgerCategory.Salary, "Salaries, year " + year, day, year, true);
                Push(notices, "Salaries paid for year " + year + ": " + Money(salaries));
            }

            // 4. Apply the verdict.
            State.Prestige = r.PrestigeAfter;
            if (r.PrestigeAfter > r.PrestigeBefore && PrestigeGainFactor() > 1) { double extra = (r.PrestigeAfter - r.PrestigeBefore) * (PrestigeGainFactor() - 1); State.Prestige = Math.Min(100, State.Prestige + extra); r.PrestigeAfter = State.Prestige; }
            DepartmentYearNotes(notices);
            SupplyYearNotes(notices);
            ColonyYearEnd(notices);
            StrandedYearEnd(notices);
            TickLab(day, notices);
            AwardLabScience(notices);
            int moraleBonus = YearEndMoraleBonus();
            if (moraleBonus > 0) foreach (CrewProfile p in Crew.Active) p.Morale = Math.Min(100, p.Morale + moraleBonus);
            State.HiringFreeze = r.HiringFreeze;
            State.Probation = r.ProbationAfter;
            State.PendingLayoffs = r.Layoffs;
            if (r.DirectorReplaced) State.DirectorNumber++;
            MoraleEvent yearEvent = r.Band == ReviewBand.Crisis ? MoraleEvent.CrisisYear
                                  : r.Band == ReviewBand.Lean ? MoraleEvent.LeanYear
                                  : r.Band <= ReviewBand.Good ? MoraleEvent.GoodYear : MoraleEvent.Idle;
            foreach (CrewProfile p in Crew.Active)
            {
                if (yearEvent != MoraleEvent.Idle) Catalogs.Morale.Apply(p, yearEvent);
                if (p.LastMissionYear != year && !p.IsFlying) { if (!Personality.IgnoresIdle(p)) Catalogs.Morale.Apply(p, MoraleEvent.Idle); DriftAfterIdleYear(p); }
                if (Promotions.PendingSelection(p) != null)
                {
                    Catalogs.Morale.Apply(p, MoraleEvent.PassedOver);
                    Push(notices, p.Name + " was passed over for astronaut selection");
                }
            }
            ClaimPlayerFirsts(notices);
            RecordYear(year, r);
            RequestDirectorTurn("the budget review for year " + year + " just closed");
            TickRivals(year, notices);
            Push(notices, "Government Review, year " + year + ": " + r.BandText + " (" + met + "/" + issued + " goals). Budget " + (r.BudgetChangePercent >= 0 ? "+" : "") + r.BudgetChangePercent + "%");
            foreach (string e in r.Events) Push(notices, e);

            // 5. Departures: retirements and resignations.
            var leaving = new List<KeyValuePair<CrewProfile, DepartureReason>>();
            foreach (CrewProfile p in Crew.Active)
            {
                if (p.Status == CrewStatus.Dead || p.IsFlying) continue;
                int age = p.AgeOn(day, Calendar.DaysPerYear);
                int service = (day - p.HiredDay) / Calendar.DaysPerYear;
                if (age >= Settings.RetireAge || service >= Settings.RetireServiceYears)
                {
                    leaving.Add(new KeyValuePair<CrewProfile, DepartureReason>(p, DepartureReason.Retired));
                    continue;
                }
                if (p.Morale < Settings.QuitMorale)
                {
                    p.LowMoraleYears++;
                    if (p.LowMoraleYears >= Settings.QuitYears)
                        leaving.Add(new KeyValuePair<CrewProfile, DepartureReason>(p, DepartureReason.Quit));
                }
                else p.LowMoraleYears = 0;
            }
            foreach (var kv in leaving) Depart(kv.Key, kv.Value, day, notices);

            // 6. Next year.
            State.CurrentYear = year + 1;
            State.DeathsThisYear = 0;
            State.MissionsThisYear = 0;
            ManualAwardsThisYear.Clear();
            r.NextGrant = Math.Round(r.NextGrant * GrantFactor());
            Ledger.Deposit(r.NextGrant, LedgerCategory.Grant, "Government grant, year " + State.CurrentYear, day + 1, State.CurrentYear);
            State.YearStartBalance = Ledger.Balance;
            Push(notices, "Fiscal year " + State.CurrentYear + " opens. Grant: " + Money(r.NextGrant));
            IssueGoals(notices);

            Action<ReviewResult> handler = YearEnded;
            if (handler != null) handler(r);
        }

        private void IssueGoals(List<string> notices)
        {
            var metIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GoalState g in GoalHistory) if (g.Status == GoalStatus.Met) metIds.Add(g.GoalId);
            var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GoalState g in ActiveGoals) activeIds.Add(g.GoalId);

            int want = Settings.GoalsPerYear - ActiveGoals.Count;
            List<GoalDefinition> picked = GoalSelector.Pick(Catalogs.Goals, want, HasAgencyAchievement, metIds, activeIds, Rng, GoalFeasible);
            foreach (GoalDefinition g in picked)
            {
                ActiveGoals.Add(new GoalState { GoalId = g.Id, YearIssued = State.CurrentYear, Status = GoalStatus.Active });
                Push(notices, "Program goal: " + g.Name);
            }
        }

        private GoalContext BuildGoalContext()
        {
            return new GoalContext
            {
                HasAchievement = HasAgencyAchievement,
                MissionsThisYear = State.MissionsThisYear,
                DeathsThisYear = State.DeathsThisYear,
                UnderBudget = Ledger.Balance >= State.YearStartBalance
            };
        }

        /// <summary>True if anyone who ever worked here holds the achievement.</summary>
        public bool HasAgencyAchievement(string pattern)
        {
            foreach (CrewProfile p in Crew.All)
                if (p.Record.HasAchievement(pattern)) return true;
            return false;
        }

        /// <summary>Live status of an active goal, for the UI.</summary>
        public bool IsGoalCurrentlyMet(GoalState gs)
        {
            GoalDefinition def = gs != null ? Catalogs.Goals.Get(gs.GoalId) : null;
            return def != null && GoalEvaluator.IsMet(def, BuildGoalContext());
        }

        /// <summary>Why a goal can no longer be met this year (a crew was lost), or null while it still can.</summary>
        public string GoalFailure(GoalState gs)
        {
            GoalDefinition def = gs != null ? Catalogs.Goals.Get(gs.GoalId) : null;
            if (def == null) return null;
            if (def.Condition == GoalConditionType.NoCrewLoss && State.DeathsThisYear > 0)
                return State.DeathsThisYear + " kerbal" + (State.DeathsThisYear == 1 ? "" : "s") + " lost this year";
            return null;
        }

        /// <summary>"[met]", "[failed: reason]" or "[open]", for windows and the AI Director's briefing.</summary>
        public string GoalStatusText(GoalState gs)
        {
            string failed = GoalFailure(gs);
            if (failed != null) return "[failed: " + failed + "]";
            return IsGoalCurrentlyMet(gs) ? "[met]" : "[open]";
        }

        private double PrestigeFactor() { return 0.75 + State.Prestige / 200.0; }

        // ------------------------------------------------------------------ roster

        /// <summary>
        /// Reconciles the registry with the host's current roster: creates profiles for new hires, refreshes mirrored
        /// stats, marks anyone missing from the roster as departed, then applies medal and promotion rules.
        /// </summary>
        public SyncResult SyncRoster(IEnumerable<CrewSnapshot> snapshots)
        {
            var result = new SyncResult();
            if (snapshots == null) return result;
            int day = CurrentDay;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (CrewSnapshot s in snapshots)
            {
                if (s == null) continue;
                seen.Add(s.Name);
                CrewProfile p = Crew.Get(s.Name);
                if (p == null)
                {
                    p = new CrewProfile(s.Name, s.Name)
                    {
                        HiredDay = day,
                        BornDay = day - HireAgeFor(s.Name) * Clock.DaysPerYear
                    };
                    Crew.Add(p);
                    result.Hired.Add(p);
                }
                else if (p.Departed && p.DepartureReason != DepartureReason.Died)
                {
                    // Came back (re-added by the host). Keep the record.
                    p.Departed = false;
                    p.DepartureReason = DepartureReason.None;
                }
                else if (p.Departed) continue; // dead stays dead, whatever the game's roster says (stock respawns the missing)

                bool fresh = result.Hired.Contains(p);
                p.Role = s.Role;
                p.Female = s.Female;
                p.Stars = Math.Max(s.Stars, LadderStars(p)); // the game's level is a floor; the KSA ladder can only add (KSA-63)
                p.Status = s.Status;
                if (fresh || !p.TraitsOwned)
                {
                    // The game sets the starting traits; after that the agency owns them (they drift with experience) and
                    // pushes changes back to the host through TraitsChanged.
                    p.Courage = s.Courage;
                    p.Stupidity = s.Stupidity;
                }
                if (!string.IsNullOrEmpty(s.PortraitKey)) p.PortraitKey = s.PortraitKey;
                for (int i = 0; i < s.Achievements.Count; i++) p.Record.Achievements.Add(s.Achievements[i]);

                if (Promotions.AssignStartingRank(p))
                    Push(result.Notices, p.Name + " hired as " + Careers.RankName(p.Track, p.RankId));
            }

            foreach (CrewProfile p in Crew.All)
            {
                if (!seen.Contains(p.Id) && !p.Departed)
                {
                    p.Departed = true;
                    p.DepartedDay = day;
                    if (p.DepartureReason == DepartureReason.None) p.DepartureReason = DepartureReason.Fired;
                    result.Departed.Add(p);
                    Push(result.Notices, p.Name + " has left the agency");
                }
            }

            foreach (CrewProfile p in Crew.Active)
                EvaluateProgress(p, null, result.Notices);

            return result;
        }

        // ------------------------------------------------------------------ missions

        /// <summary>Opens a mission entry for each crew member and marks them as flying. Returns the mission id.</summary>
        public string BeginMission(IEnumerable<string> crewIds, string missionName, string missionId = null)
        {
            string id = string.IsNullOrEmpty(missionId) ? "M" + NextMissionNumber++ : missionId;
            int day = CurrentDay;
            if (crewIds == null) return id;
            foreach (string crewId in crewIds)
            {
                CrewProfile p = Crew.Get(crewId);
                if (p == null) continue;
                if (p.Record.CurrentMission != null && p.Record.CurrentMission.MissionId == id) continue;
                p.Record.Missions.Add(new MissionEntry { MissionId = id, Name = missionName ?? id, StartDay = day, Outcome = MissionOutcome.InProgress });
                p.InFlightSinceUT = Clock.UniversalTime;
                p.Status = CrewStatus.Assigned;
            }
            return id;
        }

        /// <summary>Closes the crew member's current mission, books their time in space, and applies rules.</summary>
        public List<string> EndMission(string crewId, MissionOutcome outcome)
        {
            var notices = new List<string>();
            CrewProfile p = Crew.Get(crewId);
            if (p == null) return notices;

            MissionEntry m = p.Record.CurrentMission;
            if (m != null)
            {
                m.Outcome = outcome;
                m.EndDay = CurrentDay;
            }
            BookFlightTime(p);
            if (p.Status == CrewStatus.Assigned && outcome != MissionOutcome.LossOfCrew) p.Status = CrewStatus.Available;

            if (outcome == MissionOutcome.Success || outcome == MissionOutcome.PartialSuccess)
            {
                State.MissionsThisYear++;
                p.LastMissionYear = CurrentYear;
                Catalogs.Morale.Apply(p, outcome == MissionOutcome.Success ? MoraleEvent.MissionSuccess : MoraleEvent.MissionPartial);
            }
            else if (outcome == MissionOutcome.Abort || outcome == MissionOutcome.LossOfVehicle)
            {
                Catalogs.Morale.Apply(p, MoraleEvent.MissionFailure);
                int extraHit = Personality.MoraleOnFailure(p);
                if (extraHit != 0) p.Morale = Math.Max(0, Math.Min(100, p.Morale + extraHit));
            }

            EvaluateProgress(p, m != null ? m.MissionId : null, notices);
            return notices;
        }

        public void RecordIncident(string crewId, IncidentKind kind, string description)
        {
            CrewProfile p = Crew.Get(crewId);
            if (p == null) return;
            MissionEntry m = p.Record.CurrentMission;
            p.Record.Incidents.Add(new IncidentEntry
            {
                Kind = kind,
                Day = CurrentDay,
                MissionId = m != null ? m.MissionId : string.Empty,
                Description = description ?? string.Empty
            });
        }

        /// <summary>Marks the crew member dead, closes their mission as loss of crew, and awards posthumous medals.</summary>
        public List<string> RecordDeath(string crewId, string description)
        {
            var notices = new List<string>();
            CrewProfile p = Crew.Get(crewId);
            if (p == null) return notices;
            if (p.Departed && p.DepartureReason == DepartureReason.Died) return notices; // already recorded: no second morale blow

            RecordIncident(crewId, IncidentKind.Death, description);
            p.Status = CrewStatus.Dead;
            p.Departed = true;
            VacatePostsOf(p.Id);
            p.DepartureReason = DepartureReason.Died;
            p.DepartedDay = CurrentDay;
            MissionEntry m = p.Record.CurrentMission;
            if (m != null)
            {
                m.Outcome = MissionOutcome.LossOfCrew;
                m.EndDay = CurrentDay;
            }
            BookFlightTime(p);
            State.DeathsThisYear++;
            int survivors = ActiveCountExcluding(p);
            foreach (CrewProfile other in Crew.Active)
                if (other != p) Catalogs.Morale.ApplyScaled(other, MoraleEvent.CrewDeath, survivors);
            DriftAfterDeath(p);
            int blow = -Catalogs.Morale.ScaledDelta(MoraleEvent.CrewDeath, survivors);
            Push(notices, p.Name + " was killed in the line of duty" + (blow > 0 ? "; morale across the agency fell by " + blow : string.Empty));
            EvaluateProgress(p, m != null ? m.MissionId : null, notices);
            return notices;
        }

        /// <summary>Merges host achievements for one crew member (e.g. after an EVA) and re-applies rules.</summary>
        public List<string> AddAchievements(string crewId, IEnumerable<string> achievements)
        {
            var notices = new List<string>();
            CrewProfile p = Crew.Get(crewId);
            if (p == null || achievements == null) return notices;
            foreach (string a in achievements) p.Record.Achievements.Add(a);
            MissionEntry m = p.Record.CurrentMission;
            EvaluateProgress(p, m != null ? m.MissionId : null, notices);
            return notices;
        }

        private void BookFlightTime(CrewProfile p)
        {
            if (!p.IsFlying) return;
            double seconds = Clock.UniversalTime - p.InFlightSinceUT;
            if (seconds > 0 && Clock.SecondsPerDay > 0) p.Record.DaysInSpace += seconds / Clock.SecondsPerDay;
            p.InFlightSinceUT = -1;
        }

        // ------------------------------------------------------------------ director decisions

        /// <summary>The Director grants a pending Director-selected rank (e.g. Test Pilot to Astronaut Candidate).</summary>
        public RankDefinition SelectForRank(string crewId)
        {
            CrewProfile p = Crew.Get(crewId);
            if (p == null) return null;
            RankDefinition r = Promotions.Select(p);
            if (r != null)
            {
                Catalogs.Morale.Apply(p, MoraleEvent.OwnPromotion);
                Push(null, p.Name + " selected as " + r.Name);
            }
            return r;
        }

        /// <summary>Dismisses a crew member. Counts toward pending layoffs. Colleagues lose a little morale.</summary>
        public bool Fire(string crewId, out string message)
        {
            CrewProfile p = Crew.Get(crewId);
            if (p == null || p.Departed) { message = "No such active crew member."; return false; }
            if (p.IsFlying) { message = p.Name + " is on a mission and cannot be dismissed now."; return false; }
            var notices = new List<string>();
            Depart(p, DepartureReason.Fired, CurrentDay, notices);
            if (State.PendingLayoffs > 0) State.PendingLayoffs--;
            int remaining = ActiveCountExcluding(p);
            foreach (CrewProfile other in Crew.Active) if (other != p) Catalogs.Morale.ApplyScaled(other, MoraleEvent.ColleagueFired, remaining);
            int hit = -Catalogs.Morale.ScaledDelta(MoraleEvent.ColleagueFired, remaining);
            message = p.Name + " has been dismissed." + (hit > 0 ? " Morale across the agency fell by " + hit + "." : string.Empty) + (State.PendingLayoffs > 0 ? " Layoffs still required: " + State.PendingLayoffs : string.Empty);
            return true;
        }

        /// <summary>How many active kerbals are left once <paramref name="gone"/> is out: the roster that feels a loss.</summary>
        private int ActiveCountExcluding(CrewProfile gone)
        {
            int n = 0;
            foreach (CrewProfile c in Crew.Active) if (c != gone) n++;
            return n;
        }

        private void Depart(CrewProfile p, DepartureReason reason, int day, List<string> notices)
        {
            VacatePostsOf(p.Id);
            p.Departed = true;
            p.DepartureReason = reason;
            p.DepartedDay = day;
            p.InFlightSinceUT = -1;
            string verb = reason == DepartureReason.Retired ? "has retired" : reason == DepartureReason.Quit ? "has resigned" : "has been dismissed";
            Push(notices, p.Name + " " + verb + " (" + Careers.RankName(p.Track, p.RankId) + ")");
            Action<CrewProfile, DepartureReason> handler = CrewDeparted;
            if (handler != null) handler(p, reason);
        }

        /// <summary>How many more of a Director-awarded medal can be given this year (-1 = unlimited).</summary>
        public int ManualAwardsRemaining(string medalId)
        {
            MedalDefinition m = Medals.Get(medalId);
            if (m == null || m.Trigger != MedalTrigger.Manual) return 0;
            if (m.PerYearLimit <= 0) return -1;
            int used;
            ManualAwardsThisYear.TryGetValue(medalId, out used);
            return Math.Max(0, m.PerYearLimit - used);
        }

        /// <summary>The Director awards a Manual medal (e.g. Chief's Commendation), subject to the yearly limit.</summary>
        public bool AwardManual(string crewId, string medalId, string note, out string message)
        {
            CrewProfile p = Crew.Get(crewId);
            MedalDefinition m = Medals.Get(medalId);
            if (p == null || m == null) { message = "Unknown crew member or medal."; return false; }
            if (m.Trigger != MedalTrigger.Manual) { message = m.Name + " is not a Director-awarded medal."; return false; }
            if (p.Record.HasMedal(medalId)) { message = p.Name + " already holds the " + m.Name + "."; return false; }
            int remaining = ManualAwardsRemaining(medalId);
            if (remaining == 0) { message = "No more " + m.Name + " awards available this year."; return false; }

            p.Record.Medals.Add(new MedalAward { MedalId = medalId, Day = CurrentDay, Note = note ?? string.Empty });
            int used;
            ManualAwardsThisYear.TryGetValue(medalId, out used);
            ManualAwardsThisYear[medalId] = used + 1;
            Catalogs.Morale.Apply(p, MoraleEvent.Commendation);
            message = p.Name + " awarded the " + m.Name + ".";
            Push(null, message);
            return true;
        }

        // ------------------------------------------------------------------ recruiting

        public bool HiringAllowed(out string reason)
        {
            if (State.HiringFreeze) { reason = "Hiring is frozen until the next Government Review."; return false; }
            if (State.PendingLayoffs > 0) { reason = "The government requires " + State.PendingLayoffs + " layoff(s) before any hiring."; return false; }
            reason = string.Empty;
            return true;
        }

        /// <summary>Spends on a recruitment drive from one source and fills the pool.</summary>
        public bool RunDrive(string sourceId, out string message)
        {
            RecruitSource src = Catalogs.Recruiting.Get(sourceId);
            if (src == null) { message = "Unknown recruitment source."; return false; }
            RefreshFunds();
            if (!Ledger.CanAfford(src.DriveCost)) { message = "Not enough funds for a " + src.Name + " drive (" + Money(src.DriveCost) + ")."; return false; }
            if (Applicants.Applicants.Count >= Settings.MaxApplicants) { message = "The applicant pool is full. Interview or reject some applicants first."; return false; }

            int day = CurrentDay;
            Ledger.Withdraw(src.DriveCost, LedgerCategory.Recruiting, "Recruitment drive: " + src.Name, day, CurrentYear);
            int room = Settings.MaxApplicants - Applicants.Applicants.Count;
            var made = Applicants.Generate(src, Math.Min(room, src.ApplicantsPerDrive), State.Prestige, day, Rng, Names, Settings.PersonalityTags, TakenNames());
            message = src.Name + " drive: " + made.Count + " applicant" + (made.Count == 1 ? "" : "s") + " for " + Money(src.DriveCost) + ".";
            Push(null, message);
            return true;
        }

        /// <summary>What the applicant's next interview costs: base + step per interview already held.</summary>
        public double InterviewCost(Applicant a)
        {
            int held = a != null ? a.Interviews : 0;
            double first = Math.Max(Settings.InterviewBaseCost, a != null ? a.HireFee * Settings.InterviewFeeFraction : 0);
            return Math.Max(0, Math.Round(first + Settings.InterviewCostStep * held));
        }

        /// <summary>In Career the stock Funds can change between ticks (VAB, launches); re-read them before spending.</summary>
        private void RefreshFunds()
        {
            if (Ledger.Mirror != null && Ledger.Mirror.IsActive && State.Started) Ledger.SyncFromMirror();
        }

        /// <summary>
        /// A paid interview reveals more about an applicant (decision 2026-10-27: 200 funds, +100 each further round).
        /// Senior staff on the panel reveal two facts per interview, so they halve the bill. Hiring blind is always allowed.
        /// </summary>
        public bool Interview(string applicantId, out string message)
        {
            Applicant a = Applicants.Get(applicantId);
            if (a == null) { message = "No such applicant."; return false; }
            if (a.FullyRevealed) { message = "Nothing more to learn about " + a.Name + "."; return false; }
            double cost = InterviewCost(a);
            RefreshFunds();
            if (!Ledger.CanAfford(cost)) { message = "Not enough funds to interview " + a.Name + " (" + Money(cost) + ")."; return false; }
            Ledger.Withdraw(cost, LedgerCategory.Recruiting, "Interview: " + a.Name, CurrentDay, CurrentYear);
            Applicants.Interview(applicantId, PanelBonus());
            message = "Interviewed " + a.Name + " for " + Money(cost) + (PanelBonus() > 0 ? " (senior panel)." : ".");
            return true;
        }

        public bool Reject(string applicantId, out string message)
        {
            Applicant a = Applicants.Reject(applicantId, CurrentDay, Calendar.DaysPerYear);
            if (a == null) { message = "No such applicant."; return false; }
            message = a.Name + " was turned down. They may reapply next year.";
            return true;
        }

        /// <summary>
        /// Hires an applicant: charges the fee, opens a profile with their tags and prior experience, and asks the host
        /// (via ApplicantHired) to create the real crew member. The next roster sync fills in the mirrored stats.
        /// </summary>
        public bool Hire(string applicantId, out string message)
        {
            Applicant a = Applicants.Get(applicantId);
            if (a == null) { message = "No such applicant."; return false; }
            string why;
            if (!HiringAllowed(out why)) { message = why; return false; }
            if (!Ledger.CanAfford(a.HireFee)) { message = "Not enough funds to hire " + a.Name + " (" + Money(a.HireFee) + ")."; return false; }
            if (Crew.Get(a.Name) != null && !Crew.Get(a.Name).Departed) { message = "Someone named " + a.Name + " is already on staff."; return false; }

            int day = CurrentDay;
            Ledger.Withdraw(a.HireFee, LedgerCategory.Hiring, "Hired " + a.Name, day, CurrentYear);
            Applicants.Remove(applicantId);

            CrewProfile p = Crew.Get(a.Name);
            if (p == null)
            {
                p = new CrewProfile(a.Name, a.Name);
                Crew.Add(p);
            }
            p.Departed = false;
            p.DepartureReason = DepartureReason.None;
            p.Role = a.Role;
            p.Stars = a.Stars;
            p.Courage = a.Courage;
            p.Stupidity = a.Stupidity;
            p.Status = CrewStatus.Available;
            p.HiredDay = day;
            p.BornDay = day - HireAgeFor(a.Name) * Clock.DaysPerYear;
            p.PersonalityTags.Clear();
            p.PersonalityTags.AddRange(a.Tags);
            foreach (string ach in a.Achievements) p.Record.Achievements.Add(ach);
            Promotions.AssignStartingRank(p);

            message = "Hired " + a.Name + " as " + Careers.RankName(p.Track, p.RankId) + " for " + Money(a.HireFee) + ".";
            Push(null, message);
            Action<Applicant, CrewProfile> handler = ApplicantHired;
            if (handler != null) handler(a, p);
            return true;
        }

        /// <summary>1 when a senior crew member (third rung or higher on any track) can sit on the interview panel.</summary>
        public int PanelBonus()
        {
            foreach (CrewProfile p in Crew.Active)
            {
                CareerTrackDefinition t = Careers.GetTrack(p.Track);
                if (t != null && t.IndexOf(p.RankId) >= 2) return 1;
            }
            return 0;
        }

        private void TickRecruiting(int day, List<string> notices)
        {
            int returned = Applicants.Refresh(day);
            if (returned > 0) Push(notices, returned + " former applicant" + (returned == 1 ? "" : "s") + " reapplied.");

            int quarterLen = Math.Max(1, Calendar.DaysPerYear / 4);
            int quarter = Calendar.DayOfYear(day) / quarterLen;
            int year = Calendar.YearOf(day);
            if (State.LastWalkInYear == year && State.LastWalkInQuarter == quarter) return;
            State.LastWalkInYear = year;
            State.LastWalkInQuarter = quarter;

            if (Applicants.Applicants.Count >= Settings.MaxApplicants || Settings.WalkInsPerQuarter <= 0) return;
            RecruitSource src = PickWalkInSource();
            if (src == null) return;
            var made = Applicants.Generate(src, Settings.WalkInsPerQuarter, State.Prestige, day, Rng, Names, Settings.PersonalityTags, TakenNames());
            foreach (Applicant a in made) Push(notices, "Walk-in applicant: " + a.Name + " (" + RosterView.RoleText(a.Role) + ")");
            if (made.Count > 0) RequestDirectorTurn("a walk-in applicant arrived");
        }

        private RecruitSource PickWalkInSource()
        {
            int total = 0;
            foreach (RecruitSource s in Catalogs.Recruiting.Sources) total += Math.Max(0, s.WalkInWeight);
            if (total <= 0) return null;
            int roll = Rng.Next(total);
            foreach (RecruitSource s in Catalogs.Recruiting.Sources)
            {
                roll -= Math.Max(0, s.WalkInWeight);
                if (roll < 0) return s;
            }
            return null;
        }

        private HashSet<string> TakenNames()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CrewProfile p in Crew.All) taken.Add(p.Name);
            foreach (Applicant a in Applicants.Applicants) taken.Add(a.Name);
            foreach (Applicant a in Applicants.Returning) taken.Add(a.Name);
            return taken;
        }

        // ------------------------------------------------------------------ persistence

        public DataNode ExportState() { return AgencyStateSerializer.Write(this); }

        public void ImportState(DataNode state) { AgencyStateSerializer.Read(state, this); }

        public void Save(IPersistence persistence)
        {
            if (persistence == null) throw new ArgumentNullException(nameof(persistence));
            persistence.SaveState(ExportState());
        }

        /// <summary>Loads saved state if any. Returns false for a fresh game or a save made before KSA existed.</summary>
        public bool Load(IPersistence persistence)
        {
            if (persistence == null) throw new ArgumentNullException(nameof(persistence));
            DataNode state = persistence.LoadState();
            if (state == null) return false;
            ImportState(state);
            return true;
        }

        // ------------------------------------------------------------------ internals

        private void EvaluateProgress(CrewProfile p, string missionId, List<string> notices)
        {
            foreach (MedalDefinition medal in MedalRules.Evaluate(p, CurrentDay, missionId))
            {
                Catalogs.Morale.Apply(p, MoraleEvent.OwnMedal);
                Push(notices, p.Name + " awarded " + medal.Name);
            }
            foreach (RankDefinition rank in Promotions.PromoteAutomatically(p))
            {
                Catalogs.Morale.Apply(p, MoraleEvent.OwnPromotion);
                Push(notices, p.Name + " promoted to " + rank.Name);
            }
        }

        private void Push(List<string> notices, string message)
        {
            if (notices != null) notices.Add(message);
            Log.Info(message);
            Action<string> handler = Notice;
            if (handler != null) handler(message);
        }

        public static string Money(double amount)
        {
            return amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " funds";
        }

        /// <summary>Deterministic hire age from the crew id, so a given kerbal is the same age on every reload.</summary>
        public static int HireAgeFor(string id)
        {
            uint hash = 2166136261;
            foreach (char c in id ?? string.Empty)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return MinHireAge + (int)(hash % (uint)(MaxHireAge - MinHireAge + 1));
        }
    }
}
