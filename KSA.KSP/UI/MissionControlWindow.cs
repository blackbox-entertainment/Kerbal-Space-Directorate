using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Missions;
using KSA.Core.Personnel;
using KSA.Core.Projects;
using KSA.KSP.Flight;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// Mission Control (Jira KSA-36): the pad (projects ready to launch), live flights in progress, and the reports
    /// of every mission flown, newest first. Launching live switches to the Flight scene; the window just reports it.
    /// </summary>
    public sealed class MissionControlWindow
    {
        private const string DialogName = "KSA_MissionControl";
        private const float Width = 720f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 140f, 660f, 960f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LabelWidth = 110f;
        private const float LineHeight = 22f;
        private const float SectionGap = 12f;
        private const float ButtonHeight = 26f;
        private const int ReportsShown = 30;
        private const int FlightLogLinesInFlight = 3;
        private const int FlightLogLinesInReport = 8;
        private const int WrapWidth = 100;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;
        private string _confirmResolve;

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
            KsaCore.RolloutHold = true;
            Spawn();
        }

        public void Close()
        {
            if (_dialog == null) return;
            KsaCore.RolloutHold = false;
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
            var readyProjects = new List<Project>();
            var liveProjects = new List<Project>();
            foreach (Project p in _agency.Projects)
            {
                switch (p.Status)
                {
                    case ProjectStatus.InFlight:
                        inFlight++;
                        if (p.Mode == MissionMode.WatchLive) liveProjects.Add(p);
                        break;
                    case ProjectStatus.Design:
                    case ProjectStatus.Build: preparing++; break;
                    case ProjectStatus.Ready: ready++; readyProjects.Add(p); break;
                }
            }
            items.Add(Field("Projects", inFlight + " in flight  |  " + preparing + " in design or build  |  " + ready + " ready to launch", textWidth));

            // --- Pad and flights ---
            Section(items, "Pad and flights", textWidth);
            if (readyProjects.Count == 0 && liveProjects.Count == 0) Line(items, "Nothing on the pad and nothing in the air.", textWidth);
            foreach (Project p in readyProjects)
            {
                string id = p.Id;
                bool live = p.Mode == MissionMode.WatchLive && _agency.LiveBlocker(p) == null;
                DialogGUIButton launch = live
                    ? new DialogGUIButton("Launch live", () => OnLaunchLive(id), 120f, ButtonHeight, false)
                    : new DialogGUIButton("Launch now", () => OnLaunchNow(id), 120f, ButtonHeight, false);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    launch,
                    new DialogGUILabel("<b>" + p.Name + "</b>", 260f, LineHeight),
                    new DialogGUILabel("ready  |  " + ModeText(p.Mode), textWidth - 392f, LineHeight)));
                if (p.Mode == MissionMode.WatchLive && !live)
                {
                    string why = _agency.LiveBlocker(p);
                    Line(items, "<color=#aaaaaa>Live flight unavailable (" + (why ?? "unknown reason") + "); Launch now simulates instead.</color>", textWidth);
                }
            }
            foreach (Project p in liveProjects)
            {
                string id = p.Id;
                bool flyingNow = KsaFlightDirector.Live != null && string.Equals(KsaFlightDirector.Live.ProjectId, id, StringComparison.OrdinalIgnoreCase);
                DialogGUIBase action;
                if (flyingNow) action = new DialogGUILabel("<color=#8be28b>flying now</color>", 150f, LineHeight);
                else if (string.Equals(_confirmResolve, id, StringComparison.OrdinalIgnoreCase))
                    action = new DialogGUIButton("Confirm resolve?", () => OnConfirmResolve(id), 150f, ButtonHeight, false);
                else action = new DialogGUIButton("Resolve as simulated", () => OnAskResolve(id), 150f, ButtonHeight, false);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    action,
                    new DialogGUILabel("<b>" + p.Name + "</b>", 260f, LineHeight),
                    new DialogGUILabel("in flight since day " + p.LaunchDay + (p.DirectorFlown ? "  |  <color=#ffb347>Director-flown</color>" : string.Empty), textWidth - 422f, LineHeight)));
                if (p.FlightLog.Count == 0) Line(items, "<color=#aaaaaa>    No telemetry yet.</color>", textWidth);
                for (int i = Math.Max(0, p.FlightLog.Count - FlightLogLinesInFlight); i < p.FlightLog.Count; i++)
                    Line(items, "<color=#aaaaaa>    " + p.FlightLog[i] + "</color>", textWidth);
            }

            // --- Reports ---
            Section(items, "Mission reports", textWidth);
            if (_agency.Reports.Count == 0) Line(items, "No missions flown yet.", textWidth);
            int shown = 0;
            for (int i = _agency.Reports.Count - 1; i >= 0 && shown < ReportsShown; i--, shown++)
            {
                MissionReport r = _agency.Reports[i];
                string modeLine = Agency.OutcomeText(r.Outcome) + "  |  " + (r.Mode == MissionMode.WatchLive ? "Live flight" : "Simulated")
                    + (r.DirectorFlown ? "  |  <color=#ffb347>Director-flown</color>" : string.Empty);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 4, 0), TextAnchor.MiddleLeft,
                    new DialogGUILabel("<b>Day " + r.Day + "  " + r.ProjectName + "</b>", 300f, LineHeight),
                    new DialogGUILabel(modeLine, textWidth - 306f, LineHeight)));
                Line(items, "Crew: " + Names(r.Crew), textWidth);
                foreach (string line in r.Lines) Line(items, "    " + line, textWidth);
                if (r.Achievements.Count > 0) Line(items, "Achievements: " + string.Join(", ", r.Achievements.ToArray()), textWidth);
                if (r.Deaths.Count > 0) Line(items, "<color=#ff8080>Deaths: " + Names(r.Deaths) + "</color>", textWidth);
                Line(items, "<color=#aaaaaa>Design " + Percent(r.DesignScore) + "  |  crew " + Percent(r.CrewScore) + "  |  success chance " + Percent(r.SuccessChance) + "</color>", textWidth);
                if (r.Mode == MissionMode.WatchLive)
                {
                    Project owner = _agency.GetProject(r.ProjectId);
                    if (owner != null && owner.FlightLog.Count > 0)
                    {
                        Line(items, "Flight log:", textWidth);
                        int start = Math.Max(0, owner.FlightLog.Count - FlightLogLinesInReport);
                        if (start > 0) Line(items, "<color=#aaaaaa>    (" + start + " earlier line(s) not shown)</color>", textWidth);
                        for (int k = start; k < owner.FlightLog.Count; k++)
                            Line(items, "<color=#aaaaaa>    " + owner.FlightLog[k] + "</color>", textWidth);
                    }
                }
                if (!string.IsNullOrEmpty(r.PostMortem))
                {
                    Line(items, "Post-mortem:", textWidth);
                    foreach (string w in Wrap(r.PostMortem, WrapWidth))
                        Line(items, "<color=#aaaaaa>    " + w + "</color>", textWidth);
                }
            }
            if (_agency.Reports.Count > ReportsShown) Line(items, "(" + (_agency.Reports.Count - ReportsShown) + " older report(s) not shown)", textWidth);
            items.Add(new DialogGUISpace(SectionGap));

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));

            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "Launch what is ready, watch what is flying, and read every mission the agency has flown, newest first.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));

            var dialog = new MultiOptionDialog(DialogName, "", "Mission Control", KsaUi.Skin,
                new Rect(0.5f, 0.5f, Width, WindowHeight), content);

            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        // --- Director actions: call the Agency, remember its message, rebuild ---

        private void OnLaunchLive(string projectId)
        {
            string message;
            bool ok = _agency.LaunchLive(projectId, out message);
            _status = message ?? string.Empty;
            if (ok)
            {
                // The host is switching to the Flight scene; do not rebuild on top of a scene change.
                KsaCore.Log.Info("Mission Control: " + _status);
                return;
            }
            KsaCore.Log.Warn("Mission Control: " + _status);
            Refresh();
        }

        private void OnLaunchNow(string projectId)
        {
            string message;
            bool ok = _agency.LaunchNow(projectId, out message);
            _status = message ?? string.Empty;
            if (ok) KsaCore.Log.Info("Mission Control: " + _status);
            else KsaCore.Log.Warn("Mission Control: " + _status);
            // LaunchNow routes live projects to LaunchLive (scene change): only rebuild when the project is still ours to show.
            Project p = _agency.GetProject(projectId);
            if (ok && p != null && p.Status == ProjectStatus.InFlight && p.Mode == MissionMode.WatchLive) return;
            Refresh();
        }

        private void OnAskResolve(string projectId)
        {
            _confirmResolve = projectId;
            Project p = _agency.GetProject(projectId);
            _status = "Resolve " + (p != null ? p.Name : projectId) + " as a simulated mission? Click Confirm resolve?. The live flight is abandoned.";
            Refresh();
        }

        private void OnConfirmResolve(string projectId)
        {
            _confirmResolve = null;
            string message;
            bool ok = _agency.SimulateInstead(projectId, out message);
            _status = message ?? string.Empty;
            if (ok) KsaCore.Log.Info("Mission Control: " + _status);
            else KsaCore.Log.Warn("Mission Control: " + _status);
            Refresh();
        }

        private void OnCloseClicked()
        {
            KsaCore.RolloutHold = false;
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

        private static string ModeText(MissionMode mode)
        {
            return mode == MissionMode.WatchLive ? "Watch Live" : "Simulate";
        }

        private static string Percent(double v)
        {
            return ((int)Math.Round(v * 100.0)) + "%";
        }

        /// <summary>Word-wraps a paragraph into lines of at most <paramref name="width"/> characters (newlines break too).</summary>
        private static List<string> Wrap(string text, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            foreach (string para in text.Replace("\r", string.Empty).Split('\n'))
            {
                string current = string.Empty;
                foreach (string word in para.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (current.Length == 0) current = word;
                    else if (current.Length + 1 + word.Length <= width) current += " " + word;
                    else { lines.Add(current); current = word; }
                }
                if (current.Length > 0) lines.Add(current);
            }
            return lines;
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
                new DialogGUILabel(text, textWidth - 16f, KsaUi.LabelHeight(text, textWidth - 16f, LineHeight))));
        }
    }
}
