using System;
using System.Collections.Generic;
using KSA.Core;
using KSA.Core.Ai;
using KSA.KSP.Ai;
using UnityEngine;

namespace KSA.KSP.UI
{
    /// <summary>
    /// The AI Director (KSA-68): hand the agency to a model and watch. On/off, which model, which flights fly live,
    /// Think now, the standing plan, and the log of every decision with its reason. The player keeps time warp and
    /// every other window; switching off hands the agency straight back.
    /// </summary>
    public sealed class AiDirectorWindow
    {
        private const string DialogName = "KSA_AiDirector";
        private const float Width = 760f;
        private static float WindowHeight { get { return Mathf.Clamp(Screen.height - 160f, 560f, 900f); } }
        private static float ListHeight { get { return WindowHeight - 120f; } }
        private const float LineHeight = 22f;
        private const float ButtonHeight = 26f;
        private const string Green = "#8be28b";
        private const string Grey = "#aaaaaa";
        private const string Amber = "#ffd27f";
        private const string Red = "#ff8080";
        private const int LogShown = 60;

        private readonly Agency _agency;
        private readonly Action _onClosed;
        private PopupDialog _dialog;
        private string _status = string.Empty;
        private bool _showBriefing;

        public AiDirectorWindow(Agency agency, Action onClosed)
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
            PopupDialog d = _dialog; _dialog = null; d.Dismiss();
        }

        private void Refresh()
        {
            if (_dialog == null) return;
            PopupDialog d = _dialog; _dialog = null; d.Dismiss();
            Spawn();
        }

