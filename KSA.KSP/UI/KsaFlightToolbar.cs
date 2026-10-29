using KSA.KSP.Flight;
using KSP.UI.Screens;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// In flight the KSD toolbar button shows or hides the Flight Director window (asked for 2026-10-28). Hiding it only
    /// hides the window: the crew keeps flying, and a pad offer still pops up because it needs an answer.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public sealed class KsaFlightToolbar : MonoBehaviour
    {
        private ApplicationLauncherButton _button;

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
            Remove();
        }

        private void OnReady()
        {
            if (_button != null || ApplicationLauncher.Instance == null) return;
            _button = ApplicationLauncher.Instance.AddModApplication(OnShow, OnHide, null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW, KsaToolbar.GetIcon());
            // Lit while the window shows.
            if (!KsaFlightDirector.HudHidden) _button.SetTrue(false);
        }

        private void Remove()
        {
            if (_button == null) return;
            if (ApplicationLauncher.Instance != null) ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        private void OnShow() { KsaFlightDirector.HudHidden = false; }

        private void OnHide() { KsaFlightDirector.HudHidden = true; }
    }
}
