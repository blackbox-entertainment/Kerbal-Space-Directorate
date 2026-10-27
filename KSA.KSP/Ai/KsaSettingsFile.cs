using System;
using System.Collections.Generic;
using System.IO;
using KSA.Core.Ai;
using KSA.Core.Data;
using KSA.Core.Design;
using KSA.KSP.Data;

namespace KSA.KSP.Ai
{
    /// <summary>
    /// Reads and writes the user-local AI settings at GameData/KSA/PluginData/ksa_settings.cfg (Jira KSA-40).
    /// The file holds the API key, so it is git-ignored and never shipped; only the example file is.
    /// </summary>
    public static class KsaSettingsFile
    {
        public static string PluginDataDir
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, Path.Combine("GameData", Path.Combine("KSA", "PluginData"))); }
        }

        public static string SettingsPath { get { return Path.Combine(PluginDataDir, "ksa_settings.cfg"); } }
        public static string ExamplePath { get { return Path.Combine(PluginDataDir, "ksa_settings.example.cfg"); } }

        public static bool Exists { get { return File.Exists(SettingsPath); } }

        public static AiSettings Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                {
                    KsaCore.Log.Info("No ksa_settings.cfg yet (AI off). Use AI Setup in the Director's Office to create one.");
                    return new AiSettings();
                }
                ConfigNode root = ConfigNode.Load(SettingsPath);
                ConfigNode node = root != null ? (root.HasNode(AiSettings.NodeName) ? root.GetNode(AiSettings.NodeName) : root) : null;
                AiSettings s = AiSettings.Parse(ConfigNodeConverter.ToDataNode(node));
                KsaCore.Log.Info("AI settings loaded: mode " + s.DesignMode + ", provider " + s.Provider + ", key " + (s.HasApiKey ? "present" : "missing")
                                 + ", design model '" + s.DesignModel + "', local model '" + s.LocalModel + "'");
                return s;
            }
            catch (Exception e)
            {
                KsaCore.Log.Error("Could not read ksa_settings.cfg: " + e.Message);
                return new AiSettings();
            }
        }

        public static bool Save(AiSettings settings, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(PluginDataDir);
                ConfigNode node = ConfigNodeConverter.ToConfigNode(settings.ToDataNode());
                var root = new ConfigNode();
                root.AddNode(node);
                root.Save(SettingsPath, "KSA user settings. Contains your API key: never commit or share this file.");
                KsaCore.Log.Info("AI settings saved (mode " + settings.DesignMode + ")");
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                KsaCore.Log.Error("Could not save ksa_settings.cfg: " + e.Message);
                return false;
            }
        }

        /// <summary>Curated model list from ai_models.cfg (GameDatabase, after ModuleManager).</summary>
        public static List<ModelOption> Models()
        {
            var nodes = new List<DataNode>();
            if (GameDatabase.Instance != null)
                foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes(ModelOption.NodeName)) nodes.Add(ConfigNodeConverter.ToDataNode(n));
            return ModelOption.Parse(nodes);
        }

        public static MissionRequirements Requirements()
        {
            var nodes = new List<DataNode>();
            if (GameDatabase.Instance != null)
                foreach (ConfigNode n in GameDatabase.Instance.GetConfigNodes(MissionRequirements.NodeName)) nodes.Add(ConfigNodeConverter.ToDataNode(n));
            return MissionRequirements.Parse(nodes);
        }

        public static Dictionary<string, double> Rates(List<ModelOption> models)
        {
            var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (ModelOption m in models) if (!rates.ContainsKey(m.Id)) rates[m.Id] = m.CostPer1k;
            return rates;
        }
    }
}
