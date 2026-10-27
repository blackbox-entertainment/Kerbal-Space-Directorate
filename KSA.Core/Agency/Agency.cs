using System;
using System.Collections.Generic;
using KSA.Core.Careers;
using KSA.Core.Data;
using KSA.Core.Medals;
using KSA.Core.Personnel;

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
    /// The agency itself: owns the crew registry and applies career and medal rules to events the host reports.
    /// Platform-free. The host (KSA.KSP) feeds it roster snapshots, mission events, a clock, and persistence.
    /// </summary>
    public sealed class Agency
    {
        public const int MinHireAge = 24;
        public const int MaxHireAge = 38;

        public CrewRegistry Crew { get; }
        public CareerCatalog Careers { get; }
        public MedalCatalog Medals { get; }
        public PromotionEngine Promotions { get; }
        public MedalEvaluator MedalRules { get; }
        public IClock Clock { get; }
        public ILogger Log { get; }

        /// <summary>Counter for host-independent mission ids.</summary>
        public int NextMissionNumber { get; set; }

        /// <summary>Raised for every human-readable change (promotion, medal, hire). The host shows or logs it.</summary>
        public event Action<string> Notice;

        public Agency(CareerCatalog careers, MedalCatalog medals, IClock clock, ILogger log)
        {
            if (careers == null) throw new ArgumentNullException(nameof(careers));
            if (medals == null) throw new ArgumentNullException(nameof(medals));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (log == null) throw new ArgumentNullException(nameof(log));
            Careers = careers;
            Medals = medals;
            Clock = clock;
            Log = log;
            Crew = new CrewRegistry();
            Promotions = new PromotionEngine(careers, Crew);
            MedalRules = new MedalEvaluator(medals, Crew);
            NextMissionNumber = 1;
        }

        public int CurrentDay { get { return Clock.CurrentDay(); } }

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
                else if (p.Departed)
                {
                    // Came back (e.g. respawned or re-added by the host). Keep the record.
                    p.Departed = false;
                }

                p.Role = s.Role;
                p.Stars = s.Stars;
                p.Status = s.Status;
                p.Courage = s.Courage;
                p.Stupidity = s.Stupidity;
                for (int i = 0; i < s.Achievements.Count; i++) p.Record.Achievements.Add(s.Achievements[i]);

                if (Promotions.AssignStartingRank(p))
                    Push(result.Notices, p.Name + " hired as " + Careers.RankName(p.Track, p.RankId));
            }

            foreach (CrewProfile p in Crew.All)
            {
                if (!seen.Contains(p.Id) && !p.Departed)
                {
                    p.Departed = true;
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
            if (p.IsFlying)
            {
                double seconds = Clock.UniversalTime - p.InFlightSinceUT;
                if (seconds > 0 && Clock.SecondsPerDay > 0) p.Record.DaysInSpace += seconds / Clock.SecondsPerDay;
                p.InFlightSinceUT = -1;
            }
            if (p.Status == CrewStatus.Assigned && outcome != MissionOutcome.LossOfCrew) p.Status = CrewStatus.Available;

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

            RecordIncident(crewId, IncidentKind.Death, description);
            p.Status = CrewStatus.Dead;
            MissionEntry m = p.Record.CurrentMission;
            if (m != null)
            {
                m.Outcome = MissionOutcome.LossOfCrew;
                m.EndDay = CurrentDay;
            }
            if (p.IsFlying)
            {
                double seconds = Clock.UniversalTime - p.InFlightSinceUT;
                if (seconds > 0 && Clock.SecondsPerDay > 0) p.Record.DaysInSpace += seconds / Clock.SecondsPerDay;
                p.InFlightSinceUT = -1;
            }
            Push(notices, p.Name + " was killed in the line of duty");
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

        // ------------------------------------------------------------------ director decisions

        /// <summary>The Director grants a pending Director-selected rank (e.g. Test Pilot to Astronaut Candidate).</summary>
        public RankDefinition SelectForRank(string crewId)
        {
            CrewProfile p = Crew.Get(crewId);
            if (p == null) return null;
            RankDefinition r = Promotions.Select(p);
            if (r != null) Push(null, p.Name + " selected as " + r.Name);
            return r;
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
                Push(notices, p.Name + " awarded " + medal.Name);
            foreach (RankDefinition rank in Promotions.PromoteAutomatically(p))
                Push(notices, p.Name + " promoted to " + rank.Name);
        }

        private void Push(List<string> notices, string message)
        {
            if (notices != null) notices.Add(message);
            Log.Info(message);
            Action<string> handler = Notice;
            if (handler != null) handler(message);
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
