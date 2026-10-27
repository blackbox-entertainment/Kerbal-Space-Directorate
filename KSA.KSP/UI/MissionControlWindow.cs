using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>Mission Control (Jira KSA-36): what is flying now and the reports of every mission flown, newest first.</summary>
    public sealed class MissionControlWindow
    {
        private const string DialogName = "KSA_MissionControl";
        private const float Width = 720f;
        private const float WindowHeight = 660f;
        private const float ListHeight = 540f;
        private const float LabelWidth = 110f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const int ReportsShown = 30;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;

        public MissionControlWindow(Agency agency, Action onClosed)
        {
            if (agency == null) throw new ArgumentNullException(nameof(agency));
            _agency = agency;
            _onClosed = onClosed;
        }

        public bool IsOpen { get { return _dialog != null; } }

        public void Open()
        {
            if (_dialog != null) return;
            Spawn();
        }

        public void Close()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
        }

        private void Refresh()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog;
            _dialog = null;
            d.Dismiss();
            Spawn();
        }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            KsaCore.Log.Info("Mission Control window: " + _agency.Reports.Count + " report(s)");

            int inFlight = 0, preparing = 0, ready = 0;
            foreach (Project p in _agency.Projects)
            {
                switch (p.Status)
                {
                    case ProjectStatus.InFlight: inFlight++; break;
                    case ProjectStatus.Design:
                    case ProjectStatus.Build: preparing++; break;
                    case ProjectStatus.Ready: ready++; break;
                }
            }
            items.Add(Field("Projects", inFlight + " in flight  |  " + preparing + " in design or build  |  " + ready + " ready to launch", textWidth));

            Section(items, "Mission reports", textWidth);
            if (_agency.Reports.Count == 0) Line(items, "No missions flown yet.", textWidth);
            int shown = 0;
            for (int i = _agency.Reports.Count - 1; i >= 0 && shown < ReportsShown; i--, shown++)
            {
                MissionReport r = _agency.Reports[i];
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    new DialogGUILabel("<b>Day " + r.Day + "  " + r.ProjectName + "</b>", 300f, LineHeight),
                    new DialogGUILabel(Agency.OutcomeText(r.Outcome) + "  |  " + (r.Mode == KSA.Core.Missions.MissionMode.WatchLive ? "live" : "simulated"), textWidth - 306f, LineHeight)));
                Line(items, "Crew: " + Names(r.Crew), textWidth);
                foreach (string line in r.Lines) Line(items, "    " + line, textWidth);
                if (r.Achievements.Count > 0) Line(items, "Achievements: " + string.Join(", ", r.Achievements.ToArray()), textWidth);
                if (r.Deaths.Count > 0) Line(items, "<color=#ff8080>Deaths: " + Names(r.Deaths) + "</color>", textWidth);
                Line(items, "<color=#aaaaaa>Design " + Percent(r.DesignScore) + "  |  crew " + Percent(r.CrewScore) + "  |  success chance " + Percent(r.SuccessChance) + "</color>", textWidth);
            }
            if (_agency.Reports.Count > ReportsShown) Line(items, "(" + (_agency.Reports.Count - ReportsShown) + " older report(s) not shown)", textWidth);
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, items.ToArray()));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Every mission the agency has flown, newest first.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Mission Control", HighLogic.UISkin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, HighLogic.UISkin, false);
        }

        private void OnCloseClicked()
        {
            _dialog = null;
            if (_onClosed != null) _onClosed();
        }

        // --- text helpers ---

        private string Names(List<string> ids)
        {
            if (ids == null || ids.Count == 0) return "none";
            var names = new List<string>();
            foreach (string id in ids)
            {
                CrewProfile p = _agency.Crew.Get(id);
                names.Add(p != null ? p.Name : id);
            }
            return string.Join(", ", names.ToArray());
        }

        private static string Percent(double v)
        {
            return ((int)Math.Round(v * 100.0)) + "%";
        }

        // --- layout helpers: every line has an explicit height so rows never overlap ---

        private static DialogGUIHorizontalLayout Field(string label, string value, float textWidth)
        {
            return new DialogGUIHorizontalLayout(false, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("<b>" + label + "</b>", LabelWidth, LineHeight),
                new DialogGUILabel(value, textWidth - LabelWidth, LineHeight));
        }

        private static void Section(List<DialogGUIBase> items, string title, float textWidth)
        {
            items.Add(new DialogGUISpace(SectionGap));
            items.Add(new DialogGUILabel("<b>" + title + "</b>", textWidth, LineHeight + 2f));
        }

        private static void Line(List<DialogGUIBase> items, string text, float textWidth)
        {
            items.Add(new DialogGUIHorizontalLayout(false, false, 0f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUISpace(16f),
                new DialogGUILabel(text, textWidth - 16f, LineHeight)));
        }
    }
}
