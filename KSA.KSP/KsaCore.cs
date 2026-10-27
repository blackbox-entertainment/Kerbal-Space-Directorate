using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Careers;
using KSA.Core.Data;
using KSA.Core.Medals;
using KSA.Core.Personnel;
using KSA.KSP.Data;
using KSA.KSP.Roster;

namespace KSA.KSP
{
    /// <summary>
    /// The one place the KSP side keeps its Core objects. Catalogs load once from GameDatabase (after ModuleManager);
    /// the Agency is created per loaded game by <see cref="KsaScenario"/>.
    /// </summary>
    public static class KsaCore
    {
        public static readonly ILogger Log = new KspLogger();
        public static readonly KspClock Clock = new KspClock();

        public static CareerCatalog Careers { get; private set; }
        public static MedalCatalog Medals { get; private set; }
        public static Agency Agency { get; private set; }

        public static bool CatalogsLoaded { get { return Careers != null && Medals != null; } }

        /// <summary>Reads KSA_CAREER_TRACK and KSA_MEDAL nodes from GameDatabase. Safe to call repeatedly; reloads each time.</summary>
        public static void LoadCatalogs()
        {
            var warnings = new List<string>();
            Careers = CareerCatalog.Parse(Nodes(CareerCatalog.TrackNodeName), warnings);
            Medals = MedalCatalog.Parse(Nodes(MedalCatalog.NodeName), warnings);

            foreach (CareerTrackDefinition t in Careers.Tracks)
            {
                var names = new List<string>();
                foreach (RankDefinition r in t.Ranks) names.Add(r.Name);
                Log.Info("Career track " + t.Name + " (" + t.Ranks.Count + " ranks): " + string.Join(" > ", names.ToArray()));
            }
            var medalNames = new List<string>();
            foreach (MedalDefinition m in Medals.Medals) medalNames.Add(m.Name);
            Log.Info("Medals (" + Medals.Count + "): " + string.Join(", ", medalNames.ToArray()));
            foreach (string w in warnings) Log.Warn("Config: " + w);
        }

        /// <summary>Creates a fresh Agency for a newly loaded game. Called by the scenario module.</summary>
        public static Agency CreateAgency()
        {
            if (!CatalogsLoaded) LoadCatalogs();
            Agency = new Agency(Careers, Medals, Clock, Log);
            return Agency;
        }

        public static void ClearAgency()
        {
            Agency = null;
        }

        /// <summary>Pulls the current roster from the game into the Agency and logs what changed.</summary>
        public static void SyncRoster(string reason)
        {
            if (Agency == null) return;
            List<CrewSnapshot> snapshots = new KspRosterSource().GetSnapshots();
            var result = Agency.SyncRoster(snapshots);
            Log.Info("Roster sync (" + reason + "): " + snapshots.Count + " crew, " + result.Hired.Count + " hired, " + result.Departed.Count + " departed");
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
