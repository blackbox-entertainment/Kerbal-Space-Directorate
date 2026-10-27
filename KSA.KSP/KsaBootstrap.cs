using UnityEngine;

namespace KSA.KSP
{
    /// <summary>
    /// Minimal pipeline proof: runs once when the Main Menu loads and writes
    /// "[KSA] Loaded vX.Y.Z" to KSP.log.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class KsaBootstrap : MonoBehaviour
    {
        private void Start()
        {
            Core.ILogger log = new KspLogger();
            log.Info("Loaded v" + Core.CoreInfo.Version);
        }
    }
}
