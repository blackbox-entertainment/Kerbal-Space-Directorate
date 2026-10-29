using UnityEngine;

namespace KSA.KSP
{
    /// <summary>
    /// Runs once when the Main Menu loads: proves the pipeline ("[KSA] Loaded vX.Y.Z") and loads the
    /// career and medal catalogs from GameDatabase, listing them in KSP.log (Jira KSA-18, KSA-20).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class KsaBootstrap : MonoBehaviour
    {
        private void Start()
        {
            KsaCore.Log.Info("Kerbal Space Directorate v" + Core.CoreInfo.Version + " loaded");
            KsaCore.LoadCatalogs();
        }
    }
}
