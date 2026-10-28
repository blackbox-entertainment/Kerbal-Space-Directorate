using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Goals;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Campaign view: the exploration ladder, one row per body, with each step marked done or open from the agency's
    /// achievements, a "next step" hint per body, and the government's active goals underneath so the Director can see
    /// where the agency stands and what is being asked for. Read-only.
    /// </summary>
    public sealed class CampaignWindow
    {
        private const string DialogName = "KSA_Campaign";
        private const float Width = 720f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 200f, 520f, 860f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LineHeight = 22f;
        private const float BodyWidth = 90f;
        private const float CellWidth = 78f;
        private const string Green = "#8be28b";
        private const string Grey = "#777777";
        private const string Amber = "#ffd27f";

        private static readonly string[] Steps = { "Flyby", "Orbit", "Land", "Science", "Base", "Resupply" };
        private static readonly string[] Headers = { "Flyby", "Orbit", "Land", "Science", "Base", "Supplied" };

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;

        public CampaignWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open() { if (_dialog == null) Spawn(); }

        public void Close()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            KsaCore.Log.Info("Campaign window: " + Agency.CustomBodies.Length + " bodies, " + _agency.ActiveGoals.Count + " active goal(s)");

            Line(items, "Every world is a ladder: fly past it, orbit it, land, bring science home, build a base and keep it supplied. "
                + Color("Green", Green) + " steps are done by someone who has worked here; " + Color("grey", Grey) + " steps are still open.", textWidth);
            items.Add(new DialogGUISpace(6f));

            // Header row.
            var header = new List<DialogGUIBase>();
            header.Add(new DialogGUILabel("<b>Body</b>", BodyWidth, LineHeight));
            foreach (string h in Headers) header.Add(new DialogGUILabel("<b>" + h + "</b>", CellWidth, LineHeight));
            header.Add(new DialogGUILabel("<b>Next step</b>", textWidth - BodyWidth - CellWidth * Headers.Length - 6f * (Headers.Length + 1), LineHeight));
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, header.ToArray()));

            int doneTotal = 0, stepTotal = 0;
            foreach (string body in Agency.CustomBodies)
            {
                bool home = string.Equals(body, "Kerbin", StringComparison.OrdinalIgnoreCase);
                var row = new List<DialogGUIBase>();
                row.Add(new DialogGUILabel("<b>" + body + "</b>", BodyWidth, LineHeight));
                string next = null;
                for (int i = 0; i < Steps.Length; i++)
                {
                    string key = StepKey(body, i, home);
                    string label = home && i == 0 ? "Suborbit" : Headers[i];
                    bool done = _agency.HasAgencyAchievement(key);
                    stepTotal++;
                    if (done) doneTotal++;
                    else if (next == null) next = NextHint(body, i, home);
                    row.Add(new DialogGUILabel(done ? Color(label, Green) : Color(label, Grey), CellWidth, LineHeight));
                }
                row.Add(new DialogGUILabel(next ?? Color("ladder complete", Green), textWidth - BodyWidth - CellWidth * Headers.Length - 6f * (Headers.Length + 1), LineHeight));
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft, row.ToArray()));
            }
            items.Add(new DialogGUISpace(4f));
            Line(items, doneTotal + " of " + stepTotal + " steps done across " + Agency.CustomBodies.Length + " worlds.", textWidth);

            // Government goals.
            items.Add(new DialogGUISpace(10f));
            items.Add(new DialogGUILabel("<b>What the government is asking for (" + _agency.ActiveGoals.Count + ")</b>", textWidth, LineHeight + 2f));
            if (_agency.ActiveGoals.Count == 0) Line(items, "No active goals. The next budget review will set some.", textWidth);
            foreach (GoalState gs in _agency.ActiveGoals)
            {
                GoalDefinition def = _agency.Catalogs.Goals.Get(gs.GoalId);
                string name = def != null ? def.Name : gs.GoalId;
                bool met = _agency.IsGoalCurrentlyMet(gs);
                string desc = def != null && !string.IsNullOrEmpty(def.Description) ? "  |  " + def.Description : string.Empty;
                Line(items, (met ? Color("[met] ", Green) : Color("[open] ", Amber)) + "<b>" + name + "</b>  (set in year " + gs.YearIssued + ")" + desc, textWidth);
            }

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel("Where the agency has been, and where it goes next.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Campaign", KsaUi.Skin, new Rect(0.5f, 0.5f, Width, WindowHeight), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        /// <summary>Achievement key for a ladder step; Kerbin's first rung is a suborbital hop rather than a flyby.</summary>
        private static string StepKey(string body, int step, bool home)
        {
            if (home && step == 0) return "Suborbit:" + body;
            return Steps[step] + ":" + body;
        }

        private static string NextHint(string body, int step, bool home)
        {
            switch (step)
            {
                case 0: return home ? "fly a suborbital hop" : "fly past " + body;
                case 1: return "orbit " + body;
                case 2: return "land on " + body;
                case 3: return "run experiments at " + body;
                case 4: return "build a base or station at " + body + " (Programs)";
                default: return "send a resupply to the " + body + " program";
            }
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        private static string Color(string text, string hex) { return "<color=" + hex + ">" + text + "</color>"; }

        private static void Line(List<DialogGUIBase> items, string text, float textWidth)
        {
            items.Add(new DialogGUILabel(text, textWidth, KsaUi.LabelHeight(text, textWidth, LineHeight)));
        }
    }
}
