using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// The KSA mission report (2026-10-28): after a live flight, what the crew and engineers earned on KSA's ladder and the
    /// Science paid, shown beside KSP's own recovery screen, which only knows stock experience (and shows none for a
    /// kerbal repeating a flight). Shown underneath that screen. Reports wait in a queue until the game is at the Space Center
    /// or Tracking Station.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public sealed class MissionReportPopup : MonoBehaviour
    {
        private static readonly Queue<MissionReport> Pending = new Queue<MissionReport>();
        private float _showAt = -1f;
        private PopupDialog _dialog;

        /// <summary>Wired to Agency.LiveMissionReported.</summary>
        public static void Enqueue(MissionReport r) { if (r != null) Pending.Enqueue(r); }

        private void Update()
        {
            if (Pending.Count == 0 || _dialog != null) return;
            if (HighLogic.LoadedScene != GameScenes.SPACECENTER && HighLogic.LoadedScene != GameScenes.TRACKSTATION) { _showAt = -1f; return; }
            // A moment after the scene settles, so the report opens on top of the stock recovery screen.
            if (_showAt < 0) { _showAt = Time.realtimeSinceStartup + 1.5f; return; }
            if (Time.realtimeSinceStartup < _showAt) return;
            _showAt = -1f;
            Show(Pending.Dequeue());
        }

        private void OnDestroy() { if (_dialog != null) { _dialog.Dismiss(); _dialog = null; } }

        private void Show(MissionReport r)
        {
            const float width = 520f;
            var items = new List<DialogGUIBase>();
            items.Add(new DialogGUILabel("<b>" + r.ProjectName + "</b>: " + Agency.OutcomeText(r.Outcome), width - 30f, 24f));
            if (r.Rewards.Count == 0) items.Add(new DialogGUILabel("Nothing earned on this flight.", width - 30f, 22f));
            foreach (string line in r.Rewards) items.Add(new DialogGUILabel(line, width - 30f, KsaUi.LabelHeight(line, width - 30f, 22f)));
            items.Add(new DialogGUILabel("<color=#999999>KSP's recovery screen shows only stock experience; KSA's stars are in the Roster and Service Records.</color>", width - 30f, 40f));
            items.Add(new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleRight,
                new DialogGUIFlexibleSpace(),
                new DialogGUIButton("OK", () => { _dialog = null; }, 90f, 30f, true)));
            var content = new DialogGUIVerticalLayout(true, false, 4f, new RectOffset(8, 8, 8, 8), TextAnchor.UpperLeft, items.ToArray());
            var dialog = new MultiOptionDialog("KSA_MissionReport", "", "KSA mission report", KsaUi.Skin, new Rect(0.5f, 0.12f, width, 120f), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.12f), dialog, false, KsaUi.Skin, false); // under the stock recovery screen, not over it
            KsaCore.Log.Info("Mission report shown for " + r.ProjectName + ": " + string.Join("; ", r.Rewards.ToArray()));
        }
    }
}
