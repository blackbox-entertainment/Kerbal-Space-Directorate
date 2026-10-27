using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Careers;
using KSA.Core.Data;
using KSA.Core.Goals;
using KSA.Core.Medals;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;
using KSA.KSP.Data;
using KSA.KSP.Roster;

namespace KSA.KSP
{
    /// <summary>
    /// The one place the KSP side keeps its Core objects. Catalogs load once from GameDatabase (after ModuleManager);
    /// the Agency is created per loaded game by <see cref="KsaScenario"/> and driven by <see cref="TickAgency"/>.
    /// </summary>
    public static class KsaCore
    {
        public static readonly ILogger Log = new KspLogger();
        public static readonly KspClock Clock = new KspClock();

        public static AgencyCatalogs Catalogs { get; private set; }
        public static Agency Agency { get; private set; }

        public static bool CatalogsLoaded { get { return Catalogs != null; } }

        /// <summary>Reads every KSA config node type from GameDatabase. Safe to call repeatedly; reloads each time.</summary>
        public static void LoadCatalogs()
        {
            var warnings = new List<string>();
            var c = new AgencyCatalogs
            {
                Careers = CareerCatalog.Parse(Nodes(CareerCatalog.TrackNodeName), warnings),
                Medals = MedalCatalog.Parse(Nodes(MedalCatalog.NodeName), warnings),
                Goals = GoalCatalog.Parse(Nodes(GoalCatalog.NodeName), warnings),
                Recruiting = RecruitCatalog.Parse(Nodes(RecruitCatalog.NodeName), warnings),
                Morale = MoraleRules.Parse(Nodes(MoraleRules.NodeName), warnings)
            };
            List<DataNode> settingsNodes = Nodes(AgencySettings.NodeName);
            c.Settings = AgencySettings.Parse(settingsNodes.Count > 0 ? settingsNodes[0] : null);
            Catalogs = c;

            foreach (CareerTrackDefinition t in c.Careers.Tracks)
            {
                var names = new List<string>();
                foreach (RankDefinition r in t.Ranks) names.Add(r.Name);
                Log.Info("Career track " + t.Name + " (" + t.Ranks.Count + " ranks): " + string.Join(" > ", names.ToArray()));
            }
            var medalNames = new List<string>();
            foreach (MedalDefinition m in c.Medals.Medals) medalNames.Add(m.Name);
            Log.Info("Medals (" + c.Medals.Count + "): " + string.Join(", ", medalNames.ToArray()));
            Log.Info("Goals: " + c.Goals.Count + ", recruitment sources: " + c.Recruiting.Count + ", base grant " + Agency.Money(c.Settings.BaseGrant)
                     + ", " + c.Settings.GoalsPerYear + " goals/year" + (settingsNodes.Count == 0 ? " (defaults; no KSA_AGENCY_SETTINGS node found)" : string.Empty));
            foreach (string w in warnings) Log.Warn("Config: " + w);
        }

        /// <summary>Creates a fresh Agency for a newly loaded game and wires it to the game. Called by the scenario module.</summary>
        public static Agency CreateAgency()
        {
            if (!CatalogsLoaded) LoadCatalogs();
            ClearAgency();
            var a = new Agency(Catalogs, Clock, Log) { Names = new KspNameSource() };
            a.Ledger.Mirror = new KspFunds();
            a.CrewDeparted += OnCrewDeparted;
            a.ApplicantHired += OnApplicantHired;
            a.YearEnded += OnYearEnded;
            Agency = a;
            return a;
        }

        public static void ClearAgency()
        {
            if (Agency != null)
            {
                Agency.CrewDeparted -= OnCrewDeparted;
                Agency.ApplicantHired -= OnApplicantHired;
                Agency.YearEnded -= OnYearEnded;
            }
            Agency = null;
        }

        /// <summary>Pulls the current roster from the game into the Agency and logs what changed.</summary>
        private static int _lastSyncFrame = -1;

        public static void SyncRoster(string reason)
        {
            if (Agency == null) return;
            // The game fires kerbal events in bursts (one per applicant, per status change); one sync per frame is plenty.
            if (UnityEngine.Time.frameCount == _lastSyncFrame) return;
            _lastSyncFrame = UnityEngine.Time.frameCount;
            List<CrewSnapshot> snapshots = new KspRosterSource().GetSnapshots();
            var result = Agency.SyncRoster(snapshots);
            Log.Info("Roster sync (" + reason + "): " + snapshots.Count + " crew, " + result.Hired.Count + " hired, " + result.Departed.Count + " departed");
        }

        /// <summary>Advances agency time (fiscal years, quarterly applicants). Notices go to the screen and the log.</summary>
        public static void TickAgency(string reason)
        {
            if (Agency == null) return;
            if (Agency.Ledger.Mirror != null && Agency.Ledger.Mirror.IsActive && Agency.State.Started) Agency.Ledger.SyncFromMirror();
            List<string> notices = Agency.Tick();
            if (notices.Count == 0) return;
            Log.Info("Agency tick (" + reason + "): " + notices.Count + " notices");
            int shown = 0;
            foreach (string n in notices)
            {
                if (shown++ >= 6) break;
                ScreenMessages.PostScreenMessage("[KSA] " + n, 6f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        private static void OnCrewDeparted(CrewProfile p, DepartureReason reason)
        {
            if (reason == DepartureReason.Died) return;
            KspCrewFactory.Remove(p.Name);
            ScreenMessages.PostScreenMessage("[KSA] " + p.Name + (reason == DepartureReason.Retired ? " has retired." : reason == DepartureReason.Quit ? " has resigned." : " has been dismissed."), 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static void OnApplicantHired(Applicant a, CrewProfile p)
        {
            KspCrewFactory.Create(a, p);
            SyncRoster("hire");
        }

        private static void OnYearEnded(Core.Review.ReviewResult r)
        {
            ScreenMessages.PostScreenMessage("[KSA] Government Review, year " + r.Year + ": " + r.BandText, 10f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static List<DataNode> Nodes(string nodeName)
        {
            var list = new List<DataNode>();
            if (GameDatabase.Instance == null) return list;
            foreach (ConfigNode node in GameDatabase.Instance.GetConfigNodes(nodeName))
            {
                DataNode d = ConfigNodeConverter.ToDataNode(node);
                if (d != null) list.Add(d);
            }
            return list;
        }
    }
}
