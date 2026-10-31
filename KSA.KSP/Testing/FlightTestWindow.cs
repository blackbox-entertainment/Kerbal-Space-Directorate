using System;
using System.Collections.Generic;
using KSA.KSP.UI;
using UnityEngine;

namespace KSA.KSP.Testing
{
    /// <summary>Starts and stops the flight test bench and shows how far the ladder got (sandbox saves only).</summary>
    public sealed class FlightTestWindow
    {
        private const string DialogName = "KSA_FlightTests";
        private const float Width = 640f;
        private const float LineHeight = 22f;
        private PopupDialog _dialog;
        private readonly Action _onClosed;

        public FlightTestWindow(Action onClosed) { _onClosed = onClosed; }

        public bool IsOpen { get { return _dialog != null; } }
        public void Open() { if (_dialog == null) Spawn(); }

        public void Close()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Refresh() { Close(); Spawn(); }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            Line(items, "Flies each mission live, one after another, with no one at the controls. It stops at the first failure. Results go to:", textWidth);
            Line(items, "<color=#888888>" + FlightTestBench.ReportPath + "</color>", textWidth);
            string blocker = FlightTestBench.Blocker();
            if (blocker != null && !FlightTestBench.Active) Line(items, "<color=#ffd27f>Cannot run here: " + blocker + ".</color>", textWidth);
            items.Add(new DialogGUISpace(6f));
            for (int i = 0; i < FlightTestBench.Ladder.Length; i++)
            {
                string id = FlightTestBench.Ladder[i];
                string result = FlightTestBench.Results.Find(r => r.StartsWith("PASS " + id + ":") || r.StartsWith("FAIL " + id + ":"));
                string mark = result != null ? (result.StartsWith("PASS") ? "<color=#8be28b>" + result + "</color>" : "<color=#ff8080>" + result + "</color>")
                    : FlightTestBench.Active && i == FlightTestBench.Index ? "<color=#ffd27f>" + id + ": flying now</color>" : "<color=#888888>" + id + "</color>";
                Line(items, (i + 1) + ". " + mark, textWidth);
            }
            var list = new DialogGUIScrollList(new Vector2(Width - 30f, 360f), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));
            var buttons = new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUIButton("Start from the first", () => { string m; FlightTestBench.Start(0, out m); ScreenMessages.PostScreenMessage("[KSA] " + m, 8f, ScreenMessageStyle.UPPER_CENTER); Refresh(); }, 170f, 30f, false),
                new DialogGUIButton("Resume", () => { string m; FlightTestBench.Start(FlightTestBench.Index, out m); ScreenMessages.PostScreenMessage("[KSA] " + m, 8f, ScreenMessageStyle.UPPER_CENTER); Refresh(); }, 90f, 30f, false),
                new DialogGUIButton("Stop", () => { FlightTestBench.Stop("stopped by the Director"); Refresh(); }, 80f, 30f, false),
                new DialogGUIButton("Refresh", Refresh, 90f, 30f, false),
                new DialogGUIFlexibleSpace(),
                new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true));
            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft, list, buttons);
            var dialog = new MultiOptionDialog(DialogName, "", "Flight tests", KsaUi.Skin, new Rect(0.5f, 0.5f, Width, 470f), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        private static void Line(List<DialogGUIBase> items, string text, float textWidth)
        {
            items.Add(new DialogGUILabel(text, textWidth, KsaUi.LabelHeight(text, textWidth, LineHeight)));
        }
    }
}
