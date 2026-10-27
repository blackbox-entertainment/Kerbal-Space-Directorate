using KSP.UI.Screens;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>In the Tracking Station the KSA button opens the agency's Mission Control (live flights, pad, reports).</summary>
    [KSPAddon(KSPAddon.Startup.TrackingStation, false)]
    public sealed class KsaTrackingToolbar : MonoBehaviour
    {
        private ApplicationLauncherButton _button;
        private MissionControlWindow _window;

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Add(Remove);
            if (ApplicationLauncher.Ready) OnReady();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnReady);
            GameEvents.onGUIApplicationLauncherDestroyed.Remove(Remove);
            if (_window != null) _window.Close();
            Remove();
        }

        private void OnReady()
        {
            if (_button != null || ApplicationLauncher.Instance == null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(OnOn, OnOff, null, null, null, null,
                ApplicationLauncher.AppScenes.TRACKSTATION, KsaToolbar.GetIcon());
        }

        private void Remove()
        {
            if (_button == null) return;
            if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        private void OnOn()
        {
            if (KsaCore.Agency == null) { if (_button != null) _button.SetFalse(false); return; }
            if (_window == null) _window = new MissionControlWindow(KsaCore.Agency, () => { _window = null; if (_button != null) _button.SetFalse(false); });
            _window.Open();
        }

        private void OnOff() { if (_window != null) _window.Close(); }
    }
}
