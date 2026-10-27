using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Careers;
using KSA.Core.Data;
using KSA.Core.Goals;
using KSA.Core.Medals;
using KSA.Core.Missions;
using KSA.Core.Projects;
using KSA.Core.Personnel;
using KSA.Core.Recruiting;
using KSA.KSP.Ai;
using KSA.KSP.Build;
using KSA.KSP.Data;
using KSA.KSP.Parts;
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
                Morale = MoraleRules.Parse(Concat(Nodes(MoraleRules.NodeName), Nodes(MoraleRules.ScalingNodeName)), warnings),
                Missions = MissionCatalog.Parse(Nodes(MissionCatalog.NodeName), warnings),
                Templates = TemplateCatalog.Parse(Nodes(TemplateCatalog.NodeName), warnings),
                ProgramTypes = ProgramCatalog.Parse(Nodes(ProgramCatalog.NodeName), warnings),
                Rivals = Core.Rivals.RivalDefinition.Parse(Nodes(Core.Rivals.RivalDefinition.NodeName), warnings)
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
            Log.Info("Rivals: " + c.Rivals.Count);
            Log.Info("Missions: " + c.Missions.Count + ", templates: " + c.Templates.Count + ", program types: " + c.ProgramTypes.Count);
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
            foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies) if (la != null && !string.IsNullOrEmpty(la.name)) a.InstalledMods.Add(la.name);
            a.LiveFlightAvailable = true; // M5: built-in flight director for Kerbin missions
            a.LiveFlightCheck = Flight.LiveFlightLauncher.Check;
            a.LiveLaunchRequested += Flight.LiveFlightLauncher.OnLaunchRequested;
            a.ReportUpdated += OnReportUpdated;
            WireAi(a);
            a.MissionResolved += OnMissionResolved;
            a.CrewDeparted += OnCrewDeparted;
            a.ApplicantHired += OnApplicantHired;
            a.YearEnded += OnYearEnded;
            a.TraitsChanged += OnTraitsChanged;
            Agency = a;
            return a;
        }

        public static void ClearAgency()
        {
            if (Agency != null)
            {
                Agency.BuildRequested -= OnBuildRequested;
                Agency.LiveLaunchRequested -= Flight.LiveFlightLauncher.OnLaunchRequested;
                Agency.ReportUpdated -= OnReportUpdated;
                Agency.CrewDeparted -= OnCrewDeparted;
                Agency.ApplicantHired -= OnApplicantHired;
                Agency.YearEnded -= OnYearEnded;
                Agency.TraitsChanged -= OnTraitsChanged;
                Agency.MissionResolved -= OnMissionResolved;
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

        // ------------------------------------------------------------------ M4: AI engineering

        /// <summary>Reads the user settings, offers the part catalog and HTTP transport, and builds craft files on request.</summary>
        public static void WireAi(Agency a)
        {
            List<Core.Ai.ModelOption> models = KsaSettingsFile.Models();
            a.Ai = KsaSettingsFile.Load();
            a.Transport = UnityWebTransport.Instance;
            a.Parts = new LivePartCatalog();
            a.Requirements = KsaSettingsFile.Requirements();
            a.ModelRates = KsaSettingsFile.Rates(models);
            a.AllowDlcParts = Expansions.ExpansionsLoader.IsExpansionInstalled("MakingHistory") || Expansions.ExpansionsLoader.IsExpansionInstalled("Serenity");
            a.LaunchGravity = 9.81;
            a.BuildRequested += OnBuildRequested;
            Log.Info("AI engineering " + (a.AiDesignAvailable ? "available (" + a.Ai.DesignMode + ")" : "off") + "; " + models.Count + " curated models");
        }

        /// <summary>Re-reads ksa_settings.cfg after the AI Setup window saves it.</summary>
        public static void ReloadAiSettings()
        {
            if (Agency == null) return;
            Agency.Ai = KsaSettingsFile.Load();
        }

        private static void OnBuildRequested(Project p, Core.Design.DesignRecord record)
        {
            if (record == null || record.Plan == null) return;
            CraftWriter.Result r = CraftWriter.Write(record.Plan, Agency.Parts, record.DesignName, "KSA " + p.Id + " " + p.Name);
            if (r.Ok)
            {
                record.CraftPath = r.Path;
                p.Notes.Add("Vehicle assembled: " + r.Parts + " parts, saved to the VAB as \"KSA " + p.Id + " " + p.Name + "\".");
                foreach (string w in r.Warnings) p.Notes.Add("Assembly note: " + w);
                ScreenMessages.PostScreenMessage("[KSA] " + p.Name + ": vehicle assembled (" + r.Parts + " parts)", 6f, ScreenMessageStyle.UPPER_CENTER);
            }
            else
            {
                p.Notes.Add("Vehicle assembly failed: " + r.Error + " The template craft will fly instead.");
                Log.Warn("Assembly failed for " + p.Name + ": " + r.Error);
            }
        }

        /// <summary>Always reflects the current game: parts researched later show up without a restart.</summary>
        private sealed class LivePartCatalog : Core.Parts.IPartCatalog
        {
            public IReadOnlyList<Core.Parts.PartInfo> All { get { return KspPartCatalog.Current.All; } }
            public Core.Parts.PartInfo Get(string name) { return KspPartCatalog.Current.Get(name); }
        }

        /// <summary>Experience moved a kerbal's Courage or Stupidity: the stock kerbal follows (shown in the Astronaut Complex).</summary>
        private static void OnTraitsChanged(CrewProfile p)
        {
            if (HighLogic.CurrentGame == null || HighLogic.CurrentGame.CrewRoster == null) return;
            ProtoCrewMember pcm = HighLogic.CurrentGame.CrewRoster[p.Name];
            if (pcm == null) return;
            pcm.courage = p.Courage;
            pcm.stupidity = p.Stupidity;
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

        private static void OnMissionResolved(Core.Projects.MissionReport r)
        {
            ScreenMessages.PostScreenMessage("[KSA] " + r.ProjectName + ": " + Agency.OutcomeText(r.Outcome), 8f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static void OnReportUpdated(Core.Projects.MissionReport r)
        {
            ScreenMessages.PostScreenMessage("[KSA] Post-mortem filed for " + r.ProjectName, 6f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static void OnYearEnded(Core.Review.ReviewResult r)
        {
            ScreenMessages.PostScreenMessage("[KSA] Government Review, year " + r.Year + ": " + r.BandText, 10f, ScreenMessageStyle.UPPER_CENTER);
        }

        private static List<DataNode> Concat(List<DataNode> a, List<DataNode> b)
        {
            var list = new List<DataNode>(a);
            list.AddRange(b);
            return list;
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
