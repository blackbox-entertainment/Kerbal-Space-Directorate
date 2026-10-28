using KSA.KSP.Roster;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// While a stock building screen is open, a small KSA button strip in KSP's own skin sits at the bottom right
    /// with the agency windows that belong there: Administration gets Programs, Analytics, Departments and Campaign, the
    /// Astronaut Complex gets Recruiting and the Roster, Mission Control gets the agency's Mission Control.
    /// The windows close with the building.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.SpaceCentre, false)]
    public sealed class FacilityHooks : MonoBehaviour
    {
        private enum Facility { None, Administration, AstronautComplex, MissionControl }
        private Facility _open = Facility.None;

        private ProgramsWindow _programs;
        private AnalyticsWindow _analytics;
        private DepartmentsWindow _departments;
        private CampaignWindow _campaign;
        private RecruitingWindow _recruiting;
        private RosterWindow _roster;
        private MissionControlWindow _missionControl;

        private void Start()
        {
            GameEvents.onGUIAdministrationFacilitySpawn.Add(OnAdminOpen);
            GameEvents.onGUIAdministrationFacilityDespawn.Add(OnFacilityClosed);
            GameEvents.onGUIAstronautComplexSpawn.Add(OnAstronautComplexOpen);
            GameEvents.onGUIAstronautComplexDespawn.Add(OnFacilityClosed);
            GameEvents.onGUIMissionControlSpawn.Add(OnMissionControlOpen);
            GameEvents.onGUIMissionControlDespawn.Add(OnFacilityClosed);
        }

        private void OnDestroy()
        {
            GameEvents.onGUIAdministrationFacilitySpawn.Remove(OnAdminOpen);
            GameEvents.onGUIAdministrationFacilityDespawn.Remove(OnFacilityClosed);
            GameEvents.onGUIAstronautComplexSpawn.Remove(OnAstronautComplexOpen);
            GameEvents.onGUIAstronautComplexDespawn.Remove(OnFacilityClosed);
            GameEvents.onGUIMissionControlSpawn.Remove(OnMissionControlOpen);
            GameEvents.onGUIMissionControlDespawn.Remove(OnFacilityClosed);
            CloseAll();
        }

        private static bool Ready { get { return KsaCore.Agency != null; } }

        private void OnAdminOpen() { _open = Facility.Administration; }
        private void OnAstronautComplexOpen() { _open = Facility.AstronautComplex; }
        private void OnMissionControlOpen() { _open = Facility.MissionControl; }

        private void OnFacilityClosed()
        {
            _open = Facility.None;
            CloseAll();
        }

        private void OnGUI()
        {
            if (_open == Facility.None || !Ready) return;
            GUISkin previous = GUI.skin;
            GUI.skin = HighLogic.Skin;   // KSP's own buttons and box
            try
            {
                const float h = 60f;
                float w = _open == Facility.Administration ? 420f : 250f;
                GUILayout.BeginArea(new Rect(Screen.width - w - 24f, Screen.height - h - 70f, w, h), "KSA", GUI.skin.window);
                GUILayout.BeginHorizontal();
                switch (_open)
                {
                    case Facility.Administration:
                        if (GUILayout.Button("Programs")) ToggleWindow(ref _programs, () => new ProgramsWindow(KsaCore.Agency, () => { _programs = null; }), wnd => wnd.Open(), wnd => wnd.Close());
                        if (GUILayout.Button("Analytics")) ToggleWindow(ref _analytics, () => new AnalyticsWindow(KsaCore.Agency, () => { _analytics = null; }), wnd => wnd.Open(), wnd => wnd.Close());
                        if (GUILayout.Button("Departments")) ToggleWindow(ref _departments, () => new DepartmentsWindow(KsaCore.Agency, () => { _departments = null; }), wnd => wnd.Open(), wnd => wnd.Close());
                        if (GUILayout.Button("Campaign")) ToggleWindow(ref _campaign, () => new CampaignWindow(KsaCore.Agency, () => { _campaign = null; }), wnd => wnd.Open(), wnd => wnd.Close());
                        break;
                    case Facility.AstronautComplex:
                        if (GUILayout.Button("Recruiting")) ToggleWindow(ref _recruiting, () => new RecruitingWindow(KsaCore.Agency, () => { _recruiting = null; }), wnd => wnd.Open(), wnd => wnd.Close());
                        if (GUILayout.Button("Roster")) ToggleWindow(ref _roster, () => new RosterWindow(new KspRosterSource(), KsaCore.Log, () => { _roster = null; }), wnd => wnd.Open(), wnd => wnd.Close());
                        break;
                    case Facility.MissionControl:
                        if (GUILayout.Button("Mission Control")) ToggleWindow(ref _missionControl, () => new MissionControlWindow(KsaCore.Agency, () => { _missionControl = null; }), wnd => wnd.Open(), wnd => wnd.Close());
                        break;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
            }
            finally { GUI.skin = previous; }
        }

        private static void ToggleWindow<T>(ref T field, System.Func<T> make, System.Action<T> open, System.Action<T> close) where T : class
        {
            if (field != null) { close(field); field = null; return; }
            field = make();
            open(field);
        }

        private void CloseAll()
        {
            if (_programs != null) { _programs.Close(); _programs = null; }
            if (_analytics != null) { _analytics.Close(); _analytics = null; }
            if (_departments != null) { _departments.Close(); _departments = null; }
            if (_campaign != null) { _campaign.Close(); _campaign = null; }
            if (_recruiting != null) { _recruiting.Close(); _recruiting = null; }
            if (_roster != null) { _roster.Close(); _roster = null; }
            if (_missionControl != null) { _missionControl.Close(); _missionControl = null; }
        }
    }
}