        private void Spawn()
        {
            float textWidth = Width - 60f;
            var items = new List<DialogGUIBase>();
            AiSettings ai = _agency.Ai ?? new AiSettings();
            LlmEndpoint endpoint = ai.DirectorEndpoint();
            bool on = _agency.DirectorOn;

            Line(items, "Hand the agency to an AI and watch. It hires, interviews, staffs the lab and departments, researches, starts projects and programs, sends resupplies and rescues, and launches missions. Every action goes through the same checks as your clicks. You keep time warp and can take over at any moment.", textWidth);
            items.Add(new DialogGUISpace(4f));

            // On / off.
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 2, 2), TextAnchor.MiddleLeft,
                new DialogGUILabel("<b>AI Director: " + (on ? Color("ON", Green) : Color("off", Grey)) + "</b>", 180f, LineHeight),
                new DialogGUIButton(on ? "Take over (I am the Director)" : "Hand the agency to the AI", OnToggle, 260f, 30f, false),
                new DialogGUIButton("Think now", OnThinkNow, 110f, 30f, false),
                new DialogGUILabel(() => LiveStatus(), textWidth - 580f, LineHeight)));
            if (endpoint == null)
                Line(items, Color("No model to think with. Open AI Setup and add an OpenRouter API key (recommended), or set up a local model.", Amber), textWidth);

            // Model.
            items.Add(new DialogGUISpace(6f));
            Line(items, "<b>Model</b>  (OpenRouter; your API key from AI Setup)  now: " + Color(endpoint != null ? endpoint.Model + (endpoint.IsLocal ? " (local)" : string.Empty) : "none", endpoint != null ? Green : Amber), textWidth);
            // Without a readable key the Director falls back to the local model, which is slow and weak at this job (2026-10-28).
            if (!ai.HasApiKey)
                Line(items, Color("No OpenRouter key could be read, so the Director " + (endpoint != null ? "is using the local model (slow; small models often fail at this)" : "cannot think") + ". Enter your key again in AI Setup and save.", Amber), textWidth);
            string current = ai.DirectorModel.Length > 0 ? ai.DirectorModel : AiSettings.DefaultDirectorModel;
            foreach (DirectorModels.Preset p in DirectorModels.Presets)
            {
                string id = p.Id;
                bool sel = string.Equals(current, id, StringComparison.OrdinalIgnoreCase);
                items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(16, 0, 0, 0), TextAnchor.MiddleLeft,
                    new DialogGUIButton((sel ? "> " : "") + p.Label, () => OnModel(id), 220f, ButtonHeight, false),
                    new DialogGUILabel(Color(p.Note + "  (" + id + ")", Grey), textWidth - 240f, LineHeight)));
            }

            // Flights.
            items.Add(new DialogGUISpace(6f));
            Line(items, "<b>Flights</b>  " + Agency.FlightsText(_agency.State.DirectorFlights) + ".", textWidth);
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(16, 0, 0, 0), TextAnchor.MiddleLeft,
                FlightsButton(DirectorFlights.Simulate, "Simulate everything"),
                FlightsButton(DirectorFlights.Firsts, "Firsts live"),
                FlightsButton(DirectorFlights.All, "Everything live")));
            Line(items, Color("Live flights roll out to the pad at the Space Center, the AI gives the go after a " + _agency.Settings.Director.GoSeconds + " s countdown (press LAUNCH to go sooner), the crew flies, and the game comes back to the Space Center afterwards. Touch the controls to take over a flight. Leave the game at the Space Center (any time warp) between flights. This sets the mode of the projects the AI starts; the Switch button on a project in the Projects window still changes it.", Grey), textWidth);

            // Plan.
            items.Add(new DialogGUISpace(6f));
            Line(items, "<b>The Director's plan</b>  " + (_agency.DirectorPlan.Length > 0 ? _agency.DirectorPlan : Color("none yet: it writes one on its first turn.", Grey)), textWidth);
            Line(items, Color("Turns: every " + _agency.Settings.Director.TurnIntervalDays + " days, sooner when a mission ends, an applicant arrives, the year closes or a crew is stranded. Spend in the last 30 days: $" + _agency.Costs.SpendLast30Days(_agency.CurrentDay).ToString("0.000") + " (cap $" + ai.MonthlySpendCapUsd.ToString("0.##") + " in AI Setup).", Grey), textWidth);

            // Briefing or log.
            items.Add(new DialogGUISpace(6f));
            items.Add(new DialogGUIHorizontalLayout(false, false, 6f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleLeft,
                new DialogGUILabel("<b>" + (_showBriefing ? "What the AI sees this turn" : "Decisions (newest first)") + "</b>", 300f, LineHeight),
                new DialogGUIButton(_showBriefing ? "Show the log" : "Show the briefing", OnToggleBriefing, 160f, ButtonHeight, false),
                new DialogGUIButton("Refresh", Refresh, 90f, ButtonHeight, false)));
            if (_showBriefing)
            {
                foreach (string line in _agency.DirectorBriefing().Split('\n'))
                {
                    string l = line.TrimEnd('\r');
                    if (l.Length > 0) Line(items, Color(l, Grey), textWidth);
                }
            }
            else
            {
                List<DirectorLogEntry> log = _agency.DirectorLog;
                if (log.Count == 0) Line(items, Color("Nothing yet.", Grey), textWidth);
                int shown = 0;
                for (int i = log.Count - 1; i >= 0 && shown < LogShown; i--, shown++)
                {
                    DirectorLogEntry e = log[i];
                    string colour = e.Kind == "Action" ? Green : e.Kind == "Failed" ? Red : e.Kind == "Thought" ? "#ffffff" : Grey;
                    Line(items, Color("Y" + e.Year + " d" + e.Day + "  " + e.Kind.ToLowerInvariant() + ": ", Grey) + Color(e.Text, colour), textWidth);
                }
            }

            var list = new DialogGUIScrollList(new Vector2(Width - 30f, ListHeight), false, true,
                new DialogGUIVerticalLayout(false, false, 3f, new RectOffset(6, 6, 6, 6), TextAnchor.UpperLeft, KsaUi.Scrollable(items)));
            var content = new DialogGUIVerticalLayout(true, true, 4f, new RectOffset(4, 4, 4, 4), TextAnchor.UpperLeft,
                new DialogGUILabel(_status.Length > 0 ? _status : "The AI Director.", Width - 40f, LineHeight),
                list,
                new DialogGUIHorizontalLayout(true, false, 4f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleRight,
                    HelpWindow.Button("director"),
                    new DialogGUIFlexibleSpace(),
                    new DialogGUIButton("Close", OnCloseClicked, 90f, 30f, true)));
            var dialog = new MultiOptionDialog(DialogName, "", "AI Director", KsaUi.Skin, new Rect(0.5f, 0.5f, Width, WindowHeight), content);
            _dialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), dialog, false, KsaUi.Skin, false);
        }

        private DialogGUIButton FlightsButton(DirectorFlights f, string label)
        {
            bool sel = _agency.State.DirectorFlights == f;
            return new DialogGUIButton((sel ? "> " : "") + label, () => { _agency.SetDirectorFlights(f); _status = Agency.FlightsText(f); Refresh(); }, 190f, ButtonHeight, false);
        }

        private string LiveStatus()
        {
            if (!_agency.DirectorOn) return Color("You are the Director.", Grey);
            if (_agency.DirectorThinking) return Color("Thinking...", Amber);
            int d = _agency.DaysToDirectorTurn();
            return d <= 0 ? Color("Turn due", Amber) : Color("Next turn in " + d + " day" + (d == 1 ? "" : "s"), Green);
        }

        private void OnToggle()
        {
            string msg;
            _agency.SetDirector(!_agency.DirectorOn, out msg);
            _status = msg;
            KsaCore.Log.Info("AI Director: " + msg);
            Refresh();
        }

        private void OnThinkNow()
        {
            if (!_agency.DirectorOn) { _status = Color("Hand the agency to the AI first.", Amber); Refresh(); return; }
            _status = _agency.TakeDirectorTurn() ? "Thinking... the decisions appear here in a few seconds (press Refresh)." : Color("Could not take a turn now: see the log.", Amber);
            Refresh();
        }

        private void OnModel(string id)
        {
            AiSettings ai = _agency.Ai;
            if (ai == null) return;
            ai.DirectorModel = id;
            string error;
            if (KsaSettingsFile.Save(ai, out error)) { KsaCore.ReloadAiSettings(); _status = "Director model: " + id; }
            else _status = Color("Could not save the setting: " + error, Amber);
            Refresh();
        }

        private void OnToggleBriefing() { _showBriefing = !_showBriefing; Refresh(); }

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
