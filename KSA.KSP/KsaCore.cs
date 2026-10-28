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
                Rivals = Core.Rivals.RivalDefinition.Parse(Nodes(Core.Rivals.RivalDefinition.NodeName), warnings),
                Names = Core.Recruiting.NameCatalog.Parse(Nodes(Core.Recruiting.NameCatalog.NodeName), warnings)
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
            Log.Info("Names: " + c.Names.Male.Count + " male, " + c.Names.Female.Count + " female, " + c.Names.Family.Count + " family");
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
            var a = new Agency(Catalogs, Clock, Log) { Names = new KspNameSource(Catalogs.Names) };
            a.Ledger.Mirror = new KspFunds();
            foreach (AssemblyLoader.LoadedAssembly la in AssemblyLoader.loadedAssemblies) if (la != null && !string.IsNullOrEmpty(la.name)) a.InstalledMods.Add(la.name);
            a.LiveFlightAvailable = true; // M5: built-in flight director for Kerbin missions
            a.LiveFlightCheck = Flight.LiveFlightLauncher.Check;
            a.TemplateCheck = Parts.KspTemplateCheck.Blocker; // career: a template needs every part researched and bought
            a.ModuleHardwareCheck = Parts.KspHardwareCheck.For; // KSA-64: drills, converters, power and seats in delivered craft
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
            StockSystems.Enforce(Agency.Settings);
            AutoRollout();
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
            Ai.LocalServerLauncher.EnsureRunning(a.Ai);
            Log.Info("AI engineering " + (a.AiDesignAvailable ? "available (" + a.Ai.DesignMode + ")" : "off") + "; " + models.Count + " curated models");
        }

        // ------------------------------------------------------------------ automatic roll-out (Watch Live)

        /// <summary>Set while Mission Control is open: the Director is looking, so nothing rolls out on its own.</summary>
        public static bool RolloutHold;
        /// <summary>Craft written by an older build of the writer are re-assembled once per game session.</summary>
        private static bool _reassembled;
        private static readonly Dictionary<string, double> ReadySince = new Dictionary<string, double>();

        private static double _lastStrandCheck;

        /// <summary>A live project whose vessel no longer exists anywhere (launch failed, craft deleted, reverted) goes back to the pad and frees its crew.</summary>
        private static void ScrubStrandedFlights()
        {
            double now = UnityEngine.Time.realtimeSinceStartup;
            if (now - _lastStrandCheck < 10) return;
            _lastStrandCheck = now;
            if (FlightGlobals.Vessels == null) return;
            foreach (Project p in Agency.Projects)
            {
                if (p.Status != ProjectStatus.InFlight || p.Mode != MissionMode.WatchLive) continue;
                bool found = false;
                uint id;
                if (uint.TryParse(p.VesselId, out id)) foreach (Vessel v in FlightGlobals.Vessels) if (v != null && v.persistentId == id) { found = true; break; }
                if (found) continue;
                if (Flight.KsaFlightDirector.Live != null && Flight.KsaFlightDirector.Live.ProjectId == p.Id) continue;
                // Dead or missing crew on the game roster: the flight ended badly without us seeing it. Close it as a loss.
                var lost = new List<string>();
                KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
                foreach (string cid in p.Crew)
                {
                    ProtoCrewMember pcm = roster != null && roster.Exists(cid) ? roster[cid] : null;
                    if (pcm != null && (pcm.rosterStatus == ProtoCrewMember.RosterStatus.Dead || pcm.rosterStatus == ProtoCrewMember.RosterStatus.Missing)) lost.Add(cid);
                }
                if (lost.Count > 0)
                {
                    var result = new Core.Flight.LiveFlightResult { Crashed = true };
                    result.Deaths.AddRange(lost);
                    Core.Projects.MissionReport r = Agency.ResolveLive(p.Id, result);
                    foreach (string cid in lost) Agency.RecordDeath(cid, "Lost on " + p.Name);
                    Log.Warn("Live project " + p.Name + " closed as loss of crew: " + string.Join(", ", lost.ToArray()));
                    ScreenMessages.PostScreenMessage("[KSA] " + p.Name + ": " + (r != null ? Agency.OutcomeText(r.Outcome) : "loss of crew"), 8f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }
                string msg;
                if (Agency.ReturnToPad(p.Id, out msg))
                {
                    Log.Warn("Live project " + p.Name + " has no vessel in the game; " + msg);
                    ScreenMessages.PostScreenMessage("[KSA] " + p.Name + ": no vessel found for this flight; back on the pad with its crew freed.", 8f, ScreenMessageStyle.UPPER_CENTER);
                }
                return;
            }
        }

        /// <summary>A Ready Watch-Live project rolls to the pad after a short countdown unless Mission Control is open or the setting is 0.</summary>
        private static void AutoRollout()
        {
            if (Agency == null || HighLogic.LoadedScene != GameScenes.SPACECENTER) return;
            ScrubStrandedFlights();
            if (!_reassembled) { _reassembled = true; CraftWriter.ReassembleStale(Agency); }
            int wait = Agency.Settings.AutoRolloutSeconds;
            if (wait <= 0) return;
            double now = UnityEngine.Time.realtimeSinceStartup;
            foreach (Project p in Agency.Projects)
            {
                if (p.Status != ProjectStatus.Ready || p.Mode != MissionMode.WatchLive || Agency.LiveBlocker(p) != null) { ReadySince.Remove(p.Id); continue; }
                double since;
                if (!ReadySince.TryGetValue(p.Id, out since))
                {
                    ReadySince[p.Id] = now;
                    ScreenMessages.PostScreenMessage("[KSA] " + p.Name + " is ready: rolling out to the pad in " + wait + " s (open Mission Control to hold)", 8f, ScreenMessageStyle.UPPER_CENTER);
                    continue;
                }
                if (RolloutHold) { ReadySince[p.Id] = now; continue; }
                if (now - since < wait) continue;
                ReadySince.Remove(p.Id);
                string msg;
                if (Agency.LaunchLive(p.Id, out msg)) Log.Info("Auto roll-out: " + msg); else Log.Warn("Auto roll-out failed: " + msg);
                return; // one launch at a time: the scene changes now
            }
        }

        /// <summary>Re-reads ksa_settings.cfg after the AI Setup window saves it.</summary>
        public static void ReloadAiSettings()
        {
            if (Agency == null) return;
            Agency.Ai = KsaSettingsFile.Load();
            Ai.LocalServerLauncher.Reset();
            Ai.LocalServerLauncher.EnsureRunning(Agency.Ai);
        }

        private static void OnBuildRequested(Project p, Core.Design.DesignRecord record)
        {
            if (record == null) return;
            if (record.Plan == null && !string.IsNullOrEmpty(record.PlanJson)) { Core.Design.StagePlan rebuilt; string err; if (Core.Design.StagePlan.TryParse(record.PlanJson, out rebuilt, out err)) record.Plan = rebuilt; else Log.Warn("Stored plan unreadable: " + err); }
            if (record.Plan == null) { p.Notes.Add("Vehicle assembly skipped: the approved plan was lost; the template flies instead."); Log.Warn("Build requested for " + p.Name + " but the design has no plan"); return; }
            CraftWriter.Result r = CraftWriter.Write(record.Plan, Agency.Parts, "KSA " + p.Id + " " + record.DesignName, "KSA " + p.Id + " " + p.Name);
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
            if (r.Mode == MissionMode.Simulate) LogSimulatedFlight(r);
            PushStars(r);
        }

        /// <summary>
        /// KSA-63: a simulated mission writes its real achievements into the stock flight log (Suborbit:Kerbin, Orbit:Kerbin,
        /// Land:Mun...) exactly as a flown one would, so the game's own level and the Astronaut Complex stars follow.
        /// </summary>
        private static void LogSimulatedFlight(Core.Projects.MissionReport r)
        {
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null) return;
            foreach (string name in r.Crew)
            {
                if (r.Deaths.Contains(name) || !roster.Exists(name)) continue;
                ProtoCrewMember pcm = roster[name];
                int added = 0;
                foreach (string ach in r.Achievements)
                {
                    int colon = ach.IndexOf(':');
                    if (colon <= 0) continue;
                    string type = StockLogType(ach.Substring(0, colon));
                    string body = ach.Substring(colon + 1);
                    if (type == null || FlightGlobals.Bodies == null || !FlightGlobals.Bodies.Exists(b => b.bodyName == body)) continue;
                    if (pcm.careerLog.HasEntry(type, body)) continue;
                    pcm.careerLog.AddEntry(type, body);
                    added++;
                }
                if (added > 0) pcm.UpdateExperience();
            }
        }

        private static string StockLogType(string ksaType)
        {
            // KSA keys already use the stock log names (Flight, Suborbit, Orbit, Flyby, Land); only EVA differs.
            switch (ksaType.ToLowerInvariant())
            {
                case "flight": return "Flight";
                case "suborbit": return "Suborbit";
                case "orbit": return "Orbit";
                case "flyby": return "Flyby";
                case "land": return "Land";
                case "eva": case "exitvessel": return "ExitVessel";
                case "plantflag": return "PlantFlag";
                default: return null;
            }
        }

        /// <summary>The KSA ladder can hold more stars than the game's level; keep the stock kerbal's displayed level in step.</summary>
        private static void PushStars(Core.Projects.MissionReport r)
        {
            KerbalRoster roster = HighLogic.CurrentGame != null ? HighLogic.CurrentGame.CrewRoster : null;
            if (roster == null || Agency == null) return;
            foreach (string name in r.Crew)
            {
                CrewProfile c = Agency.Crew.Get(name);
                if (c == null || !roster.Exists(name)) continue;
                ProtoCrewMember pcm = roster[name];
                if (pcm.experienceLevel < c.Stars) pcm.experienceLevel = c.Stars; // display only: the game recomputes from its log on its own events
            }
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
