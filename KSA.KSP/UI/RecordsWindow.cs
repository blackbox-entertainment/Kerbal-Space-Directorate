using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Rivals;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Records (2026-10-28): every first on the milestone ladder with the kerbals who flew it (or the rival that got there
    /// first), and the agency's record book: best flight and career figures, who holds them and when they were set.
    /// Read-only.
    /// </summary>
    public sealed class RecordsWindow
    {
        private const string DialogName = "KSA_Records";
        private const float Width = 860f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 200f, 520f, 860f); } }
        private static float ListHeight { get { return WindowHeight - 110f; } }
        private const float LineHeight = 22f;
        private const string Green = "#8be28b";
        private const string Grey = "#888888";
        private const string Amber = "#ffd27f";
        private const string Red = "#ff8080";

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;

        public RecordsWindow(Agency agency, Action onClosed)
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

            items.Add(new DialogGUILabel("<b>Agency records</b>", textWidth, LineHeight + 2f));
            int set = 0;
            foreach (Agency.RecordKind k in Agency.RecordKinds)
            {
                RecordEntry e;
                if (_agency.Records.TryGetValue(k.Key, out e))
                {
                    set++;
                    Line(items, "<b>" + k.Title + ":</b> " + Color(k.Format(e.Value), Green) + "  " + e.Holder
                        + (e.Mission.Length > 0 ? ", " + e.Mission : string.Empty) + Color("  (year " + e.Year + (e.Day > 0 ? ", day " + e.Day : string.Empty) + ")", Grey), textWidth);
                }
                else Line(items, "<b>" + k.Title + ":</b> " + Color("not set yet", Grey), textWidth);
            }
            if (set == 0) Line(items, Color("Records are set by live flights (altitude, speed, distance) and by every mission (days in space, missions, worlds).", Grey), textWidth);

            items.Add(new DialogGUISpace(10f));
            items.Add(new DialogGUILabel("<b>Firsts</b>", textWidth, LineHeight + 2f));
            int ours = 0, theirs = 0, open = 0;
            foreach (MilestoneLadder.Rung rung in MilestoneLadder.Build(_agency.Catalogs.Missions))
            {
                FirstClaim f = _agency.FirstFor(rung.Achievement);
                string what = "First " + MilestoneLadder.Describe(rung.Achievement);
                if (f == null) { open++; Line(items, Color(what + ": open", Grey), textWidth); continue; }
                bool mine = f.ClaimedBy == Agency.PlayerId;
                if (mine) ours++; else theirs++;
                Line(items, "<b>" + what + ":</b> " + Color(_agency.FirstByText(f), mine ? Green : Red), textWidth);
            }
            Line(items, Color(ours + " firsts are ours, " + theirs + " went to rivals, " + open + " are still open.", Amber), textWidth);

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));
            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel("Who did it first, and the agency's best.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    HelpWindow.Button("records"),
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));
            var dialog = new MultiOptionDialog(DialogName, "", "Records", KsaUi.Skin, new Rect(0.5f, 0.5f, Width, WindowHeight), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
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
