using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Ties the agency's windows to the stock buildings: Administration opens Programs (with Analytics a click away),
    /// the Astronaut Complex opens Recruiting, and Mission Control opens the agency's Mission Control. The windows
    /// close again when the stock screen closes. The Director's Office stays on the toolbar for everything else.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public sealed class FacilityHooks : MonoBehaviour
    {
        private ProgramsWindow _programs;
        private AnalyticsWindow _analytics;
        private RecruitingWindow _recruiting;
        private MissionControlWindow _missionControl;

        private void Start()
        {
            GameEvents.onGUIAdministrationFacilitySpawn.Add(OnAdminOpen);
            GameEvents.onGUIAdministrationFacilityDespawn.Add(OnAdminClose);
            GameEvents.onGUIAstronautComplexSpawn.Add(OnAstronautComplexOpen);
            GameEvents.onGUIAstronautComplexDespawn.Add(OnAstronautComplexClose);
            GameEvents.onGUIMissionControlSpawn.Add(OnMissionControlOpen);
            GameEvents.onGUIMissionControlDespawn.Add(OnMissionControlClose);
        }

        private void OnDestroy()
        {
            GameEvents.onGUIAdministrationFacilitySpawn.Remove(OnAdminOpen);
            GameEvents.onGUIAdministrationFacilityDespawn.Remove(OnAdminClose);
            GameEvents.onGUIAstronautComplexSpawn.Remove(OnAstronautComplexOpen);
            GameEvents.onGUIAstronautComplexDespawn.Remove(OnAstronautComplexClose);
            GameEvents.onGUIMissionControlSpawn.Remove(OnMissionControlOpen);
            GameEvents.onGUIMissionControlDespawn.Remove(OnMissionControlClose);
            CloseAll();
        }

        private static bool Ready { get { return KsaCore.Agency != null; } }

        private void OnAdminOpen()
        {
            if (!Ready) return;
            if (_programs == null) _programs = new ProgramsWindow(KsaCore.Agency, () => { _programs = null; });
            _programs.Open();
            if (_analytics == null) _analytics = new AnalyticsWindow(KsaCore.Agency, () => { _analytics = null; });
            _analytics.Open();
            KsaCore.Log.Info("Administration opened: Programs and Analytics");
        }

        private void OnAdminClose()
        {
            if (_programs != null) { _programs.Close(); _programs = null; }
            if (_analytics != null) { _analytics.Close(); _analytics = null; }
        }

        private void OnAstronautComplexOpen()
        {
            if (!Ready) return;
            if (_recruiting == null) _recruiting = new RecruitingWindow(KsaCore.Agency, () => { _recruiting = null; });
            _recruiting.Open();
            KsaCore.Log.Info("Astronaut Complex opened: Recruiting");
        }

        private void OnAstronautComplexClose()
        {
            if (_recruiting != null) { _recruiting.Close(); _recruiting = null; }
        }

        private void OnMissionControlOpen()
        {
            if (!Ready) return;
            if (_missionControl == null) _missionControl = new MissionControlWindow(KsaCore.Agency, () => { _missionControl = null; });
            _missionControl.Open();
            KsaCore.Log.Info("Mission Control opened: agency Mission Control");
        }

        private void OnMissionControlClose()
        {
            if (_missionControl != null) { _missionControl.Close(); _missionControl = null; }
        }

        private void CloseAll()
        {
            OnAdminClose();
            OnAstronautComplexClose();
            OnMissionControlClose();
        }
    }
}
