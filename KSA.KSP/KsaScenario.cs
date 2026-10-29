using KSA.Core;
using KSA.Core.Data;
using KSA.KSP.Data;

namespace KSA.KSP
{
    /// <summary>
    /// Persists the Agency inside the game's save file (Jira KSA-22). KSP creates one of these per loaded game
    /// and calls OnLoad / OnSave; between those, the Agency lives in <see cref="KsaCore.Agency"/>.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.SPACECENTER, GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.EDITOR)]
    public class KsaScenario : ScenarioModule
    {
        private bool _loaded;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            Agency agency = KsaCore.CreateAgency();
            bool hadState = agency.Load(new ConfigNodePersistence(node));
            _loaded = true;
            KsaCore.Log.Info(hadState
                ? "Agency state loaded: " + agency.Crew.Count + " crew on file"
                : "No KSA state in this save; starting a fresh agency");
        }

        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (KsaCore.Agency == null) return;
            KsaCore.Agency.Save(new ConfigNodePersistence(node));
        }

        public void Start()
        {
            // OnLoad runs before the roster is guaranteed complete; sync once the scene is up.
            if (!_loaded) KsaCore.CreateAgency();
            Roster.KspCrewFactory.RepairMisnamedHires(KsaCore.Agency); // hires the old rename bug left under random names
            if (KsaCore.Agency != null)
                foreach (string id in KsaCore.Agency.UndoPhantomStrandings()) KsaCore.Log.Info("Save repair: " + id + " never had a rocket; its stranding was undone");
            KsaCore.SyncRoster("scene start");
            KsaCore.TickAgency("scene start");
            Build.CraftWriter.PublishTemplates();
        }

        public void OnDestroy()
        {
            // The scenario is recreated for the next game; don't leave a stale Agency behind at the main menu.
            if (HighLogic.LoadedScene == GameScenes.MAINMENU) KsaCore.ClearAgency();
        }

        /// <summary>Core's IPersistence over the ConfigNode KSP hands to the scenario module.</summary>
        private sealed class ConfigNodePersistence : IPersistence
        {
            private readonly ConfigNode _node;

            public ConfigNodePersistence(ConfigNode node) { _node = node; }

            public DataNode LoadState()
            {
                if (_node == null || !_node.HasNode(AgencyStateSerializer.RootName)) return null;
                return ConfigNodeConverter.ToDataNode(_node.GetNode(AgencyStateSerializer.RootName));
            }

            public void SaveState(DataNode state)
            {
                if (_node == null || state == null) return;
                _node.RemoveNodes(AgencyStateSerializer.RootName);
                _node.AddNode(ConfigNodeConverter.ToConfigNode(state));
            }
        }
    }
}
